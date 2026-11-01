namespace StudyJourney.Sdk.Dto;

/// <summary>课表变更的种类。</summary>
public enum ScheduleChangeKind
{
    /// <summary>整份课表被重新加载（远程 PUT / 手工改文件后 Reload）。</summary>
    /// <remarks>消费者：提醒/自动化（全量重算缓存）</remarks>
    Reloaded = 0,

    /// <summary>某一节课被编辑（课名/时间/类型）。</summary>
    /// <remarks>消费者：提醒/自动化（只重算当日）</remarks>
    EntryEdited = 1,

    /// <summary>按天的作息模板被改（"应用模板"）。</summary>
    /// <remarks>消费者：提醒（全部节次时间可能变）</remarks>
    DayTemplatesChanged = 2,

    /// <summary>调休表被改（补课日增删）。</summary>
    /// <remarks>消费者：自动化（TriggerDays 的映射基准变了）</remarks>
    MakeupDaysChanged = 3,
}

/// <summary>
/// 课表变更事件（CORE_ARCHITECTURE §2.6.4）。课表插件在**版本自增之后**发布它。
///
/// 事件类型定义在 Sdk 而不是课表插件里：否则发布方与订阅方各自 ALC 里各有一份类型，
/// <c>Subscribe&lt;T&gt;</c> 永远匹配不上（§4.6）。
/// </summary>
public sealed record ScheduleChangedEvent
{
    /// <summary>变更种类，决定订阅方是"全量重算"还是"只算当日"。</summary>
    /// <remarks>消费者：提醒（缓存失效范围） / 自动化（规则重算范围） / 主窗（重绘） / 远程（响应新鲜度）</remarks>
    public required ScheduleChangeKind Kind { get; init; }

    /// <summary>受影响的日期；<c>null</c> 表示全量（<see cref="ScheduleChangeKind.Reloaded"/>）。</summary>
    /// <remarks>消费者：提醒 / 自动化 / 主窗</remarks>
    public DateOnly? Date { get; init; }

    /// <summary>单调递增的版本号。订阅方缓存时必须一并存它，用来判断"我这份过期没有"。</summary>
    /// <remarks>消费者：提醒 / 自动化（各自的事件驱动缓存）</remarks>
    public required long Version { get; init; }
}
