using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace OnShow.Cards.Token;

/// <summary>
/// 统一 HTTP GET + JSON 解析:10s 连接 / 15s 总超时,指数退避重试 2 次。
/// 连接策略:IPv4 优先——代理/TUN 环境常见 IPv6 黑洞,默认 AAAA 优先会挂死连接
/// (实测 bigmodel/api.z.ai 超时 36s、curl happy-eyeballs 秒通,xfyun 仅 IPv4 故幸免)。
/// </summary>
public static class Http
{
    static readonly HttpClient Client = new(CreateHandler())
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        handler.ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var ordered = addresses
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();

            Exception? last = null;
            foreach (var addr in ordered)
            {
                var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addr, ctx.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    last = ex;
                    socket.Dispose();
                }
            }
            throw last ?? new SocketException();
        };
        return handler;
    }

    static Http()
        => Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.0.0");

    public static async Task<(JsonDocument Doc, string Body)> GetAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (headers is not null)
                    foreach (var (k, v) in headers)
                        req.Headers.TryAddWithoutValidation(k, v);

                using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Trunc(body, 180)}");

                return (JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body), body);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
            {
                last = ex;
                if (attempt >= 3) throw;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
    }

    public static async Task<JsonDocument> GetJsonAsync(
        string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
        => (await GetAsync(url, headers, ct)).Doc;

    public static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>在任意 JSON 结构中按 key 名(不区分大小写)深度查找第一个数值。</summary>
    public static decimal? FindNumber(JsonElement el, params string[] names)
    {
        var queue = new Queue<JsonElement>();
        queue.Enqueue(el);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in cur.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Number &&
                        names.Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)) &&
                        p.Value.TryGetDecimal(out var v))
                        return v;
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        queue.Enqueue(p.Value);
                }
            }
            else if (cur.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cur.EnumerateArray())
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        queue.Enqueue(item);
            }
        }
        return null;
    }
}
