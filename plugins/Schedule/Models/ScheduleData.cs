namespace StudyJourney.Plugin.Schedule.Models;

/// <summary>
/// <c>schedule.json</c> 的根容器（v3.3.0 遗留格式，零迁移——老师机器上的老文件必须原地读懂）。
///
/// 五个顶层节，**属性声明顺序 = 落盘顺序**（Entries → Exams → TimeTemplates →
/// DayTimeTemplates → MakeupDays），与老文件一致，便于人工 diff。
///
/// 三个不要动的约定：
/// <list type="number">
/// <item>属性名就是 JSON 键名（PascalCase），不加 <c>[JsonPropertyName]</c>；</item>
/// <item><see cref="Exams"/> 对课表插件是**纯过路数据**：读进来只为了原样写回，不许丢、不许改；</item>
/// <item>所有集合在 <see cref="Normalize"/> 后保证非 null —— 手改文件写成 <c>"Entries": null</c> 也不会炸。</item>
/// </list>
/// </summary>
internal sealed class ScheduleData
{
    /// <summary>全部课节（跨整周，按 DayOfWeek + Period 排列）。</summary>
    public List<ScheduleEntry> Entries { get; set; } = [];

    /// <summary>考试集合。<b>课表插件不消费</b>，读写时原样保留（M4 归考试插件）。</summary>
    public List<ExamEntry> Exams { get; set; } = [];

    /// <summary>全周通用作息模板（某天没在 <see cref="DayTimeTemplates"/> 单独定制时用它）。</summary>
    public List<TimeTemplate> TimeTemplates { get; set; } = [];

    /// <summary>按星期几独立定制的作息模板：key = 1(周一)…7(周日)，JSON 键是字符串、值是数组。</summary>
    public Dictionary<int, List<TimeTemplate>> DayTimeTemplates { get; set; } = [];

    /// <summary>调休（补课日）表。</summary>
    public List<MakeupDay> MakeupDays { get; set; } = [];

    /// <summary>把可能为 null 的集合补成空集合（反序列化后立刻调用）。</summary>
    public void Normalize()
    {
        Entries ??= [];
        Exams ??= [];
        TimeTemplates ??= [];
        DayTimeTemplates ??= [];
        MakeupDays ??= [];

        // 字典的值也可能是 null（手写成 "1": null），逐项补齐，读侧就不用到处判空。
        foreach (var key in DayTimeTemplates.Keys.ToList())
        {
            if (DayTimeTemplates[key] is null)
                DayTimeTemplates[key] = [];
        }
    }

    /// <summary>取某星期几应使用的作息模板：独立定制优先，缺省回退全周默认。</summary>
    public List<TimeTemplate> GetTemplatesFor(int dayOfWeek)
        => DayTimeTemplates.TryGetValue(dayOfWeek, out var custom) && custom is not null
            ? custom
            : TimeTemplates;

    /// <summary>
    /// 整份深拷贝（<see cref="Entries"/> / <see cref="Exams"/> / 模板 / 调休全复制）。
    /// 用途：Save 前拍快照，避免边写边被后台线程改。
    /// </summary>
    public ScheduleData Clone() => new()
    {
        Entries = [.. Entries.Select(e => e.Clone())],
        Exams = [.. Exams.Select(e => e.Clone())],
        TimeTemplates = [.. TimeTemplates.Select(t => t.Clone())],
        DayTimeTemplates = DayTimeTemplates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(t => t.Clone()).ToList()),
        MakeupDays = [.. MakeupDays.Select(m => m.Clone())],
    };
}
