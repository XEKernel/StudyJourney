using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Test;

/// <summary>
/// 假的课表能力（M0 夹具）：**今天数学、英语两节课**，其余天返回同一份假数据。
///
/// ⚠ 只返回 Sdk 的 DTO（<see cref="TodaySchedule"/>/<see cref="ScheduledPeriod"/>），
/// **不得**把 <c>Control</c> 之类 UI 对象塞进返回值（CORE_ARCHITECTURE §2.6.2 的 DTO 纪律）。
/// </summary>
public sealed class TestScheduleProvider : IScheduleProvider
{
    /// <summary>假课表：第 1 节数学、第 2 节英语（时间固定 <c>HH:mm</c> 零填充）。</summary>
    public static readonly IReadOnlyList<ScheduledPeriod> SamplePeriods =
    [
        new ScheduledPeriod
        {
            DayOfWeek = 1,
            Period = 1,
            Subject = "数学",
            StartTimeStr = "08:00",
            EndTimeStr = "08:45",
            Type = PeriodType.Normal,
        },
        new ScheduledPeriod
        {
            DayOfWeek = 1,
            Period = 2,
            Subject = "英语",
            StartTimeStr = "08:55",
            EndTimeStr = "09:40",
            Type = PeriodType.Normal,
        },
    ];

    /// <inheritdoc />
    public Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var schedule = new TodaySchedule
        {
            Date = date,
            EffectiveDayOfWeek = ResolveEffectiveDayOfWeek(date),
            IsMakeupDay = false,
            Periods = SamplePeriods,
        };

        return Task.FromResult(Result<TodaySchedule>.Ok(schedule));
    }

    /// <inheritdoc />
    public Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default)
        => GetDayAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    /// <summary>M0 不做调休：周日归一化为 7，其余等于 <see cref="DayOfWeek"/> 的整数形式。</summary>
    public int ResolveEffectiveDayOfWeek(DateOnly date)
        => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
}
