namespace OnShow.Cards.Token;

/// <summary>凭据字段定义(设置 UI 依据它动态生成输入框)。</summary>
public sealed record CredField(string Key, string Label, bool Secret);

/// <summary>
/// Token 数据源适配器契约。实现要求:
/// 不抛异常(失败返回 TokenSnapshot.Fail);HTTP 细节走 Http 助手;超时与重试由助手统一处理。
/// </summary>
public interface ITokenProvider
{
    string Id { get; }
    string DisplayName { get; }
    string DefaultBaseUrl { get; }

    /// <summary>该服务商查看额度/费用的控制台网页(Widget 点行跳转);空 = 无网页。</summary>
    string DefaultConsoleUrl { get; }

    IReadOnlyList<CredField> CredentialFields { get; }

    Task<TokenSnapshot> FetchAsync(TokenAccount account, IReadOnlyDictionary<string, string> credentials, CancellationToken ct);
}
