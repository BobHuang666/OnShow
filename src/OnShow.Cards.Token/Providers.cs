using System.Globalization;
using System.Text.Json;
using OnShow.Core;

namespace OnShow.Cards.Token;

/// <summary>Provider 公共小工具。</summary>
internal static class ProviderUtil
{
    public static string BaseUrl(TokenAccount account, string fallback)
    {
        var url = account.BaseUrl?.Trim().TrimEnd('/') ?? "";
        return url.Length == 0 ? fallback : url;
    }

    /// <summary>智谱系接口的业务错误:{code:非0且非200, msg:"..."};成功(0/200)或无 code 返回 null。</summary>
    public static string? ReadServerError(JsonElement root)
    {
        if (!root.TryGetProperty("code", out var codeEl) || !codeEl.TryGetInt32(out var code)) return null;
        if (code == 0 || code == 200) return null;   // 该系接口成功时返回 code:200
        var msg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
            ? msgEl.GetString()
            : root.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String ? m2.GetString() : null;
        return string.IsNullOrWhiteSpace(msg) ? $"接口返回 code={code}" : msg;
    }

    /// <summary>按字段名取数值,Number 与数字字符串都接受。</summary>
    public static decimal? NumOf(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}

/// <summary>DeepSeek:GET /user/balance → 余额型(官方文档接口)。</summary>
public sealed class DeepSeekProvider : ITokenProvider
{
    public string Id => "deepseek";
    public string DisplayName => "DeepSeek";
    public string DefaultBaseUrl => "https://api.deepseek.com";
    public string DefaultConsoleUrl => "https://platform.deepseek.com/usage";
    public IReadOnlyList<CredField> CredentialFields { get; } = new[] { new CredField("apiKey", "API Key", true) };

    public async Task<TokenSnapshot> FetchAsync(TokenAccount a, IReadOnlyDictionary<string, string> cred, CancellationToken ct)
    {
        var key = cred.GetValueOrDefault("apiKey");
        if (string.IsNullOrWhiteSpace(key)) return TokenSnapshot.Fail(a.Id, "未配置 API Key");

        var url = $"{ProviderUtil.BaseUrl(a, DefaultBaseUrl)}/user/balance";
        var doc = await Http.GetJsonAsync(url, new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" }, ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
            return TokenSnapshot.Fail(a.Id, "响应中无余额信息");

        var info = infos[0];
        decimal? total = null;
        if (info.TryGetProperty("total_balance", out var tb) && tb.ValueKind == JsonValueKind.String)
            if (decimal.TryParse(tb.GetString(), out var v)) total = v;
        var currency = info.TryGetProperty("currency", out var c) ? c.GetString() ?? "CNY" : "CNY";

        return new TokenSnapshot(a.Id, SnapshotKind.Balance, currency, total, null, null, null, null, DateTime.Now);
    }
}

/// <summary>
/// 智谱 GLM:GET /api/monitor/usage/quota/limit → 配额型。
/// ⚠ 非公开文档接口(社区验证),响应结构未完全确定 → 防御性字段挖掘;
/// 首次接入真实 Key 后按实际返回在日志中核对并收窄解析。
/// </summary>
public sealed class ZhipuProvider : ITokenProvider
{
    public string Id => "zhipu";
    public string DisplayName => "智谱 GLM";
    public string DefaultBaseUrl => "https://open.bigmodel.cn";
    public string DefaultConsoleUrl => "https://open.bigmodel.cn/";
    public IReadOnlyList<CredField> CredentialFields { get; } = new[] { new CredField("apiKey", "API Key", true) };

    public async Task<TokenSnapshot> FetchAsync(TokenAccount a, IReadOnlyDictionary<string, string> cred, CancellationToken ct)
    {
        var key = cred.GetValueOrDefault("apiKey");
        if (string.IsNullOrWhiteSpace(key)) return TokenSnapshot.Fail(a.Id, "未配置 API Key");

        var url = $"{ProviderUtil.BaseUrl(a, DefaultBaseUrl)}/api/monitor/usage/quota/limit";
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = key,               // 智谱接受裸 Key;Bearer 由服务端兼容
            ["Accept-Language"] = "zh-CN",
        };
        var (doc, body) = await Http.GetAsync(url, headers, ct);
        var root = doc.RootElement;

        var serverError = ProviderUtil.ReadServerError(root);
        if (serverError is not null) return TokenSnapshot.Fail(a.Id, serverError);

        var used = Http.FindNumber(root, "usage", "used", "total_usage", "used_amount");
        var limit = Http.FindNumber(root, "limit", "total", "total_amount", "quota_total", "hard_limit");
        var remaining = Http.FindNumber(root, "remaining", "remain", "available", "balance");

        if (limit is null && remaining is not null && used is not null)
            limit = used + remaining;

