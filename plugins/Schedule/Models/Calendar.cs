using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule.Models;

/// <summary>
/// 调休（补课日）：某个**具体日期**按指定的星期几执行。
///
/// 场景：周日补周五的课 —— 那天要走周五的课表，周五中午的听力自动化也要照常触发。
/// 判定只认 <see cref="DateStr"/> 的 <c>yyyy-MM-dd</c> 精确匹配，**不按星期几推断**。
/// </summary>
internal sealed class MakeupDay
{
    /// <summary>调休日期，格式 <c>yyyy-MM-dd</c>。</summary>
    public string DateStr { get; set; } = string.Empty;

    /// <summary>按星期几执行：1=周一 … 7=周日（越界值视为无效条目）。</summary>
    public int DayOfWeek { get; set; }

    /// <summary>深拷贝。</summary>
    public MakeupDay Clone() => new() { DateStr = DateStr, DayOfWeek = DayOfWeek };
}

/// <summary>
/// 作息时段模板（课表网格的行定义）。
///
/// ⚠ 属性名是 <c>StartTime</c>/<c>EndTime</c>（**不是** <c>StartTimeStr</c>），
/// 与 <see cref="ScheduleEntry"/> 不同 —— 老文件里就是这样，不能"统一"掉。
/// ⚠ 不加任何派生只读属性（旧实现因此把 <c>TimeDisplay</c> 写进了文件）。
/// </summary>
internal sealed class TimeTemplate
{
    /// <summary>第几节课，从 1 开始。</summary>
    public int Period { get; set; }

    /// <summary>起始时间，格式 <c>HH:mm</c>。</summary>
    public string StartTime { get; set; } = "08:00";

    /// <summary>结束时间，格式 <c>HH:mm</c>。</summary>
    public string EndTime { get; set; } = "08:45";

    /// <summary>节次类型。</summary>
    public PeriodType Type { get; set; } = PeriodType.Normal;

    /// <summary>深拷贝。</summary>
    public TimeTemplate Clone() => new()
    {
        Period = Period,
        StartTime = StartTime,
        EndTime = EndTime,
        Type = Type,
    };
}

/// <summary>
/// 考试条目。<b>课表插件不消费它</b>（M4 归考试插件），但它在同一个 <c>schedule.json</c> 里，
/// 所以本插件**读写时必须原样保留**：读进来只是为了能整份写回去，不允许丢字段、不允许改值。
/// </summary>
internal sealed class ExamEntry
{
    /// <summary>考试名称（"2026 高考第二次模拟"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>考试日期，格式 <c>yyyy-MM-dd</c>。</summary>
    public string DateStr { get; set; } = string.Empty;

    /// <summary>科目集合。</summary>
    public List<ExamSubject> Subjects { get; set; } = [];

    /// <summary>深拷贝。</summary>
    public ExamEntry Clone() => new()
    {
        Name = Name,
        DateStr = DateStr,
        Subjects = [.. Subjects.Select(s => s.Clone())],
    };
}

/// <summary>考试科目（考试条目里的一科）。同样原样保留，不参与课表逻辑。</summary>
internal sealed class ExamSubject
{
    /// <summary>科目名（"语文"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>开考时间，格式 <c>HH:mm</c>。</summary>
    public string StartTimeStr { get; set; } = "09:00";

    /// <summary>结束时间，格式 <c>HH:mm</c>。</summary>
    public string EndTimeStr { get; set; } = "11:30";

    /// <summary>深拷贝。</summary>
    public ExamSubject Clone() => new()
    {
        Name = Name,
        StartTimeStr = StartTimeStr,
        EndTimeStr = EndTimeStr,
    };
}
