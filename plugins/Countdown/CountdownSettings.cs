using System.Text.Json.Nodes;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Plugin.Countdown;

/// <summary>
/// 倒计时插件的 8 个配置字段（与 <c>plugin.json</c> 的 <c>configFields</c> 一一对应）。
///
/// **为什么手动取值、不用 <c>ReadSection&lt;T&gt;(JsonSerializerContext)</c>**：
/// 倒计时只有 8 个标量字段，为它写一个 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// 反而多一份要维护的样板，且反序列化对"字段类型不符"的处理是**整节**失败/静默，
/// 而这里要的是"坏一个字段只回落那一个字段，其余照读"。手写取值的代价是 8 行，收益是逐字段容错。
///
/// 容错规则（D 系列"配置永不崩"）：
/// <list type="bullet">
/// <item>节为 <c>null</c>（纯内存空配置）或键缺失 → 该字段取默认值，**不记 Warn**（缺失是常态，不是故障）；</item>
/// <item>键存在但类型不符（例如 <c>ShowDays</c> 存了字符串）→ 该字段回落默认值 + 记一条 Warn，**绝不抛**。</item>
/// </list>
/// </summary>
internal sealed class CountdownSettings
{
    /// <summary>默认高考时间（与 v3.3.0 存量值一致）。</summary>
    internal const string DefaultGaokaoDate = "2027-06-07 09:00:00";

    /// <summary>默认备考起始日（本届高三入学的那个暑假）。</summary>
    internal const string DefaultStartDate = "2024-08-24";

    /// <summary>目标时间字符串，原样保存（解析失败由调用方决定降级行为，本类不改写用户输入）。</summary>
    public string GaokaoDateStr { get; init; } = DefaultGaokaoDate;

    /// <summary>备考起始日字符串。</summary>
    public string StartDateStr { get; init; } = DefaultStartDate;

    /// <summary>是否显示大号剩余天数。</summary>
    public bool ShowDays { get; init; } = true;

    /// <summary>时/分/秒一行里是否显示"时"。</summary>
    public bool ShowHours { get; init; } = true;

    /// <summary>是否显示"分"。</summary>
    public bool ShowMinutes { get; init; } = true;

    /// <summary>是否显示"秒"。</summary>
    public bool ShowSeconds { get; init; } = true;

    /// <summary>是否显示备考进度条（关闭时整块进度区不建控件）。</summary>
    public bool ShowProgressBar { get; init; } = true;

    /// <summary>进度区排版：<c>false</c> 横向（文字左、进度右），<c>true</c> 纵向（文字上、进度下）。</summary>
    public bool CountdownProgressBarStyle { get; init; }

    /// <summary>8 个字段名。**唯一来源**——<see cref="WriteDefaults"/> 与测试都从这里取，避免手抄键名打错。</summary>
    internal static readonly string[] FieldNames =
    [
        nameof(GaokaoDateStr),
        nameof(StartDateStr),
        nameof(ShowDays),
        nameof(ShowHours),
        nameof(ShowMinutes),
        nameof(ShowSeconds),
        nameof(ShowProgressBar),
        nameof(CountdownProgressBarStyle),
    ];

    /// <summary>全默认值（纯内存空的配置节、或 Reset 之后的状态）。</summary>
    internal static CountdownSettings CreateDefault() => new();

    /// <summary>从配置节解析出强类型设置；<paramref name="section"/> 为 null 时全用默认值。</summary>
    /// <param name="section">核心摘出来的本插件配置节（深拷贝，随便读）。</param>
    /// <param name="logger">用于记录"类型不符"的 Warn；测试可传 null。</param>
    internal static CountdownSettings FromSection(JsonNode? section, IPluginLogger? logger) => new()
    {
        GaokaoDateStr = ReadString(section, nameof(GaokaoDateStr), DefaultGaokaoDate, logger),
        StartDateStr = ReadString(section, nameof(StartDateStr), DefaultStartDate, logger),
        ShowDays = ReadBool(section, nameof(ShowDays), true, logger),
        ShowHours = ReadBool(section, nameof(ShowHours), true, logger),
        ShowMinutes = ReadBool(section, nameof(ShowMinutes), true, logger),
        ShowSeconds = ReadBool(section, nameof(ShowSeconds), true, logger),
        ShowProgressBar = ReadBool(section, nameof(ShowProgressBar), true, logger),
        CountdownProgressBarStyle = ReadBool(section, nameof(CountdownProgressBarStyle), false, logger),
    };

    /// <summary>
    /// 把 8 个键**就地**写成默认值（供 <c>ISettingsContributor.ResetToDefaults</c> 调用）。
    /// 只改这 8 个键，节点上其它键原样保留——核心负责整体写回。
    /// </summary>
    internal static void WriteDefaults(JsonObject target)
    {
        target[nameof(GaokaoDateStr)] = JsonValue.Create(DefaultGaokaoDate);
        target[nameof(StartDateStr)] = JsonValue.Create(DefaultStartDate);
        target[nameof(ShowDays)] = JsonValue.Create(true);
        target[nameof(ShowHours)] = JsonValue.Create(true);
        target[nameof(ShowMinutes)] = JsonValue.Create(true);
        target[nameof(ShowSeconds)] = JsonValue.Create(true);
        target[nameof(ShowProgressBar)] = JsonValue.Create(true);
        target[nameof(CountdownProgressBarStyle)] = JsonValue.Create(false);
    }

    /// <summary>
    /// 读字符串。**空串被视为"用户主动清空"**，原样返回（日期字段要靠它表达"未设置"），
    /// 只有类型不符才回落默认值。
    /// </summary>
    private static string ReadString(JsonNode? section, string key, string fallback, IPluginLogger? logger)
    {
        if (section?[key] is not JsonValue value)
            return fallback;

        if (value.TryGetValue<string>(out var text))
            return text;

        logger?.Warn($"配置项 {key} 类型不符（期望字符串），已回落到默认值。");
        return fallback;
    }

    /// <summary>读布尔。JSON 的 <c>true</c>/<c>false</c> 之外（含字符串 "true" / 数字 1）一律回落默认值并 Warn。</summary>
    private static bool ReadBool(JsonNode? section, string key, bool fallback, IPluginLogger? logger)
    {
        if (section?[key] is not JsonValue value)
            return fallback;

        if (value.TryGetValue<bool>(out var flag))
            return flag;

        logger?.Warn($"配置项 {key} 类型不符（期望 true/false），已回落到默认值 {fallback}。");
        return fallback;
    }
}
