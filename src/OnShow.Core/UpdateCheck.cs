using System.Text.Json;

namespace OnShow.Core;

/// <summary>GitHub Release 最新版信息(/releases/latest 响应的提取结果)。</summary>
public sealed class GitHubReleaseInfo
{
    public string TagName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public string? ZipAssetUrl { get; init; }
    public string? ZipAssetName { get; init; }
    public long ZipSize { get; init; }
    public string? ChecksumsUrl { get; init; }

    /// <summary>去掉 v 前缀的语义版本;无法解析时为 null。</summary>
    public Version? Version => Version.TryParse(TagName.TrimStart('v', 'V'), out var v) ? v : null;
}

/// <summary>解析 /releases/latest JSON,提取 OnShow-*.zip 与 checksums.txt 两个资产。解析失败返回 null。</summary>
public static class GitHubReleaseParser
{
    public static GitHubReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? tagName = null, name = null, body = null;
            if (root.TryGetProperty("tag_name", out var tagEl) && tagEl.ValueKind == JsonValueKind.String)
                tagName = tagEl.GetString();
            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                name = nameEl.GetString();
            if (root.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String)
                body = bodyEl.GetString();
            if (tagName is null) return null;

            string? zipUrl = null, zipName = null, csUrl = null;
            long zipSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var assetName = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (assetName is null || assetUrl is null) continue;

                    if (zipUrl is null
                        && assetName.StartsWith("OnShow-", StringComparison.OrdinalIgnoreCase)
                        && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = assetUrl;
                        zipName = assetName;
                        zipSize = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                            ? s.GetInt64() : 0;
                    }
                    else if (csUrl is null && assetName.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        csUrl = assetUrl;
                    }
                }
            }

            return new GitHubReleaseInfo
            {
                TagName = tagName,
                Name = name ?? tagName,
                Body = body ?? "",
                ZipAssetUrl = zipUrl,
                ZipAssetName = zipName,
                ZipSize = zipSize,
                ChecksumsUrl = csUrl,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>GitHub 更新检查(HTTP)。调用方传入仓库的 releases/latest API 地址,本类不感知仓库。</summary>
public static class UpdateCheck
{
    static readonly Lazy<HttpClient> _http = new(() =>
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("OnShow");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        c.Timeout = TimeSpan.FromSeconds(30);
        return c;
    });

    /// <summary>共享 HttpClient(检查与下载共用,带 UA;GitHub API 必需 UA)。</summary>
    public static HttpClient Http => _http.Value;

    /// <summary>查询最新 Release。仓库尚无任何 Release(404)时返回 null;网络错误抛异常。</summary>
    public static async Task<GitHubReleaseInfo?> CheckLatestAsync(string apiUrl, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(apiUrl, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return GitHubReleaseParser.Parse(json);
    }
}

/// <summary>版本比较辅助。</summary>
public static class UpdateHelper
{
    /// <summary>latestTag(如 v1.2.0)是否比 current 更新;无法解析视为不更新。</summary>
    public static bool IsNewer(string latestTag, Version current)
    {
        try
        {
            return Version.Parse(latestTag.TrimStart('v', 'V')) > current;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>checksums.txt(行格式:"&lt;sha256&gt;  &lt;filename&gt;"")解析。</summary>
public static class ChecksumFile
{
    public static string? FindFor(string content, string fileName)
    {
        foreach (var raw in content.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }
        return null;
    }
}
