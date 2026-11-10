using Avalonia.Media;
using StudyJourney.Plugin.Schedule;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// 课表栏（次区）的界面状态断言：只读控件文本/颜色，不做渲染（渲染需要平台，属 App 层）。
/// 覆盖四件事：行数与当天节次一致、当前课被强调、已上完的被弱化、没课走空态。
/// </summary>
public sealed class ScheduleBarTests
{
    private static readonly DateOnly Day = new(2026, 11, 11);      // 周三

    [Fact(DisplayName = "M1C-40 列表行数等于当天节次数，节次与时间如实显示")]
    public void Refresh_RowsMatchPeriods()
    {
        var (bar, _) = NewBar(NewSchedule(
            (1, "早读", "07:00", "07:30"),
            (2, "语文", "08:00", "08:45"),
            (3, "英语", "09:15", "10:00")));

        bar.Refresh(At(6, 0));

        Assert.Equal(3, bar.VisibleRowCount);
        Assert.Equal("1", bar.RowAt(0).PeriodShown);
        Assert.Equal("早读", bar.RowAt(0).SubjectShown);
        Assert.Equal("07:00–07:30", bar.RowAt(0).TimeShown);
        Assert.Equal("英语", bar.RowAt(2).SubjectShown);
        Assert.False(bar.IsEmptyStateShown);
    }

    [Fact(DisplayName = "M1C-41 正在上的那一节：强调色 + 加粗 + 左侧竖条")]
    public void Refresh_CurrentPeriodIsAccented()
    {
        var (bar, theme) = NewBar(NewSchedule(
            (1, "语文", "08:00", "08:45"),
            (2, "英语", "09:15", "10:00"),
            (3, "数学", "10:20", "11:05")));

        bar.Refresh(At(9, 30));      // 第 2 节正在进行

        var row = bar.RowAt(1);
        Assert.Equal(theme.Accent, row.SubjectColor);
        Assert.Equal(FontWeight.Bold, row.SubjectWeight);
        Assert.True(row.AccentBarShown);

        // 相邻两行不能跟着一起亮。
        Assert.NotEqual(theme.Accent, bar.RowAt(0).SubjectColor);
        Assert.False(bar.RowAt(0).AccentBarShown);
        Assert.False(bar.RowAt(2).AccentBarShown);
    }

    [Fact(DisplayName = "M1C-42 已经上完的那一节：弱化色，且不带竖条")]
    public void Refresh_FinishedPeriodIsDimmed()
    {
        var (bar, theme) = NewBar(NewSchedule(
            (1, "语文", "08:00", "08:45"),
            (2, "英语", "09:15", "10:00"),
            (3, "数学", "10:20", "11:05")));

        bar.Refresh(At(9, 30));

        var finished = bar.RowAt(0);
        Assert.Equal(theme.TextDim, finished.SubjectColor);
        Assert.False(finished.AccentBarShown);
        Assert.Equal(FontWeight.Normal, finished.SubjectWeight);

        // 还没到的那一节保持主文字色。
        Assert.Equal(theme.TextPrimary, bar.RowAt(2).SubjectColor);
    }

    [Fact(DisplayName = "M1C-43 今天没课：不抛异常、显示空态、不留空列表")]
    public void Refresh_NoPeriods_ShowsEmptyState()
    {
        var (bar, _) = NewBar(NewSchedule());

        var exception = Record.Exception(() => bar.Refresh(At(10, 0)));

        Assert.Null(exception);
        Assert.True(bar.IsEmptyStateShown);
        Assert.Equal(0, bar.VisibleRowCount);
        Assert.Equal(string.Empty, bar.NextTextShown);      // 空态不重复说"已结束"
    }

    [Fact(DisplayName = "M1C-44 课多时只显示行池容量内的节次，并提示还有几节")]
    public void Refresh_TooManyPeriods_Truncates()
    {
        var periods = Enumerable.Range(1, 15)
            .Select(i => (i, $"课{i}", "08:00", "08:45"))
            .ToArray();
        var (bar, _) = NewBar(NewSchedule(periods));

        bar.Refresh(At(7, 0));

        Assert.Equal(12, bar.VisibleRowCount);
        Assert.Equal("还有 3 节", bar.TruncationTextShown);
    }

    [Fact(DisplayName = "M1C-45 顶部显示日期与星期")]
    public void Refresh_HeaderShowsDateAndWeekday()
    {
        var (bar, _) = NewBar(NewSchedule((1, "语文", "08:00", "08:45")));

        bar.Refresh(At(7, 0));

        Assert.Equal("11月11日 周三", bar.HeaderTextShown);
    }

    [Fact(DisplayName = "M1C-46 重复刷新复用同一批行控件（不重建控件树）")]
    public void Refresh_ReusesRowControls()
    {
        var (bar, _) = NewBar(NewSchedule(
            (1, "语文", "08:00", "08:45"),
            (2, "英语", "09:15", "10:00")));

        bar.Refresh(At(7, 0));
        var first = bar.RowAt(0);

        bar.Refresh(At(9, 30));      // 状态变了，但控件实例必须还是同一个

        Assert.Same(first, bar.RowAt(0));
        Assert.Equal("语文", first.SubjectShown);       // 文本仍是这一节，只是颜色变了
        Assert.Equal(2, bar.VisibleRowCount);
    }

    private static DateTime At(int hour, int minute) => Day.ToDateTime(new TimeOnly(hour, minute));

    private static (ScheduleBar Bar, ThemeTokens Theme) NewBar(TodaySchedule schedule)
    {
        var theme = new FakeShellService().CurrentTheme;
        return (new ScheduleBar(new FixedProvider(schedule), theme), theme);
    }

    private static TodaySchedule NewSchedule(params (int Period, string Subject, string Start, string End)[] periods)
    {
        var dow = 3;    // 周三

        return new TodaySchedule
        {
            Date = Day,
            EffectiveDayOfWeek = dow,
            IsMakeupDay = false,
            Periods = periods
                .Select(p => new ScheduledPeriod
                {
                    DayOfWeek = dow,
                    Period = p.Period,
                    Subject = p.Subject,
                    StartTimeStr = p.Start,
                    EndTimeStr = p.End,
                    Type = PeriodType.Normal,
                })
                .ToList(),
        };
    }

    /// <summary>固定返回同一份课表（课表栏只关心"今天"，不关心日期参数）。</summary>
    private sealed class FixedProvider(TodaySchedule schedule) : IScheduleProvider
    {
        public Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<TodaySchedule>.Ok(schedule));

        public Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<TodaySchedule>.Ok(schedule));

        public int ResolveEffectiveDayOfWeek(DateOnly date) => schedule.EffectiveDayOfWeek;
    }
}
