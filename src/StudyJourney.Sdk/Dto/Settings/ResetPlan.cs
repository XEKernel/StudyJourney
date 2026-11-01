namespace StudyJourney.Sdk.Dto;

/// <summary>重置项落在三段清单的哪一段（D-1：确认框本来就是三段，不是两段）。</summary>
public enum ResetItemGroup
{
    /// <summary>重置后**保持不变**的东西（例如"账号不会被删除"）。</summary>
    /// <remarks>消费者：ResetPlanBuilder（"会保留"段）</remarks>
    Kept = 0,

    /// <summary>会被重置为默认值、但**可以重新设置**。</summary>
    /// <remarks>消费者：ResetPlanBuilder（"会重置"段）</remarks>
    Reset = 1,

    /// <summary>重置后**不可恢复**（例如自定义课表被示例课表覆盖）。</summary>
    /// <remarks>消费者：ResetPlanBuilder（"不可恢复"段，需勾选已知悉）</remarks>
    Irreversible = 2,
}

/// <summary>一条重置影响项。</summary>
public sealed record ResetPlanItem
{
    /// <summary>属于哪一段。</summary>
    /// <remarks>消费者：ResetPlanBuilder（分组渲染）</remarks>
    public required ResetItemGroup Group { get; init; }

    /// <summary>人话描述（"课表里所有科目的自定义时间"）。</summary>
    /// <remarks>消费者：ResetPlanBuilder → 确认框正文</remarks>
    public required string Description { get; init; }

    /// <summary>补充说明（"共 3 天独立作息将被删除"），可空。</summary>
    /// <remarks>消费者：确认框（展开详情时显示）</remarks>
    public string? Detail { get; init; }
}

/// <summary>
/// 一个插件对"重置会丢什么"的自述（D-1）。**只描述、不动手**——核心收集它拼成三段清单，
/// 用户勾选"我已知悉"、核心备份之后，才逐个调用 <see cref="ISettingsContributor.ResetToDefaults"/>。
/// </summary>
public sealed record ResetPlan
{
    /// <summary>一句话概括（"课表与作息将恢复为示例数据"）。</summary>
    /// <remarks>消费者：ResetPlanBuilder（清单标题）</remarks>
    public required string Summary { get; init; }

    /// <summary>逐条影响项。</summary>
    /// <remarks>消费者：ResetPlanBuilder（三段清单）</remarks>
    public required IReadOnlyList<ResetPlanItem> Items { get; init; }
}
