using System;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 时间模拟器（调试用）：给全应用加一个时间偏移，
/// 可快速跳到"快下课/下课后/快上课"等关键时刻观察提醒与课表栏效果。
/// </summary>
public static class TimeSimulator
{
    private static TimeSpan _offset = TimeSpan.Zero;

    /// <summary>模拟后的当前时间（真实时间 + 偏移）</summary>
    public static DateTime Now => DateTime.Now + _offset;

    public static TimeSpan Offset => _offset;

    /// <summary>设置时间偏移（可为负）</summary>
    public static void SetOffset(TimeSpan offset)
    {
        _offset = offset;
    }

    /// <summary>跳到指定时刻（基于模拟日期，只调整时分秒）</summary>
    public static void JumpTo(TimeSpan timeOfDay)
    {
        var target = Now.Date + timeOfDay;
        _offset = target - DateTime.Now;
    }

    public static void Reset()
    {
        _offset = TimeSpan.Zero;
    }

    public static string FormatNow() => Now.ToString("yyyy-MM-dd HH:mm:ss");
}
