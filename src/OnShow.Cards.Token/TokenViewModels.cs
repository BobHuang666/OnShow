using OnShow.Core;

namespace OnShow.Cards.Token;

/// <summary>Token 卡单行视图模型:快照到达后原地更新,UI 走 INPC 增量刷新。</summary>
public class TokenRowVm : ObservableObject
{
    string _statusText = "—";
    string _detailText = "";
    string _errorText = "";
    string _level = "normal";
    double _barRatio = -1;

    public required TokenAccount Account { get; init; }
    public required ITokenProvider Provider { get; init; }

    public string Name => string.IsNullOrWhiteSpace(Account.Name) ? Provider.DisplayName : Account.Name;
    public string ProviderName => Provider.DisplayName;
    /// <summary>未自定义名称时 Name 与 ProviderName 相同,副标签不再重复显示。</summary>
    public bool ProviderNameVisible => !string.IsNullOrWhiteSpace(Account.Name) && Name != Provider.DisplayName;
    /// <summary>点击行跳转的控制台网页:账户自定义优先,否则 Provider 默认;空 = 无网页。</summary>
    public string ConsoleUrl => string.IsNullOrWhiteSpace(Account.ConsoleUrl) ? Provider.DefaultConsoleUrl : Account.ConsoleUrl.Trim();

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    /// <summary>normal / warn / danger / error</summary>
    public string Level { get => _level; private set => Set(ref _level, value); }
    /// <summary>0–1;负数表示无进度条。</summary>
    public double BarRatio { get => _barRatio; private set => Set(ref _barRatio, value); }

    public void Apply(TokenSnapshot snap, decimal? daySpend)
    {
        if (snap.Error is not null)
        {
            ErrorText = snap.Error;
            DetailText = $"上次成功 {snap.FetchedAt:HH:mm}";
            StatusText = "—";
            BarRatio = -1;
            Level = "error";
            return;
        }

        ErrorText = "";
        switch (snap.Kind)
        {
            case SnapshotKind.Balance:
                StatusText = $"{s_CurrencyPrefix.GetValueOrDefault(snap.Currency, snap.Currency + " ")}{snap.Balance:N2}";
                DetailText = daySpend is { } d && d != 0 ? $"今日 {FormatSigned(d)}" : "";
                BarRatio = -1;
                Level = "normal";
                break;

            case SnapshotKind.Quota:
            {
                double? ratio = null;
                if (snap.Used is not null && snap.Limit is { } lim && lim > 0)
                    ratio = (double)Math.Clamp(snap.Used.Value / lim, 0, 1);
                BarRatio = ratio ?? -1;
                StatusText = snap.Used is not null && snap.Limit is not null
                    ? $"{TrimNum(snap.Used)} / {TrimNum(snap.Limit)}"
                    : snap.Used is not null ? TrimNum(snap.Used) : "—";
                DetailText = ratio is { } r
                    ? $"{r * 100:N1}% 已使用{ExpirySuffix(snap.ExpiresAt)}"
                    : "";
                Level = ratio is { } rr ? (rr >= 0.95 ? "danger" : rr >= 0.8 ? "warn" : "normal") : "normal";
                break;
            }

            case SnapshotKind.UsageTokens:
                StatusText = $"{TrimNum(snap.UsedTokens)} tokens";
                DetailText = snap.Period ?? "";
                BarRatio = -1;
                Level = "normal";
                break;
        }
    }

    static readonly Dictionary<string, string> s_CurrencyPrefix = new()
    {
        ["CNY"] = "¥",
        ["USD"] = "$",
    };

    static string FormatSigned(decimal d) => d > 0 ? $"-{d:N2}" : $"+{-d:N2}";

    /// <summary>到期倒计时后缀:· 剩 x 天 / x 时;已过期提示。</summary>
    static string ExpirySuffix(DateTime? expiresAt)
    {
        if (expiresAt is null) return "";
        var remain = expiresAt.Value - DateTime.Now;
        if (remain <= TimeSpan.Zero) return " · 已过期";
        return remain.TotalDays >= 1
            ? $" · 剩 {(int)remain.TotalDays} 天"
            : $" · 剩 {(int)remain.TotalHours} 时";
    }

    /// <summary>大数缩写:M / B(英文风格,如 14.44M / 1.00B),万级用万。</summary>
    static string TrimNum(decimal? v)
    {
        if (v is null) return "—";
        var a = Math.Abs(v.Value);
        if (a >= 1_000_000_000m) return $"{v.Value / 1_000_000_000m:N2}B";
        if (a >= 1_000_000m) return $"{v.Value / 1_000_000m:N2}M";
        if (a >= 10_000m) return $"{v.Value / 10_000m:N1}万";
        return $"{v.Value:N0}";
    }
}
