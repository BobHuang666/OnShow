namespace OnShow.Cards.Token;

/// <summary>Token 监控账户(token.json 持久化;凭据另存 DPAPI,不落明文)。</summary>
public class TokenAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ProviderId { get; set; } = "deepseek";

    /// <summary>留空使用 Provider 默认地址。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>控制台网页(Widget 点行跳转);留空用 Provider 默认。</summary>
    public string ConsoleUrl { get; set; } = "";

    /// <summary>Manual 兜底类型使用。</summary>
    public decimal? ManualUsed { get; set; }
    public decimal? ManualLimit { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>手动排序号(设置页拖拽调整);0 表示未初始化,按名称排序迁移。</summary>
    public int Order { get; set; }
}

/// <summary>快照类型:余额型(剩余额度) / 配额型(已用/总量) / 用量型(累计消耗)。</summary>
public enum SnapshotKind { Balance, Quota, UsageTokens }

/// <summary>一次拉取的结果或错误。错误时保留 Kind 默认值,Error 非空。</summary>
public sealed record TokenSnapshot(
    string AccountId,
    SnapshotKind Kind,
    string Currency,
    decimal? Balance,
    decimal? Used,
    decimal? Limit,
    decimal? UsedTokens,
    string? Period,
    DateTime FetchedAt,
    string? Error = null,
    DateTime? ExpiresAt = null)
{
    public static TokenSnapshot Fail(string accountId, string error)
        => new(accountId, SnapshotKind.Balance, "", null, null, null, null, null, DateTime.Now, error);
}
