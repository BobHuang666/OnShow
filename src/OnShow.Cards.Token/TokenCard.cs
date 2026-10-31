using System.Text.Json;
using OnShow.Core;
using OnShow.Core.Cards;

namespace OnShow.Cards.Token;

/// <summary>
/// Token 监控卡:按 Provider 轮询各账户,快照写入行 VM;
/// 失败保留上次快照 + 错误文案,不静默;余额型按当日基线本地累计「今日消耗」。
/// </summary>
public sealed class TokenCard : ICard
{
    public CardMetadata Metadata { get; } = new("token", "Token 监控", "🔑");

    public event Action? ViewModelChanged;

    static string DataFile => Path.Combine(AppPaths.Root, "token.json");

    readonly object _gate = new();
    ICardHost _host = null!;
    List<TokenAccount> _accounts = new();
    TokenState _state = new();
    List<TokenRowVm> _rows = new();
    System.Threading.Timer? _timer;
    SynchronizationContext? _ui;
    int _polling;
    DateTime _lastRefresh = DateTime.MinValue;

    public void OnLoaded(ICardHost host)
    {
        _host = host;
        _ui = SynchronizationContext.Current;
        _accounts = Json.Load<List<TokenAccount>>(DataFile);
        _state = host.State.Get<TokenState>("token", new TokenState());
        MigrateOrder();
        RebuildRows();
        _timer = new System.Threading.Timer(
            _ => _ = RefreshAsync(),
            null, TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(Math.Max(1, host.Config.TokenPollMinutes)));
        Log.Info($"Token 卡加载:{_accounts.Count} 个账户");
    }

    // ---------- 查询 ----------

    public IReadOnlyList<TokenRowVm> Rows { get { lock (_gate) return _rows.ToList(); } }
    public DateTime LastRefresh => _lastRefresh;
    public object GetViewModel() => Rows;

    // ---------- 账户操作(设置窗口调用) ----------

    public void Upsert(TokenAccount account, Dictionary<string, string>? credentials)
    {
        lock (_gate)
        {
            var idx = _accounts.FindIndex(x => x.Id == account.Id);
            if (idx >= 0) _accounts[idx] = account;
            else
            {
                if (account.Order == 0) account.Order = _accounts.Count == 0 ? 0 : _accounts.Max(a => a.Order) + 1;
                _accounts.Add(account);
            }
            Json.Save(DataFile, _accounts);
            RebuildRows();
        }
        if (credentials is not null) SecretStore.Set(account.Id, credentials);
        _ = RefreshOneAsync(account.Id);
    }

