using System.Globalization;

namespace StudyJourney.Plugin.Countdown;

/// <summary>
/// 倒计时的**纯函数**内核（M1-B）：不碰 UI、不读配置、不写日志，因此可以被测试逐条钉死。
///
/// 为什么单独切出来：
/// <list type="bullet">
/// <item><see cref="DaysRemaining"/> 与 <see cref="TimeRemaining"/> 语义**不同**——
/// 前者是"自然日差"，跨零点就少一天，目标已过返回负数；后者是"真实剩余时长"，
/// 目标已过返回 <see cref="TimeSpan.Zero"/>，绝不返回负数。混用会得到
/// "目标当天还剩 0 天 0 时" 或 "已过一天还显示 -1 天" 这类错。</item>
/// <item>闰年、月末、时分秒边界全部交给 <see cref="DateTime"/> 自己算，本类只做减法与 clamp。</item>
/// </list>
/// </summary>
internal static class CountdownMath
{
    /// <summary>
    /// 允许的三种紧凑写法（与 v3.3.0 <c>settings.json</c> 的存量值一致）。
    /// 用 <see cref="DateTime.TryParseExact(string?, string[], IFormatProvider?, DateTimeStyles, out DateTime)"/>
    /// 而不是宽松的 <c>TryParse</c>：后者随系统区域变化，同一份配置在不同电脑上可能解析成不同日期。
    /// </summary>
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>
    /// 容错解析：接受 <c>yyyy-MM-dd HH:mm:ss</c> / <c>yyyy-MM-dd HH:mm</c> / <c>yyyy-MM-dd</c>；
    /// 空串、null、格式非法一律返回 <c>false</c> 并给 <c>default</c>，**不抛异常**。
    /// </summary>
    /// <remarks>消费者：倒计时插件（StartAsync 判"日期是否可用"）、CountdownCard（渲染前的唯一解析点）</remarks>
    public static bool TryParseDateTime(string? text, out DateTime value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        return DateTime.TryParseExact(
            text.Trim(),
            Formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    /// <summary>剩余**自然日** = <c>(target.Date - now.Date).Days</c>。目标已过 → 负数。</summary>
    /// <remarks>消费者：CountdownCard（"距高考 123 天"）</remarks>
    public static int DaysRemaining(DateTime now, DateTime target) => (target.Date - now.Date).Days;

    /// <summary>剩余**真实时长**；已到或已过目标 → <see cref="TimeSpan.Zero"/>（不返回负数）。</summary>
    /// <remarks>消费者：CountdownCard（时/分/秒那一行）</remarks>
    public static TimeSpan TimeRemaining(DateTime now, DateTime target)
        => now >= target ? TimeSpan.Zero : target - now;

    /// <summary>
    /// 备考进度 0..1（已 clamp）。
    /// <c>start &gt;= target</c>（配置写反/起始日等于考试日）或 <c>now &gt;= target</c> → 1.0；
    /// <c>now &lt;= start</c> → 0.0。
    /// </summary>
    /// <remarks>消费者：CountdownCard（进度条与百分比）</remarks>
    public static double Progress(DateTime now, DateTime start, DateTime target)
    {
        // 顺序不能换：start >= target 时进度条没有任何意义，直接给满，避免除零与"负数进度"。
        if (start >= target)
            return 1.0;

        if (now >= target)
            return 1.0;

        if (now <= start)
            return 0.0;

        var total = (target - start).Ticks;
        var elapsed = (now - start).Ticks;
        return Math.Clamp((double)elapsed / total, 0.0, 1.0);
    }
}
