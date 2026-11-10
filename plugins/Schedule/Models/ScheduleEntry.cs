using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule.Models;

/// <summary>
/// 一条课节（v3.3.0 的 <c>schedule.json</c> 原样结构）。
///
/// ⚠ **属性名必须与落盘 JSON 完全一致，且是 PascalCase**：老文件里就是
/// <c>"DayOfWeek"</c> / <c>"StartTimeStr"</c>，不加 <c>[JsonPropertyName]</c> 才对得上
/// （加了反而对不上——默认命名策略本来就是属性名原样输出）。
/// ⚠ 这里只有 6 个持久化字段；<c>StartTime</c>/<c>EndTime</c> 之类派生值不进模型，
/// 否则 System.Text.Json 会把它们当数据写回文件（v3.3.0 就因此长期存着一份
/// <c>"TimeDisplay": "07:00-07:50"</c> 的假数据，见旧 <c>Models/ScheduleEntry.cs</c> 的注释）。
/// 时间一律由 <c>ScheduleResolver.TryParseTime</c> 宽松解析。
/// </summary>
internal sealed class ScheduleEntry
{
    /// <summary>星期几：1=周一 … 7=周日。</summary>
    public int DayOfWeek { get; set; }

    /// <summary>第几节课，从 1 开始。</summary>
    public int Period { get; set; }

    /// <summary>课程名称（"数学"/"早读"/"听力"…）。</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>上课时间，约定 <c>HH:mm</c>（读入时不保证，解析见 <c>ScheduleResolver.TryParseTime</c>）。</summary>
    public string StartTimeStr { get; set; } = "08:00";

    /// <summary>下课时间，约定 <c>HH:mm</c>。</summary>
    public string EndTimeStr { get; set; } = "08:45";

    /// <summary>节次类型。**以整数落盘**（I-4.1：枚举只能追加不能重排）。</summary>
    public PeriodType Type { get; set; } = PeriodType.Normal;

    /// <summary>深拷贝（写回前做快照用，避免调用方改到正在被读的实例）。</summary>
    public ScheduleEntry Clone() => new()
    {
        DayOfWeek = DayOfWeek,
        Period = Period,
        Subject = Subject,
        StartTimeStr = StartTimeStr,
        EndTimeStr = EndTimeStr,
        Type = Type,
    };
}
