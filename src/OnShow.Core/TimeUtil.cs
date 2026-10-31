namespace OnShow.Core;

/// <summary>DDL 剩余时间的展示规则与紧急度分级。</summary>
public static class TimeUtil
{
    /// <param name="Text">展示文本</param>
    /// <param name="Level">far(>7天) / near(≤7天) / urgent(≤24h,需秒级刷新) / over(已过期)</param>
    /// <param name="Seconds">是否需要秒级定时器</param>
    public static (string Text, string Level, bool Seconds) Describe(DateTime due, DateTime now)
    {
        var remain = due - now;
        if (remain <= TimeSpan.Zero)
        {
            var over = -remain;
            return (over.TotalDays >= 1 ? $"+{(int)over.TotalDays} 天" : $"+{(int)over.TotalHours} 时", "over", false);
        }

        if (remain.TotalHours > 24)
        {
            var days = (int)remain.TotalDays;
            return (remain.TotalDays > 7
                ? $"{days} 天"
                : $"{days} 天 {remain.Hours} 时", remain.TotalDays > 7 ? "far" : "near", false);
        }

        return ($"{(int)remain.TotalHours:00}:{remain.Minutes:00}:{remain.Seconds:00}", "urgent", true);
    }
}
