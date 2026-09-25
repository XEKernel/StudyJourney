using System;
using System.Globalization;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 日期 / 时间**字符串 ↔ 控件值**的转换（2026-09-25，规划 2.7 ②）。
///
/// 起因：倒计时页的目标日期原来是一个 TextBox，要求老师手打 `yyyy-MM-dd HH:mm:ss`，
/// 起算日期要求 `yyyy-MM-dd` —— 手打格式串是老师每天最容易打错的地方。
/// 改成 DatePicker + TimePicker 之后，"控件值 → 落盘字符串"这段仍有语义
/// （空值=不显示、秒怎么办、非法值怎么办），所以抽成**纯函数**放在这里：
///   · 设置页只负责把控件值交给它 / 从它拿值填控件；
///   · 语义可以单独断言（SJ_SELFTEST=settings），不必开窗口点一遍。
///
/// 落盘格式保持与旧版完全一致（`yyyy-MM-dd HH:mm:ss` / `yyyy-MM-dd`），
/// 因此 settings.json 向后兼容，主窗口解析路径完全不用改。
/// </summary>
public static class DateTimeStr
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    // ── 解析（宽松：兼容老师以前手打过的各种写法）────────────────

    /// <summary>解析日期时间串（"2027-06-07 09:00:00"，也接受 "2027-06-07 9:00"）。空/非法 → null</summary>
    public static DateTime? ParseDateTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s.Trim(), out var dt) ? dt : null;
    }

    /// <summary>解析纯日期串（"2024-08-24"；也接受带时间的写法，取日期部分）。空/非法 → null</summary>
    public static DateTime? ParseDate(string? s)
    {
        var dt = ParseDateTime(s);
        return dt?.Date;
    }

    /// <summary>
    /// 解析 "HH:mm"（自动化固定时间触发用；兼容旧数据里的 "H:mm" / "HH:mm:ss"）。
    /// 空/无冒号/超范围 → null。
    ///
    /// ⚠ 刻意**不用** `TimeSpan.TryParse`：它会把孤零零的 "18" 解析成"18 天"
    /// （触发时刻 = 当天 +18 天 → 永远不会落在触发窗口里，规则静默失效），
    /// 也会接受 "1.02:00:00" 这种老师绝不会想写的值。
    /// </summary>
    public static TimeSpan? ParseTimeOfDay(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Trim().Split(':');
        if (parts.Length is < 2 or > 3) return null;
        if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return null;
        int sec = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out sec)) return null;
        if (h is < 0 or > 23 || m is < 0 or > 59 || sec is < 0 or > 59) return null;
        return new TimeSpan(h, m, sec);
    }

    // ── 合成 ────────────────────────────────────────────────

    /// <summary>合成落盘串。日期为空 → 空串（= 不显示倒计时，与旧版"清空输入框"等价）</summary>
    public static string ComposeDate(DateTime? date)
        => date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// 合成"yyyy-MM-dd HH:mm:ss"。时刻为空时用 <paramref name="defaultTime"/>。
    /// <paramref name="second"/> 用来保留原值里的秒 —— 老师改外观设置时不该顺手把秒吃掉。
    /// </summary>
    public static string ComposeDateTime(DateTime? date, TimeSpan? time, TimeSpan defaultTime, int second = 0)
    {
        if (date == null) return "";
        var t = time ?? defaultTime;
        int sec = Math.Clamp(second, 0, 59);
        return $"{date.Value.ToString(DateFormat, CultureInfo.InvariantCulture)} " +
               $"{t.Hours:D2}:{t.Minutes:D2}:{sec:D2}";
    }

    /// <summary>
    /// 分/时都没变时保留原值的秒，否则归零。
    /// 场景：目标日期 09:00:30，老师只去改了天气设置 —— 不该把 30 秒悄悄改成 00。
    /// </summary>
    public static int PreserveSecond(string? oldValue, DateTime date, TimeSpan time)
    {
        var old = ParseDateTime(oldValue);
        if (old == null) return 0;
        return old.Value.Date == date.Date
               && old.Value.Hour == time.Hours
               && old.Value.Minute == time.Minutes
            ? old.Value.Second
            : 0;
    }

    // ── TimePicker / DatePicker 控件值互转 ───────────────────

    /// <summary>DatePicker.SelectedDate（DateTimeOffset?）→ DateTime?（只取日期）</summary>
    public static DateTime? ToDate(DateTimeOffset? v) => v?.Date;

    /// <summary>DateTime? → DatePicker.SelectedDate（按本地时区）</summary>
    public static DateTimeOffset? ToOffset(DateTime? v) => v == null ? null : new DateTimeOffset(v.Value.Date);

    /// <summary>TimePicker.SelectedTime（TimeSpan?）→ 展示串 "HH:mm"；空 → 空串</summary>
    public static string FormatTimeOfDay(TimeSpan? t)
        => t == null ? "" : $"{t.Value.Hours:D2}:{t.Value.Minutes:D2}";

    // ── 数值型输入（自动化页的"几分钟"输入框）────────────────

    /// <summary>解析非负整数并夹到 [0, max]；空/非法 → fallback。用于"上课前 N 分钟"这类框</summary>
    public static int ParseMinutes(string? s, int fallback, int max = 1440)
    {
        if (!int.TryParse(s?.Trim(), out int v)) return fallback;
        if (v < 0) return fallback;
        return Math.Min(v, max);
    }
}
