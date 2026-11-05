using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.A015Probe;

/// <summary>
/// 探针用的课表能力：返回固定假数据（不读盘、不依赖任何核心实现）。
/// 它存在的唯一意义是**被注册进注册表、再从核心侧取回来比 Type 身份**（A0-15-a）。
/// </summary>
public sealed class ProbeScheduleProvider : IScheduleProvider
{
    /// <summary>固定返回"今天两节课"。</summary>
    public Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<TodaySchedule>.Ok(Build(date)));

    /// <inheritdoc />
    public Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default)
        => GetDayAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    /// <summary>纯函数：周日归一化为 7。</summary>
    public int ResolveEffectiveDayOfWeek(DateOnly date)
    {
        var d = (int)date.DayOfWeek;   // 0 = 周日
        return d == 0 ? 7 : d;
    }

    private static TodaySchedule Build(DateOnly date) => new()
    {
        Date = date,
        EffectiveDayOfWeek = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek,
        IsMakeupDay = false,
        Periods =
        [
            new ScheduledPeriod
            {
                DayOfWeek = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek,
                Period = 1,
                Subject = "数学",
                StartTimeStr = "08:00",
                EndTimeStr = "08:45",
                Type = PeriodType.Normal,
            },
            new ScheduledPeriod
            {
                DayOfWeek = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek,
                Period = 2,
                Subject = "英语",
                StartTimeStr = "08:55",
                EndTimeStr = "09:40",
                Type = PeriodType.Normal,
            },
        ],
    };
}