        if (limit is not null)
            return new TokenSnapshot(a.Id, SnapshotKind.Quota, "", null, used ?? (limit - remaining), limit, null, null, DateTime.Now);

        if (remaining is not null)
            return new TokenSnapshot(a.Id, SnapshotKind.Balance, "", remaining, null, null, null, null, DateTime.Now);

        Log.Info($"[zhipu] 响应格式未识别:{Http.Trunc(body, 400)}");
        return TokenSnapshot.Fail(a.Id, "响应格式未识别,详见日志");
    }
}

/// <summary>
/// 讯飞星火 MaaS:GET /api/v1/gpt-finetune/api-services/free-quota?channel={modelId} → 配额型。
/// 控制台后台接口(用户实测抓包),靠 **Cookie 会话**鉴权;凭据填浏览器里复制的完整 Cookie 串。
/// channel 从 usage 接口首行 modelId 动态获取(失败回退 spark-x2.6)。
/// 展示:已用/总额度(如 14.44M / 1.00B)+ 到期倒计时。
/// </summary>
public sealed class XunfeiProvider : ITokenProvider
{
    public string Id => "xunfei";
    public string DisplayName => "讯飞星火 MaaS";
    public string DefaultBaseUrl => "https://maas.xfyun.cn";
    public string DefaultConsoleUrl => "https://maas.xfyun.cn/usageStatistics?ch=xcgzh_x26";
    public IReadOnlyList<CredField> CredentialFields { get; } = new[] { new CredField("cookie", "Cookie(从浏览器复制完整串)", true) };

    public async Task<TokenSnapshot> FetchAsync(TokenAccount a, IReadOnlyDictionary<string, string> cred, CancellationToken ct)
    {
        var (cookie, headers, baseUrl) = Prepare(a, cred);
        if (cookie is null) return TokenSnapshot.Fail(a.Id, "未配置 Cookie(浏览器 F12 复制)");

        // channel 从 usage 接口首行 modelId 动态获取;失败回退默认
        var channel = "spark-x2.6";
        try
        {
            var end = DateTime.Now;
            var start = end.AddDays(-6);
            var usageUrl = $"{baseUrl}/api/v1/gpt-finetune/api-services/usage?page=1&size=1" +
                           $"&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}";
            var (d1, _) = await Http.GetAsync(usageUrl, headers, ct);
            if (d1.RootElement.TryGetProperty("data", out var dd) && dd.TryGetProperty("rows", out var rr)
                && rr.GetArrayLength() > 0 && rr[0].TryGetProperty("modelId", out var mid))
                channel = mid.GetString() ?? channel;
        }
        catch { /* channel 回退默认 */ }

        var url = $"{baseUrl}/api/v1/gpt-finetune/api-services/free-quota?channel={Uri.EscapeDataString(channel)}";
        var (doc, body) = await Http.GetAsync(url, headers, ct);
        return ParseFreeQuota(a.Id, doc.RootElement, body);
    }

