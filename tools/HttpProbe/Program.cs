using System.Diagnostics;
using OnShow.Cards.Token;

// 用与 OnShow 相同的 Http 栈实测三家端点连通性(无效凭据,预期 401/未登录 JSON)
var targets = new (string Name, string Url, Dictionary<string, string> Headers)[]
{
    ("bigmodel", "https://open.bigmodel.cn/api/monitor/usage/quota/limit",
        new Dictionary<string, string> { ["Authorization"] = "probe-test" }),
    ("z.ai    ", "https://api.z.ai/api/monitor/usage/quota/limit",
        new Dictionary<string, string> { ["Authorization"] = "Bearer probe-test" }),
    ("xfyun   ", "https://maas.xfyun.cn/api/v1/gpt-finetune/api-services/usage?page=1&size=10&startDate=2026-10-25&endDate=2026-10-31",
        new Dictionary<string, string> { ["Cookie"] = "probe-test=1" }),
};

foreach (var (name, url, headers) in targets)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var (doc, body) = await Http.GetAsync(url, headers, CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"[{name}] OK {sw.ElapsedMilliseconds,5}ms  {Http.Trunc(body.ReplaceLineEndings(" "), 90)}");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"[{name}] FAIL {sw.ElapsedMilliseconds,5}ms  {ex.GetType().Name}: {Http.Trunc(ex.Message, 80)}");
    }
}
