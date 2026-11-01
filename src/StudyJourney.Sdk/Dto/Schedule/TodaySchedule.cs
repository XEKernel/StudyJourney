namespace StudyJourney.Sdk.Dto;

/// <summary>
/// 某一天的实际课表（CORE_ARCHITECTURE §2.6.3）。<see cref="IScheduleProvider.GetDayAsync"/> 的返回体。
/// </summary>
public sealed record TodaySchedule
{
    /// <summary>请求的日期。</summary>
    /// <remarks>消费者：全部消费方（结果自描述：缓存键、日志、远程响应）</remarks>
    public required DateOnly Date { get; init; }

    /// <summary>那天实际按星期几执行（1=周一 … 7=周日，已含调休映射）。</summary>
    /// <remarks>
    /// 消费者：主窗（显示"今天按周五上"） / 远程（网页星期标题） / 自动化（规则 TriggerDays 的匹配基准）
    /// </remarks>
    public required int EffectiveDayOfWeek { get; init; }

    /// <summary>这天是否是调休日（<c>MakeupDays</c> 里存在该日期）。</summary>
    /// <remarks>
    /// 消费者：主窗（顶部提示"今天补周五的课"） / 远程（网页标注）。
    /// 单独一个布尔而不是让消费方算 <c>EffectiveDayOfWeek != 真实星期几</c>：
    /// 那个判断看起来简单，但周日归一化会咬人。
    /// </remarks>
    public required bool IsMakeupDay { get; init; }

    /// <summary>当天的节次，按 <see cref="ScheduledPeriod.Period"/> 升序。</summary>
    /// <remarks>消费者：全部消费方（主循环数据）</remarks>
    public required IReadOnlyList<ScheduledPeriod> Periods { get; init; }
}