    internal (string? Cookie, Dictionary<string, string> Headers, string BaseUrl) Prepare(
        TokenAccount a, IReadOnlyDictionary<string, string> cred)
    {
        var cookieRaw = cred.GetValueOrDefault("cookie")?.Trim() ?? "";
        // 容错:用户可能连 "cookie:" 前缀一起复制
        if (cookieRaw.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
            cookieRaw = cookieRaw[7..].Trim();
        var baseUrl = ProviderUtil.BaseUrl(a, DefaultBaseUrl);
        var headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/plain, */*",
            ["Referer"] = $"{baseUrl}/usageStatistics",
        };
        if (cookieRaw.Length > 0)
        {
            headers["Cookie"] = cookieRaw;
            // 控制台部分校验同时读同名 Header:把 cookie 里的会话值补发为请求头
            if (TryGetCookieValue(cookieRaw, "atp-auth-token", out var atp)) headers["atp-auth-token"] = atp;
            if (TryGetCookieValue(cookieRaw, "tenantToken", out var tenant)) headers["tenantToken"] = tenant;
        }
        return (cookieRaw.Length == 0 ? null : cookieRaw, headers, baseUrl);
    }

    /// <summary>解析 free-quota 响应:data.claim.freeQuota{usedTokens,totalTokens,expiresAt}。</summary>
    public static TokenSnapshot ParseFreeQuota(string accountId, JsonElement root, string? rawBody = null)
    {
        if (ProviderUtil.ReadServerError(root) is { } err) return TokenSnapshot.Fail(accountId, err);

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("claim", out var claim)
            || !claim.TryGetProperty("freeQuota", out var fq))
        {
            Log.Info($"[xunfei] 无 freeQuota(可能未领取额度或会话过期):{Http.Trunc(rawBody ?? root.GetRawText(), 300)}");
            return TokenSnapshot.Fail(accountId, "未领取免费额度或会话过期");
        }

        var used = ProviderUtil.NumOf(fq, "usedTokens");
        var total = ProviderUtil.NumOf(fq, "totalTokens");
        DateTime? expiresAt = null;
        if (fq.TryGetProperty("expiresAt", out var expEl) && expEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(expEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
            expiresAt = exp;

        return new TokenSnapshot(accountId, SnapshotKind.Quota, "", null, used, total, null, null,
            DateTime.Now, null, expiresAt);
    }

    static bool TryGetCookieValue(string cookie, string name, out string value)
    {
        foreach (var part in cookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = part[(eq + 1)..].Trim();
                return true;
            }
        }
        value = "";
        return false;
    }
}

/// <summary>
/// Z.ai(GLM Coding Plan,国际版,与 bigmodel.cn 开放平台是两套账号体系):
/// GET /api/monitor/usage/quota/limit → 配额型(百分比)。
/// 规格来自官方插件 glm-plan-usage 源码(zai-org/zai-coding-plugins):
/// 鉴权为 **裸 Coding Plan Token**(即工具里配置的 ANTHROPIC_AUTH_TOKEN,不带 Bearer 前缀);
/// 响应 data.limits[]:TOKENS_LIMIT(5 小时窗口用量百分比)/ TIME_LIMIT(月度)。
/// </summary>
public sealed class ZaiProvider : ITokenProvider
{
    public string Id => "zai";
    public string DisplayName => "Z.ai (GLM Coding Plan)";
    public string DefaultBaseUrl => "https://api.z.ai";
    public string DefaultConsoleUrl => "https://z.ai/";
    public IReadOnlyList<CredField> CredentialFields { get; } = new[] { new CredField("apiKey", "Coding Plan Token(ANTHROPIC_AUTH_TOKEN)", true) };

    public async Task<TokenSnapshot> FetchAsync(TokenAccount a, IReadOnlyDictionary<string, string> cred, CancellationToken ct)
    {
        var token = cred.GetValueOrDefault("apiKey");
        if (string.IsNullOrWhiteSpace(token)) return TokenSnapshot.Fail(a.Id, "未配置 Coding Plan Token");

        var url = $"{ProviderUtil.BaseUrl(a, DefaultBaseUrl)}/api/monitor/usage/quota/limit";
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = token.Trim(),          // 官方插件为裸 token,无 Bearer 前缀
            ["Accept-Language"] = "en-US,en",
            ["Content-Type"] = "application/json",
        };
        var (doc, body) = await Http.GetAsync(url, headers, ct);
        return ParseQuota(a.Id, doc.RootElement, body);
    }

    /// <summary>解析 quota/limit 响应:优先 TOKENS_LIMIT(5 小时窗口),退回 TIME_LIMIT(月度)。</summary>
    public static TokenSnapshot ParseQuota(string accountId, JsonElement root, string? rawBody = null)
    {
        var serverError = ProviderUtil.ReadServerError(root);
        if (serverError is not null) return TokenSnapshot.Fail(accountId, serverError);

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("limits", out var limits))
        {
            Log.Info($"[zai] 响应缺 data.limits:{Http.Trunc(rawBody ?? root.GetRawText(), 300)}");
            return TokenSnapshot.Fail(accountId, "响应缺 limits,详见日志");
        }

        decimal? tokenPct = null, timePct = null;
        foreach (var item in limits.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "TOKENS_LIMIT" && tokenPct is null)
                tokenPct = ProviderUtil.NumOf(item, "percentage");
            if (type == "TIME_LIMIT" && timePct is null)
                timePct = ProviderUtil.NumOf(item, "percentage");
        }

        var pct = tokenPct ?? timePct;
        if (pct is null)
        {
            Log.Info($"[zai] limits 中无百分比:{Http.Trunc(rawBody ?? root.GetRawText(), 300)}");
            return TokenSnapshot.Fail(accountId, "响应无用量百分比,详见日志");
        }

        return new TokenSnapshot(accountId, SnapshotKind.Quota, "", null,
            decimal.Round(pct.Value, 1), 100, null, tokenPct is not null ? "5小时窗口" : "月度", DateTime.Now);
    }
}

/// <summary>手动兜底:无网络,读账户上录入的 已用/额度。</summary>
public sealed class ManualProvider : ITokenProvider
{
    public string Id => "manual";
    public string DisplayName => "手动录入";
    public string DefaultBaseUrl => "";
    public string DefaultConsoleUrl => "";   // 手动录入无网页,点击行回退设置页
    public IReadOnlyList<CredField> CredentialFields { get; } = Array.Empty<CredField>();

    public Task<TokenSnapshot> FetchAsync(TokenAccount a, IReadOnlyDictionary<string, string> cred, CancellationToken ct)
        => Task.FromResult(new TokenSnapshot(
            a.Id, SnapshotKind.Quota, "", null, a.ManualUsed, a.ManualLimit, null, null, DateTime.Now));
}
