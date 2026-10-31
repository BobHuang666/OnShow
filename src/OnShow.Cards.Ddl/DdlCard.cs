using OnShow.Core;
using OnShow.Core.Cards;

namespace OnShow.Cards.Ddl;

/// <summary>
/// DDL 倒计时卡。
/// 定时策略:常驻 1s 定时器只做文本增量与提醒检查(开销极低);
/// 有 ≤24h 条目时文本每秒变化(INPC 自动刷新),否则文本按分钟粒度变化,自然静默。
/// </summary>
public sealed class DdlCard : ICard
{
    public CardMetadata Metadata { get; } = new("ddl", "DDL 倒计时", "⏰");

    public event Action? ViewModelChanged;

    static string DataFile => Path.Combine(AppPaths.Root, "ddl.json");

    readonly object _gate = new();
    ICardHost _host = null!;
    List<DdlItem> _items = new();
    DdlState _state = new();
    List<DdlItemVm> _vms = new();
    System.Threading.Timer? _timer;

    public void OnLoaded(ICardHost host)
    {
        _host = host;
        _items = Json.Load<List<DdlItem>>(DataFile);
        _state = host.State.Get<DdlState>("ddl", new DdlState());
        Rebuild();
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
        Log.Info($"DDL 卡加载:{_items.Count} 条");
    }

    void Tick()
    {
        try
        {
            var now = DateTime.Now;
            lock (_gate)
            {
                foreach (var vm in _vms) vm.Update(now);
                CheckReminders(now);
            }
        }
        catch (Exception ex)
        {
            Log.Error("DDL Tick 异常", ex);
        }
    }

    /// <summary>DDL 卡数据本地自持,无外部刷新源。</summary>
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    // ---------- 查询 ----------

    /// <summary>展示列表:未完成按截止时间升序在前,已完成后置。</summary>
    public IReadOnlyList<DdlItemVm> Items { get { lock (_gate) return _vms.ToList(); } }

    public DdlItemVm? Next { get { lock (_gate) return _vms.FirstOrDefault(v => !v.Done); } }

    public int ActiveCount { get { lock (_gate) return _items.Count(i => !i.Done); } }

    public DdlItem? GetItem(string id) { lock (_gate) return _items.FirstOrDefault(i => i.Id == id); }

    public object GetViewModel() => Items;

    // ---------- 数据操作(设置窗口 / 快速添加调用) ----------

    public void Upsert(DdlItem item)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(i => i.Id == item.Id);
            if (idx >= 0) _items[idx] = item; else _items.Add(item);
            _state.Fired.Remove(item.Id);   // 截止时间可能被修改,重置提醒
            Persist();
            Rebuild();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.Id == id);
            _state.Fired.Remove(id);
            Persist();
            Rebuild();
        }
    }

    public void SetDone(string id, bool done)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            item.Done = done;
            Persist();
            Rebuild();
        }
    }

    /// <summary>外部(设置页)修改后整体重载。</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _items = Json.Load<List<DdlItem>>(DataFile);
            _state = _host.State.Get<DdlState>("ddl", new DdlState());
            Rebuild();
        }
    }

    // ---------- 内部 ----------

    void Rebuild()
    {
        var now = DateTime.Now;
        _vms = _items
            .OrderBy(i => i.Done)
            .ThenBy(i => i.Due)
            .Select(i => new DdlItemVm { Item = i })
            .ToList();
        foreach (var vm in _vms) vm.Update(now);
        ViewModelChanged?.Invoke();
    }

    void Persist() => Json.Save(DataFile, _items);

    void CheckReminders(DateTime now)
    {
        if (!_host.Config.DdlRemindersEnabled) return;
        var dirty = false;
        foreach (var item in _items)
        {
            if (item.Done) continue;
            var remain = item.Due - now;
            if (remain <= TimeSpan.Zero) continue;

            var firedList = _state.Fired.TryGetValue(item.Id, out var l) ? l : new List<string>();
            foreach (var th in item.RemindBefore)
            {
                var span = ParseThreshold(th);
                if (span is null || firedList.Contains(th) || remain > span.Value) continue;
                firedList.Add(th);
                _state.Fired[item.Id] = firedList;
                dirty = true;
                var (text, _, _) = TimeUtil.Describe(item.Due, now);
                _host.Notify("DDL 临近", $"「{item.Title}」剩余 {text}");
                Log.Info($"DDL 提醒:{item.Title} 阈值 {th}");
            }
        }
        if (dirty)
        {
            _host.State.Set("ddl", _state);
            _host.State.Flush();
        }
    }

    public static TimeSpan? ParseThreshold(string t) => t switch
    {
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "3h" => TimeSpan.FromHours(3),
        "24h" => TimeSpan.FromHours(24),
        "3d" => TimeSpan.FromDays(3),
        "7d" => TimeSpan.FromDays(7),
        _ => null,
    };

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

/// <summary>DDL 卡持久化状态(state.json 的 "ddl" 节)。</summary>
public class DdlState
{
    /// <summary>itemId → 已触发过的提醒阈值,保证每阈值只提醒一次。</summary>
    public Dictionary<string, List<string>> Fired { get; set; } = new();
}
