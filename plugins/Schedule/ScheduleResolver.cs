using System.Globalization;
using StudyJourney.Plugin.Schedule.Models;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// 课表的两个纯函数：日期→"实际按周几上"、时间串→<see cref="TimeSpan"/>。
///
/// **刻意做成 static 无状态**：调休映射是整套自动化最容易静默出错的地方
/// （规则按星期几配，映射错了调休日就"什么都没发生"），必须能被自检直接断言，
/// 不必读真实 <c>schedule.json</c>。
/// </summary>
internal static class ScheduleResolver
{
    /// <summary>
    /// 时间串的接受格式。四档缺一不可：
    /// <list type="bullet">
    /// <item><c>HH:mm</c> —— 本项目约定格式；</item>
    /// <item><c>H:mm</c> —— 老师手打"7:00"；</item>
    /// <item><c>HH:mm:ss</c> / <c>H:mm:ss</c> —— 从别处粘来的带秒写法。</item>
    /// </list>
    /// ⚠ v3.3.0 只试了前两档，于是 <c>"07:00:00"</c> 落进 <c>TimeSpan.TryParse</c> 的宽松分支、
    /// 被静默当成 08:00，整节课错位且不报错。带秒的两档就是补这个洞。
    /// </summary>
    private static readonly string[] TimeFormats =
    [
        @"hh\:mm",
        @"h\:mm",
        @"hh\:mm\:ss",
        @"h\:mm\:ss",
    ];

    /// <summary>日期键格式（与落盘 <c>DateStr</c> 一致）。</summary>
    private const string DateKeyFormat = "yyyy-MM-dd";

    /// <summary>
    /// 把日期映射成"那天实际按星期几执行"（1=周一 … 7=周日）：
    /// 调休表里按 <c>yyyy-MM-dd</c> 精确命中且 <c>DayOfWeek</c> 在 1..7 → 用调休值；
    /// 否则用真实星期几，并把 .NET 的周日 = 0 归一化成 7。
    /// </summary>
    /// <param name="date">要解析的日期。</param>
    /// <param name="makeupDays">调休表，可为 <c>null</c> 或空。</param>
    /// <remarks>
    /// 越界的 <c>DayOfWeek</c>（0 / 8 / 99）视为**无效条目并继续往后找** ——
    /// 手改文件写坏一条不该把同一天的另一条正确记录也废掉（"命中"= 日期对 **且** 星期合法）。
    /// 同一天有多条有效记录时取**第一条**。
    /// </remarks>
    public static int ResolveEffectiveDayOfWeek(DateOnly date, IReadOnlyList<MakeupDay>? makeupDays)
    {
        if (makeupDays is { Count: > 0 })
        {
            var key = date.ToString(DateKeyFormat, CultureInfo.InvariantCulture);
            for (var i = 0; i < makeupDays.Count; i++)
            {
                var item = makeupDays[i];
                if (item is null)
                    continue;

                if (string.Equals(item.DateStr, key, StringComparison.Ordinal) &&
                    item.DayOfWeek is >= 1 and <= 7)
                {
                    return item.DayOfWeek;
                }
            }
        }

        // .NET 的 DayOfWeek 周日 = 0，本项目全链路统一用 7（最容易咬人的边界）。
        var dow = (int)date.DayOfWeek;
        return dow == 0 ? 7 : dow;
    }

    /// <summary>
    /// 该日期是否在调休表里（按 <c>yyyy-MM-dd</c> 精确匹配，不校验 <c>DayOfWeek</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ResolveEffectiveDayOfWeek"/> 的口径**故意不同**：这里回答"这天是不是被特殊安排了"，
    /// 是给界面提示用的；那里回答"实际按周几上"。两者不一致时（例如调休条目的星期写坏了）
    /// 界面应当不显示"今天按周 X 上"，由调用方比较两者决定。
    /// </remarks>
    public static bool IsMakeupDay(DateOnly date, IReadOnlyList<MakeupDay>? makeupDays)
    {
        if (makeupDays is not { Count: > 0 })
            return false;

        var key = date.ToString(DateKeyFormat, CultureInfo.InvariantCulture);
        for (var i = 0; i < makeupDays.Count; i++)
        {
            if (string.Equals(makeupDays[i]?.DateStr, key, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 宽松解析 <c>"HH:mm"</c>。**解析失败返回 <c>false</c>，绝不抛异常。**
    /// </summary>
    /// <param name="text">待解析文本，可为 <c>null</c>。</param>
    /// <param name="value">解析成功时的时间（保证在 <c>[00:00, 24:00)</c> 内）。</param>
    public static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        if (TimeSpan.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture, out var exact))
            return IsTimeOfDay(exact, out value);

        // 兜底：TimeSpan.TryParse 能吃掉别的分隔写法（"7:5"、"08.30" 之类）。
        // 注意 "07" 会被它解析成 7 **天**，所以下面必须用 IsTimeOfDay 卡住取值范围。
        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var loose))
            return IsTimeOfDay(loose, out value);

        return false;
    }

    /// <summary>把时间格式化成契约要求的 <c>HH:mm</c>（24 小时制、零填充）。</summary>
    public static string FormatTime(TimeSpan value)
        => value.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    /// <summary>取值范围校验：只接受一天之内的时间。</summary>
    private static bool IsTimeOfDay(TimeSpan candidate, out TimeSpan value)
    {
        if (candidate >= TimeSpan.Zero && candidate < TimeSpan.FromDays(1))
        {
            value = candidate;
            return true;
        }

        value = default;
        return false;
    }
}
