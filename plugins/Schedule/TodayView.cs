using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// 把「今天的课表 + 某一时刻」投影成课表栏要显示的一切：顶部日期星期、调休提示、
/// 每行的状态、下一节文案、折叠节数。**纯逻辑，不碰任何控件**，因此可以直接断言。
///
/// 单独成类而不是塞进 <see cref="ScheduleBar"/>：
/// 这里全是"什么时候算上完 / 什么时候算正在上 / 下一节是哪节"的业务判定，
/// 和"控件怎么摆"是两件事；混在一起会让每秒刷新的那条路径既难测又难读。
/// </summary>
internal sealed class TodayView
{
    /// <summary>上课前多少分钟开始提示"即将开始"（与 v3.3.0 的预备铃一致）。</summary>
    public const int PrepareMinutes = 2;

    private readonly ScheduleRowState[] _states;

    private TodayView(
        string header,
        string makeupHint,
        IReadOnlyList<ScheduledPeriod> periods,
        ScheduleRowState[] states,
        int shownCount,
        string nextText)
    {
        Header = header;
        MakeupHint = makeupHint;
        Periods = periods;
        _states = states;
        ShownCount = shownCount;
        NextText = nextText;
    }

    /// <summary>顶部第一行：「11月11日 周三」。</summary>
    public string Header { get; }

    /// <summary>调休提示；空串表示不显示。</summary>
    public string MakeupHint { get; }

    /// <summary>当天的节次（按节次升序，由 provider 保证）。</summary>
    public IReadOnlyList<ScheduledPeriod> Periods { get; }

    /// <summary>列表里真正显示的行数（超过行池容量的部分折叠）。</summary>
    public int ShownCount { get; }

    /// <summary>被折叠掉的节数；0 表示全部显示。</summary>
    public int HiddenCount => Periods.Count - ShownCount;

    /// <summary>今天没课。</summary>
    public bool IsEmpty => Periods.Count == 0;

    /// <summary>底部文案：下一节 / 即将开始 / 今天的课已结束；空串表示不显示。</summary>
    public string NextText { get; }

    /// <summary>第 <paramref name="index"/> 行应当处于什么状态。</summary>
    public ScheduleRowState StateAt(int index) => _states[index];

    /// <summary>起止时间的显示写法（如 <c>09:15–10:00</c>，用 en dash 而不是连字符）。</summary>
    public static string FormatRange(ScheduledPeriod period)
        => $"{period.StartTimeStr}–{period.EndTimeStr}";

    /// <summary>按给定时刻算出整个视图。</summary>
    /// <param name="schedule">当天课表（已含调休映射）。</param>
    /// <param name="now">当前时刻（当日偏移）。</param>
    /// <param name="maxRows">行池容量，超出部分折叠成"还有 N 节"。</param>
    public static TodayView Build(TodaySchedule schedule, TimeSpan now, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var periods = schedule.Periods;
        var states = new ScheduleRowState[periods.Count];
        var currentIndex = FindCurrentIndex(periods, now);

        ScheduledPeriod? next = null;
        var nextStart = TimeSpan.MaxValue;

        for (var i = 0; i < periods.Count; i++)
        {
            var known = TryWindow(periods[i], out var start, out var end);

            states[i] = i == currentIndex
                ? ScheduleRowState.Current
                : known && end <= now
                    ? ScheduleRowState.Finished
                    : ScheduleRowState.Upcoming;

            if (known && start > now && start < nextStart)
            {
                next = periods[i];
                nextStart = start;
            }
        }

        return new TodayView(
            FormatHeader(schedule.Date),
            FormatMakeupHint(schedule),
            periods,
            states,
            Math.Min(periods.Count, maxRows),
            FormatNext(next, nextStart, now, periods.Count));
    }

    /// <summary>当前正在上的那一节的下标；重叠时取高节次（与 v3.3.0 一致：下节的预备铃覆盖上节末尾）。</summary>
    private static int FindCurrentIndex(IReadOnlyList<ScheduledPeriod> periods, TimeSpan now)
    {
        var index = -1;

        for (var i = 0; i < periods.Count; i++)
        {
            if (!TryWindow(periods[i], out var start, out var end))
                continue;                                  // 时间坏掉的节次不参与"当前课"判定

            if (now < start || now >= end)
                continue;

            if (index < 0 || periods[i].Period >= periods[index].Period)
                index = i;
        }

        return index;
    }

    private static string FormatNext(ScheduledPeriod? next, TimeSpan nextStart, TimeSpan now, int periodCount)
    {
        if (next is null)
        {
            // 有课但今天已全部上完：说清楚，别让界面看起来像"没数据"。
            return periodCount > 0 ? "今天的课已结束" : string.Empty;
        }

        var minutes = (int)Math.Ceiling((nextStart - now).TotalMinutes);
        var prefix = minutes is > 0 and <= PrepareMinutes ? "即将开始" : "下一节";

        return $"{prefix} {next.Subject} {next.StartTimeStr}";
    }

    private static string FormatHeader(DateOnly date)
        => $"{date.Month}月{date.Day}日 {WeekName(NormalizeDayOfWeek(date.DayOfWeek))}";

    private static string FormatMakeupHint(TodaySchedule schedule)
    {
        var realDow = NormalizeDayOfWeek(schedule.Date.DayOfWeek);
        var effective = schedule.EffectiveDayOfWeek;

        // 只有"确实是调休日"且"映射真的变了"才提示：调休条目的星期写坏时
        // IsMakeupDay 仍为 true，但实际按真实星期上，此时提示会骗人。
        var show = schedule.IsMakeupDay && effective != realDow && effective is >= 1 and <= 7;

        return show ? $"今天按{WeekName(effective)}上" : string.Empty;
    }

    /// <summary>解析一节的起止时间；解析不了返回 false（该节仍显示，只是不参与状态判定）。</summary>
    private static bool TryWindow(ScheduledPeriod period, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        if (!ScheduleResolver.TryParseTime(period.StartTimeStr, out start) ||
            !ScheduleResolver.TryParseTime(period.EndTimeStr, out end))
        {
            return false;
        }

        if (end <= start)
            end = start + TimeSpan.FromMinutes(45);   // 手改坏的时长：按 45 分钟节处理，别让它"永远在上"

        return true;
    }

    private static int NormalizeDayOfWeek(DayOfWeek dayOfWeek)
        => dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;

    private static string WeekName(int dayOfWeek) => dayOfWeek switch
    {
        1 => "周一",
        2 => "周二",
        3 => "周三",
        4 => "周四",
        5 => "周五",
        6 => "周六",
        7 => "周日",
        _ => $"周{dayOfWeek}",
    };
}
