namespace StudyJourney.Sdk.Dto;

/// <summary>
/// 一节课（CORE_ARCHITECTURE §2.6.2）。**按消费方裁出来的投影，不是 <c>ScheduleEntry</c> 的搬运。**
///
/// 为什么只有 6 个字段：v3.3.0 的 <c>ScheduleEntry</c> 本来也只有 6 个持久化字段；
/// 其余成员（<c>StartTime</c>/<c>EndTime</c> 计算属性、<c>GetEndDateTimeActual</c>）是派生运算，
/// 交给 <c>Sdk.DateTimeUtil</c> 的纯函数，不放进契约——否则每个消费方都要复制一遍"宽松解析"。
/// </summary>
public sealed record ScheduledPeriod
{
    /// <summary>星期几：1=周一 … 7=周日（**已含调休映射**，即"那天实际按周几上"）。</summary>
    /// <remarks>消费者：提醒（生成去重键） / 自动化（TriggerDays 匹配） / 主窗（周视图列） / 远程（weekday 字段）</remarks>
    public required int DayOfWeek { get; init; }

    /// <summary>第几节课，从 1 开始。</summary>
    /// <remarks>消费者：提醒（去重键） / 自动化（eventKey） / 远程（网页行定位） / 课表编辑器（格子坐标）</remarks>
    public required int Period { get; init; }

    /// <summary>课程名称（如"数学"/"听力"/"晚自习"）。</summary>
    /// <remarks>
    /// 消费者：主窗（显示） / 提醒（通知正文 + 按科目静音 + 标题） /
    /// 自动化（TriggerSubject 匹配 + 科目目录解析 + "自习/班会"特殊判定） / 远程（网页显示） / 课表编辑器（编辑）
    /// </remarks>
    public required string Subject { get; init; }

    /// <summary>上课时间，格式 <c>"HH:mm"</c>。</summary>
    /// <remarks>
    /// 消费者：远程（**原样回传网页**，由网页自己格式化） / 主窗（显示"08:00-08:45"） /
    /// 提醒（解析成 TimeSpan 后与 now 比较） / 自动化（eventKey 含时间去冒号）
    /// </remarks>
    public required string StartTimeStr { get; init; }

    /// <summary>下课时间，格式 <c>"HH:mm"</c>。</summary>
    /// <remarks>消费者：与 <see cref="StartTimeStr"/> 完全相同的四处</remarks>
    public required string EndTimeStr { get; init; }

    /// <summary>节次类型（普通/早自习/晚自习/晚读/午休）。</summary>
    /// <remarks>
    /// 消费者：提醒（自习类连堂静音 + 特殊节次提醒开关） / 自动化（听力节次触发） / 主窗（分组显示）
    /// </remarks>
    public required PeriodType Type { get; init; }
}
