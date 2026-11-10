using StudyJourney.Plugin.Schedule;
using StudyJourney.Plugin.Schedule.Models;
using Xunit;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// <see cref="ScheduleResolver"/> 的断言。这两个纯函数是调休 / 时间解析的全部逻辑所在，
/// 也是 v3.3.0 出过两次静默错误的地方（周日 0/7、带秒时间串），所以逐条钉住。
/// </summary>
public sealed class ScheduleResolverTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);      // 周一
    private static readonly DateOnly Tuesday = new(2026, 9, 15);     // 周二
    private static readonly DateOnly Saturday = new(2026, 9, 19);    // 周六
    private static readonly DateOnly Sunday = new(2026, 9, 20);      // 周日

    // ── 调休映射 ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveEffectiveDayOfWeek_普通周一_返回1()
    {
        Assert.Equal(1, ScheduleResolver.ResolveEffectiveDayOfWeek(Monday, []));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_普通周六_返回6()
    {
        Assert.Equal(6, ScheduleResolver.ResolveEffectiveDayOfWeek(Saturday, []));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_真实周日_归一化为7而不是0()
    {
        // .NET 的 DayOfWeek 周日是 0，本项目全链路用 7 —— 最容易错的一处。
        Assert.Equal(7, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, null));
        Assert.Equal(DayOfWeek.Sunday, Sunday.DayOfWeek);
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_普通周二_返回2()
    {
        Assert.Equal(2, ScheduleResolver.ResolveEffectiveDayOfWeek(Tuesday, []));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_调休命中_返回被补的那一天()
    {
        // 9/20 是周日，补周五的课。
        MakeupDay[] makeup = [new() { DateStr = "2026-09-20", DayOfWeek = 5 }];

        Assert.Equal(5, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, makeup));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_调休里的星期越界_忽略并回落真实星期()
    {
        MakeupDay[] zero = [new() { DateStr = "2026-09-20", DayOfWeek = 0 }];
        MakeupDay[] eight = [new() { DateStr = "2026-09-20", DayOfWeek = 8 }];

        Assert.Equal(7, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, zero));
        Assert.Equal(7, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, eight));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_同一天两条调休_取第一条有效的()
    {
        MakeupDay[] makeup =
        [
            new() { DateStr = "2026-09-20", DayOfWeek = 5 },
            new() { DateStr = "2026-09-20", DayOfWeek = 6 },
        ];

        Assert.Equal(5, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, makeup));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_第一条越界_跳过它取后面有效的()
    {
        MakeupDay[] makeup =
        [
            new() { DateStr = "2026-09-20", DayOfWeek = 0 },
            new() { DateStr = "2026-09-20", DayOfWeek = 6 },
        ];

        Assert.Equal(6, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, makeup));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_调休表为null_不抛()
    {
        Assert.Equal(1, ScheduleResolver.ResolveEffectiveDayOfWeek(Monday, null));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_日期必须在yyyy_MM_dd上精确匹配()
    {
        // 斜杠写法是老师手改文件时最常见的手滑，不命中就回落真实星期，而不是抛异常。
        MakeupDay[] makeup = [new() { DateStr = "2026/09/20", DayOfWeek = 5 }];

        Assert.Equal(7, ScheduleResolver.ResolveEffectiveDayOfWeek(Sunday, makeup));
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_只影响被点名的日期_邻近日不受影响()
    {
        MakeupDay[] makeup = [new() { DateStr = "2026-09-20", DayOfWeek = 5 }];

        Assert.Equal(6, ScheduleResolver.ResolveEffectiveDayOfWeek(Saturday, makeup));
        Assert.Equal(1, ScheduleResolver.ResolveEffectiveDayOfWeek(new DateOnly(2026, 9, 21), makeup));
    }

    // ── 调休日判定 ────────────────────────────────────────────────────

    [Fact]
    public void IsMakeupDay_按日期精确匹配()
    {
        MakeupDay[] makeup = [new() { DateStr = "2026-09-20", DayOfWeek = 5 }];

        Assert.True(ScheduleResolver.IsMakeupDay(Sunday, makeup));
        Assert.False(ScheduleResolver.IsMakeupDay(Saturday, makeup));
        Assert.False(ScheduleResolver.IsMakeupDay(Sunday, null));
        Assert.False(ScheduleResolver.IsMakeupDay(Sunday, []));
    }

    // ── 时间解析 ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("07:00", 7, 0, 0)]
    [InlineData("07:50", 7, 50, 0)]
    [InlineData("0:05", 0, 5, 0)]
    [InlineData("7:05", 7, 5, 0)]
    [InlineData("23:59", 23, 59, 0)]
    [InlineData("07:00:00", 7, 0, 0)]      // 从别处粘来的带秒写法：v3.3.0 会静默当成 08:00
    [InlineData("7:00:00", 7, 0, 0)]
    [InlineData(" 08:05 ", 8, 5, 0)]
    public void TryParseTime_合法写法_全部解析到正确时刻(string text, int h, int m, int s)
    {
        Assert.True(ScheduleResolver.TryParseTime(text, out var value));
        Assert.Equal(new TimeSpan(h, m, s), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("25:00")]      // 越界小时，不许被宽松解析吃掉
    [InlineData("08:61")]      // 越界分钟
    [InlineData("-1:00")]
    [InlineData("12")]
    public void TryParseTime_非法写法_返回false且不抛(string? text)
    {
        Assert.False(ScheduleResolver.TryParseTime(text, out var value));
        Assert.Equal(default, value);
    }

    [Fact]
    public void TryParseTime_带秒写法不能被当成整点()
    {
        // 这条是回归用例：v3.3.0 里 "07:00:00" 走宽松分支被解析成 08:00，
        // 导致该节课的时长、"当前课"判定全部错位且不报错。
        Assert.True(ScheduleResolver.TryParseTime("07:00:00", out var value));
        Assert.Equal(7, value.Hours);
        Assert.NotEqual(TimeSpan.FromHours(8), value);
    }

    [Theory]
    [InlineData("7:05", "07:05")]
    [InlineData("07:05", "07:05")]
    [InlineData("07:05:30", "07:05")]
    [InlineData("23:59", "23:59")]
    public void FormatTime_输出HH_mm零填充(string input, string expected)
    {
        Assert.True(ScheduleResolver.TryParseTime(input, out var value));
        Assert.Equal(expected, ScheduleResolver.FormatTime(value));
    }
}
