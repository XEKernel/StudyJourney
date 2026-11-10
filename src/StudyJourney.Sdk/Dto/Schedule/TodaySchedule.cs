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

    /// <summary>
    /// 这天是否是调休日（<c>MakeupDays</c> 里**按日期**存在该条目）。
    /// </summary>
    /// <remarks>
    /// 消费者：主窗（顶部提示"今天补周五的课"） / 远程（网页标注）。
    /// 单独一个布尔而不是让消费方算 <c>EffectiveDayOfWeek != 真实星期几</c>：
    /// 那个判断看起来简单，但周日归一化会咬人。
    ///
    /// ⚠ **这是"原始事实"，不等于"调休真的生效了"。** 判定口径分两层：
    /// <list type="number">
    /// <item>本属性：调休表里**有没有这一天的记录**（只比日期，不看记录里的星期是否合法）。</item>
    /// <item><see cref="IScheduleProvider.ResolveEffectiveDayOfWeek"/>：记录存在**且** <c>DayOfWeek</c>
    ///       在 1..7 才采纳，否则忽略该条、回落真实星期。</item>
    /// </list>
    /// 两者在"老师把调休条目的星期写坏了（0 或 8）"时会分叉：
    /// <c>IsMakeupDay == true</c> 但 <c>EffectiveDayOfWeek</c> 仍等于真实星期。
    /// **要在界面上说"今天按周五上"，必须两个条件同时成立**
    /// （<c>IsMakeupDay &amp;&amp; EffectiveDayOfWeek != 真实星期</c>），
    /// 否则会对着一张没变的课表告诉老师"今天补周五的课"。
    /// </remarks>
    public required bool IsMakeupDay { get; init; }

    /// <summary>当天的节次，按 <see cref="ScheduledPeriod.Period"/> 升序。</summary>
    /// <remarks>消费者：全部消费方（主循环数据）</remarks>
    public required IReadOnlyList<ScheduledPeriod> Periods { get; init; }
}
