namespace OnShow.Core;

/// <summary>
/// GitHub Issue 反馈内容组装(纯函数,便于单元测试)。
/// 客户端绝不携带任何凭据;上传范围 = 用户文字 + 环境信息文本。
/// </summary>
public static class FeedbackComposer
{
    /// <summary>Issue 标题:取描述首行,压缩空白,超长截断。</summary>
    public static string ComposeTitle(string text)
    {
        var first = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "用户反馈";
        var compact = string.Join(' ', first.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 48 ? compact : compact[..48] + "…";
    }

    /// <summary>Issue 正文:描述 + 可选环境信息块 + 来源尾注。envBlock 为 null 时不附环境信息。</summary>
    public static string ComposeBody(string text, string? envBlock)
    {
        var body = text.TrimEnd();
        if (!string.IsNullOrWhiteSpace(envBlock))
            body += "\n\n---\n\n**环境信息**\n" + envBlock.TrimEnd() + "\n";
        return body + "\n\n> 通过 OnShow 反馈窗口提交";
    }

    /// <summary>
    /// 拼接新建 Issue 预填 URL。浏览器对 URL 长度有限制(约 8k),
    /// 超限时截断正文并注明「完整内容已复制到剪贴板」。
    /// </summary>
    public static string BuildIssueUrl(string issuesNewUrl, string title, string body)
    {
        const int maxUrlLength = 6500;
        var b = body;
        var url = Compose(issuesNewUrl, title, b);
        while (url.Length > maxUrlLength && b.Length > 200)
        {
            b = b[..(b.Length * 3 / 4)].TrimEnd() + "\n\n(描述过长已截断,完整内容已复制到剪贴板)";
            url = Compose(issuesNewUrl, title, b);
        }
        return url;

        static string Compose(string baseUrl, string t, string bodyText)
            => $"{baseUrl}?title={Uri.EscapeDataString(t)}&body={Uri.EscapeDataString(bodyText)}";
    }
}
