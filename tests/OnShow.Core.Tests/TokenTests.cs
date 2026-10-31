using System.Text.Json;
using OnShow.Cards.Token;
using Xunit;

namespace OnShow.Core.Tests;

public class TokenTests
{
    [Fact]
    public void FindNumber_Searches_Nested_Objects()
    {
        using var doc = JsonDocument.Parse("""{"code":200,"data":{"items":[{"used_amount":12.5}]}}""");
        Assert.Equal(12.5m, OnShow.Cards.Token.Http.FindNumber(doc.RootElement, "used_amount"));
    }

    [Fact]
    public void FindNumber_Missing_Returns_Null()
    {
        using var doc = JsonDocument.Parse("""{"hello":"world"}""");
        Assert.Null(OnShow.Cards.Token.Http.FindNumber(doc.RootElement, "usage"));
    }

    [Fact]
    public void TokenRowVm_Quota_Ratio_And_Levels()
    {
        var account = new TokenAccount { Name = "测试" };
        var vm = new TokenRowVm { Account = account, Provider = new ManualProvider() };

        vm.Apply(new TokenSnapshot(account.Id, SnapshotKind.Quota, "", null, 82, 100, null, null, DateTime.Now), null);
        Assert.Equal(0.82, vm.BarRatio, 3);
        Assert.Equal("warn", vm.Level);
        Assert.Contains("82", vm.StatusText);

        vm.Apply(new TokenSnapshot(account.Id, SnapshotKind.Quota, "", null, 96, 100, null, null, DateTime.Now), null);
        Assert.Equal("danger", vm.Level);
    }

    [Fact]
    public void TokenRowVm_Balance_Shows_Currency_And_DaySpend()
    {
        var account = new TokenAccount { Name = "DeepSeek 主账户" };
        var vm = new TokenRowVm { Account = account, Provider = new ManualProvider() };

        vm.Apply(new TokenSnapshot(account.Id, SnapshotKind.Balance, "CNY", 55.4m, null, null, null, null, DateTime.Now), 12.6m);
        Assert.Contains("¥55.40", vm.StatusText);
        Assert.Contains("今日 -12.60", vm.DetailText);
        Assert.Equal(-1, vm.BarRatio);
    }

    [Fact]
    public void TokenRowVm_Error_Keeps_Friendly_State()
    {
        var account = new TokenAccount { Name = "x" };
        var vm = new TokenRowVm { Account = account, Provider = new ManualProvider() };
        vm.Apply(TokenSnapshot.Fail(account.Id, "请求超时"), null);
        Assert.Equal("error", vm.Level);
        Assert.Equal("请求超时", vm.ErrorText);
    }

    [Fact]
    public void SavedSnapshot_Roundtrip()
    {
        var snap = new TokenSnapshot("a1", SnapshotKind.UsageTokens, "", null, null, null, 12345, "累计", DateTime.Now);
        var saved = SavedSnapshot.From(snap);
        var back = saved.ToSnapshot("a1");
        Assert.Equal(snap.Kind, back.Kind);
        Assert.Equal(snap.UsedTokens, back.UsedTokens);
        Assert.Equal(snap.Period, back.Period);
    }

    // ===== Z.ai quota/limit 解析(形状取自官方 glm-plan-usage 插件) =====

    [Fact]
    public void ZaiParse_TokensLimit_Wins_With_Percentage()
    {
        using var doc = JsonDocument.Parse("""{"code":200,"data":{"limits":[{"type":"TOKENS_LIMIT","percentage":82.5},{"type":"TIME_LIMIT","percentage":10,"currentValue":3,"usage":30}]}}""");
        var snap = ZaiProvider.ParseQuota("a1", doc.RootElement);
        Assert.Equal(SnapshotKind.Quota, snap.Kind);
        Assert.Equal(82.5m, snap.Used);
        Assert.Equal(100m, snap.Limit);
        Assert.Equal("5小时窗口", snap.Period);
        Assert.Null(snap.Error);
    }

    [Fact]
    public void ZaiParse_String_Percentage_And_TimeLimit_Fallback()
    {
        using var doc = JsonDocument.Parse("""{"data":{"limits":[{"type":"TIME_LIMIT","percentage":"37"}]}}""");
        var snap = ZaiProvider.ParseQuota("a1", doc.RootElement);
        Assert.Equal(37m, snap.Used);
        Assert.Equal("月度", snap.Period);
    }

    [Fact]
    public void ZaiParse_ServerError_Is_Surfaced()
    {
        using var doc = JsonDocument.Parse("""{"code":500,"msg":"当前用户不存在coding plan","success":false}""");
        var snap = ZaiProvider.ParseQuota("a1", doc.RootElement);
        Assert.Equal("当前用户不存在coding plan", snap.Error);
    }

    [Fact]
    public void ZaiParse_Missing_Limits_Fails_Clearly()
    {
        using var doc = JsonDocument.Parse("""{"code":200}""");
        var snap = ZaiProvider.ParseQuota("a1", doc.RootElement);
        Assert.StartsWith("响应缺 limits", snap.Error);
    }

    // ===== 讯飞 MaaS free-quota 解析(形状取自用户实测抓包) =====

    [Fact]
    public void XunfeiParse_FreeQuota()
    {
        using var doc = JsonDocument.Parse("""{"code":0,"data":{"canClaim":false,"channel":"spark-x2.6","claim":{"freeQuota":{"effectiveAt":"2026-10-29 18:26:03","expiresAt":"2026-11-29 18:26:03","quotaStatus":"ACTIVE","remainingTokens":985558672,"totalTokens":1000000000,"usageAvailable":true,"usedTokens":14441328,"validitySeconds":2678400}}},"succeed":true}""");
        var snap = XunfeiProvider.ParseFreeQuota("a1", doc.RootElement);
        Assert.Equal(SnapshotKind.Quota, snap.Kind);
        Assert.Equal(14441328m, snap.Used);
        Assert.Equal(1000000000m, snap.Limit);
        Assert.Equal(new DateTime(2026, 11, 29, 18, 26, 3), snap.ExpiresAt);
        Assert.Null(snap.Error);
    }

    [Fact]
    public void XunfeiParse_NotLoggedIn_Is_Surfaced()
    {
        using var doc = JsonDocument.Parse("""{"code":4001,"failed":true,"message":"用户未登录","succeed":false}""");
        var snap = XunfeiProvider.ParseFreeQuota("a1", doc.RootElement);
        Assert.Equal("用户未登录", snap.Error);
    }

    [Fact]
    public void TokenRowVm_Quota_Format_M_B_With_Expiry()
    {
        var account = new TokenAccount { Name = "Spark" };
        var vm = new TokenRowVm { Account = account, Provider = new ManualProvider() };
        vm.Apply(new TokenSnapshot(account.Id, SnapshotKind.Quota, "", null, 14441328m, 1000000000m, null, null,
            DateTime.Now, null, DateTime.Now.AddDays(31)), null);
        Assert.Contains("14.44M / 1.00B", vm.StatusText);
        Assert.Contains("% 已使用", vm.DetailText);
        Assert.Contains("剩 30 天", vm.DetailText);
        Assert.Equal(0.014441328, vm.BarRatio, 6);
    }
}
