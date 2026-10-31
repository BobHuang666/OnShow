using OnShow.Core;
using Xunit;

namespace OnShow.Core.Tests;

public class TimeUtilTests
{
    readonly DateTime _now = new(2026, 6, 1, 12, 0, 0);

    [Theory]
    [InlineData(960, "far")]     // 40 天 → far
    [InlineData(72, "near")]     // 3 天 → near
    [InlineData(23, "urgent")]   // ≤24h → urgent
    public void Levels_Follow_Display_Rules(double hoursAhead, string expected)
    {
        var (text, level, seconds) = TimeUtil.Describe(_now.AddHours(hoursAhead), _now);
        Assert.Equal(expected, level);
        Assert.False(string.IsNullOrEmpty(text));
        Assert.Equal(expected == "urgent", seconds);
    }

    [Fact]
    public void Urgent_Shows_Hhmmss()
    {
        var (text, _, _) = TimeUtil.Describe(_now.AddHours(23).AddMinutes(4).AddSeconds(5), _now);
        Assert.Equal("23:04:05", text);
    }

    [Fact]
    public void Past_Shows_Over_Level()
    {
        var (text, level, seconds) = TimeUtil.Describe(_now.AddDays(-2), _now);
        Assert.Equal("over", level);
        Assert.StartsWith("+2", text);
        Assert.False(seconds);
    }

    [Fact]
    public void Near_Includes_Days_And_Hours()
    {
        var (text, level, _) = TimeUtil.Describe(_now.AddDays(3).AddHours(5), _now);
        Assert.Equal("near", level);
        Assert.Equal("3 天 5 时", text);
    }
}
