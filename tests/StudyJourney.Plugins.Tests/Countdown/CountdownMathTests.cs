using StudyJourney.Plugin.Countdown;
using Xunit;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>
/// 倒计时纯函数的卡点测试。重点不是"跑通"，而是把**语义差异**钉住：
/// 自然日差（可负）与真实剩余时长（不为负）不是一回事，目标当天/已过/起始日前后都要有证据。
/// </summary>
public sealed class CountdownMathTests
{
    private static DateTime D(int y, int m, int d, int hh = 0, int mm = 0, int ss = 0)
        => new(y, m, d, hh, mm, ss);

    // ── 解析 ────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "M1B-01 三种紧凑格式都能解析（含首尾空格）")]
    [InlineData("2027-06-07 09:00:00", 2027, 6, 7, 9, 0, 0)]
    [InlineData("2027-06-07 09:00", 2027, 6, 7, 9, 0, 0)]
    [InlineData("2027-06-07", 2027, 6, 7, 0, 0, 0)]
    [InlineData("  2027-06-07 09:00:00  ", 2027, 6, 7, 9, 0, 0)]
    public void TryParseDateTime_AcceptsThreeFormats(
        string text, int y, int m, int d, int hh, int mi, int ss)
    {
        Assert.True(CountdownMath.TryParseDateTime(text, out var value));
        Assert.Equal(D(y, m, d, hh, mi, ss), value);
    }

    [Theory(DisplayName = "M1B-02 空串/非法格式一律 false，且不抛")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2027/06/07")]                 // 宽松 TryParse 会接受，这里刻意不接受（跨区域不确定）
    [InlineData("2027-06-07 09:00:00.500")]    // 带毫秒不在白名单内
    [InlineData("2027-13-01")]                 // 月份越界
    [InlineData("2027-02-29")]                 // 2027 不是闰年
    [InlineData("abc")]
    public void TryParseDateTime_RejectsInvalid(string text)
    {
        Assert.False(CountdownMath.TryParseDateTime(text, out var value));
        Assert.Equal(default, value);
    }

    [Fact(DisplayName = "M1B-03 null 与「仅空格」都返回 false")]
    public void TryParseDateTime_Null_ReturnsFalse()
    {
        Assert.False(CountdownMath.TryParseDateTime(null, out var value));
        Assert.Equal(default, value);
        Assert.False(CountdownMath.TryParseDateTime(" ", out _));
    }

    [Fact(DisplayName = "M1B-04 闰日 2028-02-29 可解析，2028-02-28 距它 1 天")]
    public void LeapDay_IsValid_AndCountsOneDay()
    {
        Assert.True(CountdownMath.TryParseDateTime("2028-02-29", out var leapDay));
        Assert.Equal(D(2028, 2, 29), leapDay);
        Assert.Equal(1, CountdownMath.DaysRemaining(D(2028, 2, 28, 23, 59, 59), leapDay));
    }

    // ── 自然日差 ────────────────────────────────────────────────────────────

    [Theory(DisplayName = "M1B-05 剩余天数按自然日算：当天 0、跨零点 ±1、已过为负")]
    [InlineData(2027, 6, 7, 0, 0, 0)]        // 目标当天凌晨
    [InlineData(2027, 6, 7, 23, 0, 0)]       // 目标当天深夜（已过时刻仍是 0 天）
    [InlineData(2027, 6, 6, 0, 0, 1)]
    [InlineData(2027, 6, 8, 0, 0, -1)]
    [InlineData(2027, 5, 7, 12, 0, 31)]      // 5 月有 31 天
    [InlineData(2027, 2, 28, 12, 0, 99)]     // 2027 非闰年：2/28 → 6/7
    public void DaysRemaining_CountsCalendarDays(int ny, int nm, int nd, int nh, int nmi, int expect)
    {
        var target = D(2027, 6, 7, 9, 0, 0);
        Assert.Equal(expect, CountdownMath.DaysRemaining(D(ny, nm, nd, nh, nmi), target));
    }

    // ── 真实时长 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1B-06 目标已过 → 剩余时长为 Zero，不返回负数")]
    public void TimeRemaining_PastTarget_IsZero()
    {
        var target = D(2027, 6, 7, 9, 0, 0);
        Assert.Equal(TimeSpan.Zero, CountdownMath.TimeRemaining(D(2027, 6, 12, 9, 0, 0), target));
        Assert.Equal(TimeSpan.Zero, CountdownMath.TimeRemaining(target, target));
    }

    [Fact(DisplayName = "M1B-07 目标未到 → 剩余时长就是两者之差")]
    public void TimeRemaining_FutureTarget_IsDifference()
    {
        var target = D(2027, 6, 7, 9, 0, 0);
        var now = D(2027, 6, 6, 7, 0, 0);
        Assert.Equal(new TimeSpan(1, 2, 0, 0), CountdownMath.TimeRemaining(now, target));
    }

    [Fact(DisplayName = "M1B-08 目标当天只剩几小时：天数为 0，时长仍为正")]
    public void SameDay_TargetLaterToday_DaysZeroButTimePositive()
    {
        var target = D(2027, 6, 9, 9, 0, 0);
        var now = D(2027, 6, 9, 8, 30, 0);

        Assert.Equal(0, CountdownMath.DaysRemaining(now, target));
        Assert.Equal(TimeSpan.FromMinutes(30), CountdownMath.TimeRemaining(now, target));
    }

    // ── 进度 ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1B-09 起始日之前 / 起始日当天 → 进度 0")]
    public void Progress_BeforeOrAtStart_IsZero()
    {
        var start = D(2024, 8, 24);
        var target = D(2027, 6, 7, 9, 0, 0);

        Assert.Equal(0.0, CountdownMath.Progress(D(2024, 8, 23, 23, 59, 59), start, target));
        Assert.Equal(0.0, CountdownMath.Progress(start, start, target));
    }

    [Fact(DisplayName = "M1B-10 目标当天 / 目标已过 → 进度 1，且永不越界")]
    public void Progress_AtOrPastTarget_IsOne()
    {
        var start = D(2024, 8, 24);
        var target = D(2027, 6, 7, 9, 0, 0);

        Assert.Equal(1.0, CountdownMath.Progress(target, start, target));
        Assert.Equal(1.0, CountdownMath.Progress(D(2030, 1, 1), start, target));
    }

    [Fact(DisplayName = "M1B-11 start >= target（写反/相等）→ 进度 1，不做除法")]
    public void Progress_StartNotBeforeTarget_IsOne()
    {
        var target = D(2027, 6, 7, 9, 0, 0);

        Assert.Equal(1.0, CountdownMath.Progress(D(2025, 1, 1), target, target));
        Assert.Equal(1.0, CountdownMath.Progress(D(2025, 1, 1), D(2028, 1, 1), target));
    }

    [Fact(DisplayName = "M1B-12 区间中点 → 0.5")]
    public void Progress_Midpoint_IsHalf()
    {
        var start = D(2024, 8, 24);
        var target = D(2027, 6, 7, 9, 0, 0);
        var middle = start.AddTicks((target - start).Ticks / 2);

        Assert.Equal(0.5, CountdownMath.Progress(middle, start, target), 6);
    }
}
