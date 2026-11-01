namespace StudyJourney.Sdk.Dto;

/// <summary>
/// 节次类型。**整数值与 v3.3.0 的 <c>PeriodType</c> 保持一致**（0..4），
/// 因为它们会随课表一起落进 <c>schedule.json</c>（I-4.1：枚举以整数落盘，只能追加不能重排）。
/// </summary>
public enum PeriodType
{
    /// <summary>普通课。</summary>
    Normal = 0,

    /// <summary>早自习。</summary>
    Morning = 1,

    /// <summary>晚自习。</summary>
    Evening = 2,

    /// <summary>晚读。</summary>
    Reading = 3,

    /// <summary>午休 / 午自习。</summary>
    Noon = 4,
}