    /// <summary>设置页拖拽排序:按给定 id 顺序重写 Order 并落盘。</summary>
    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        lock (_gate)
        {
            for (var i = 0; i < orderedIds.Count; i++)
            {
                var account = _accounts.FirstOrDefault(x => x.Id == orderedIds[i]);
                if (account is not null) account.Order = i;
            }
            Json.Save(DataFile, _accounts);
            RebuildRows();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _accounts.RemoveAll(a => a.Id == id);
            _state.Last.Remove(id);
            _state.Baselines.Remove(id);
            Json.Save(DataFile, _accounts);
            RebuildRows();
        }
        SecretStore.Remove(id);
        PersistState();
    }

    public void Reload()
    {
        lock (_gate)
        {
            _accounts = Json.Load<List<TokenAccount>>(DataFile);
            MigrateOrder();
            RebuildRows();
        }
    }

    /// <summary>旧数据迁移:全部 Order 均为 0 时,按名称初始化排序号。</summary>
    void MigrateOrder()
    {
        if (_accounts.Count == 0 || _accounts.Any(a => a.Order != 0)) return;
        var ordered = _accounts.OrderBy(a => a.Enabled ? 0 : 1).ThenBy(a => a.Name).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        Json.Save(DataFile, _accounts);
    }

    /// <summary>配置里轮询间隔变化后重排定时器。</summary>
    public void Reschedule(int minutes)
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(
            _ => _ = RefreshAsync(),
            null, TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(Math.Max(1, minutes)));
        Log.Info($"Token 轮询间隔 -> {minutes} 分钟");
    }

    // ---------- 刷新 ----------

    public async Task RefreshOneAsync(string accountId)
    {
        TokenAccount? account;
        lock (_gate) account = _accounts.FirstOrDefault(x => x.Id == accountId && x.Enabled);
        if (account is null) return;
        var snap = await FetchAccountAsync(account, CancellationToken.None);
        Apply(account.Id, snap);
        _lastRefresh = DateTime.Now;
        PersistState();
        PostRaise();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            TokenAccount[] list;
            lock (_gate) list = _accounts.Where(a => a.Enabled).ToArray();
            if (list.Length > 0)
            {
                var snaps = await Task.WhenAll(list.Select(a => FetchAccountAsync(a, ct)));
                foreach (var s in snaps) Apply(s.AccountId, s);
            }
            _lastRefresh = DateTime.Now;
            PersistState();
            PostRaise();
        }
        catch (Exception ex)
        {
            Log.Error("Token 轮询异常", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    async Task<TokenSnapshot> FetchAccountAsync(TokenAccount account, CancellationToken ct)
    {
        try
        {
            var provider = ProviderRegistry.Get(account.ProviderId) ?? ProviderRegistry.Get("manual")!;
            var creds = SecretStore.Get(account.Id);
            return await provider.FetchAsync(account, creds, ct);
        }
        catch (OperationCanceledException)
        {
            return TokenSnapshot.Fail(account.Id, "已取消");
        }
        catch (Exception ex)
        {
            Log.Error($"[{account.ProviderId}:{account.Name}] 拉取失败", ex);
            return TokenSnapshot.Fail(account.Id, Friendly(ex));
        }
    }

    static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException h => h.Message.Length > 80 ? h.Message[..80] + "…" : h.Message,
        TaskCanceledException => "请求超时",
        JsonException => "响应不是有效 JSON",
        _ => ex.Message,
    };

    // ---------- 快照应用 ----------

    void Apply(string accountId, TokenSnapshot snap)
    {
        lock (_gate)
        {
            // 余额型:当日基线 → 今日消耗
            if (snap.Kind == SnapshotKind.Balance && snap.Balance is not null && snap.Error is null)
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                var baseline = _state.Baselines.TryGetValue(accountId, out var b) ? b : null;
                if (baseline is null || baseline.Date != today)
                    _state.Baselines[accountId] = new DayBaseline { Date = today, DayStartBalance = snap.Balance.Value };
            }

            _state.Last[accountId] = SavedSnapshot.From(snap);

            var row = _rows.FirstOrDefault(r => r.Account.Id == accountId);
            if (row is null) return;
            var spend = DaySpend(accountId, snap);
            RunOnUi(() => row.Apply(snap, spend));
        }
    }

    decimal? DaySpend(string accountId, TokenSnapshot snap)
    {
        if (snap.Kind != SnapshotKind.Balance || snap.Balance is null) return null;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_state.Baselines.TryGetValue(accountId, out var b) || b.Date != today) return null;
        return b.DayStartBalance - snap.Balance.Value;
    }

    void RebuildRows()
    {
        _rows = _accounts
            .OrderBy(a => a.Enabled ? 0 : 1)
            .ThenBy(a => a.Order)
            .ThenBy(a => a.Name)
            .Select(a =>
            {
                var vm = new TokenRowVm
                {
                    Account = a,
                    Provider = ProviderRegistry.Get(a.ProviderId) ?? ProviderRegistry.Get("manual")!,
                };
                if (_state.Last.TryGetValue(a.Id, out var saved) && saved.FetchedAt > DateTime.Now.AddDays(-7))
                    vm.Apply(saved.ToSnapshot(a.Id), null);   // 离线兜底:恢复缓存快照
                else
                    vm.Apply(TokenSnapshot.Fail(a.Id, "尚未刷新"), null);
                return vm;
            })
            .ToList();
        PostRaise();
    }

    void RunOnUi(Action action)
    {
        if (_ui is not null) _ui.Post(_ => action(), null);
        else action();
    }

    void PostRaise() => RunOnUi(() => ViewModelChanged?.Invoke());

    void PersistState()
    {
        _host.State.Set("token", _state);
        _host.State.Flush();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        try { PersistState(); } catch { /* 退出阶段尽力而为 */ }
    }
}

/// <summary>Token 卡持久化状态(state.json 的 "token" 节)。</summary>
public class TokenState
{
    /// <summary>最近快照(重启后离线兜底显示)。</summary>
    public Dictionary<string, SavedSnapshot> Last { get; set; } = new();

    /// <summary>余额型账户的当日基线(今日消耗 = 基线 − 当前余额)。</summary>
    public Dictionary<string, DayBaseline> Baselines { get; set; } = new();
}

public class SavedSnapshot
{
    public SnapshotKind Kind { get; set; }
    public string Currency { get; set; } = "";
    public decimal? Balance { get; set; }
    public decimal? Used { get; set; }
    public decimal? Limit { get; set; }
    public decimal? UsedTokens { get; set; }
    public string? Period { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public static SavedSnapshot From(TokenSnapshot s) => new()
    {
        Kind = s.Kind,
        Currency = s.Currency,
        Balance = s.Balance,
        Used = s.Used,
        Limit = s.Limit,
        UsedTokens = s.UsedTokens,
        Period = s.Period,
        FetchedAt = s.FetchedAt,
        ExpiresAt = s.ExpiresAt,
    };

    public TokenSnapshot ToSnapshot(string accountId)
        => new(accountId, Kind, Currency, Balance, Used, Limit, UsedTokens, Period, FetchedAt, null, ExpiresAt);
}

public class DayBaseline
{
    public string Date { get; set; } = "";
    public decimal DayStartBalance { get; set; }
}
