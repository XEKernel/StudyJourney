using System.Collections.Generic;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 提醒「快捷档」（2026-09-27，规划 2.7 P2「提醒固定阈值 8 个开关」）。
///
/// 起因：设置页 8 个提醒开关平铺，老师不知道哪几个该开（尤其"距下课 10 分钟""特殊时段"这类），
/// 出厂默认又是分散的（有的开有的关）→ 只能一个个试。现在给 4 个档位，选完仍可逐项微调。
///
/// ⚠ `Toggles` 的顺序必须与 `SchedulePage.axaml` 里 8 个复选框的**界面顺序**一致，
///   也即与 `SchedulePage.Load/Apply` 的赋值顺序一致（自检里断言数量与顺序）。
/// </summary>
public sealed record ReminderPreset(
    string Name,
    string Hint,
    bool ClassStart,
    bool ClassMid,
    bool NextClassSoon,
    bool EndSoon10,
    bool EndSoon1,
    bool ClassEnd,
    bool DayEnd,
    bool SpecialPeriod)
{
    /// <summary>
    /// 8 个开关（顺序：预备铃 / 上课提醒 / 课间提醒 / 距下课 10 分钟 / 距下课 1 分钟 /
    /// 下课提醒 / 一天结束 / 特殊时段）—— 与 SchedulePage 的复选框顺序严格一致。
    /// </summary>
    public bool[] Toggles => new[]
    {
        ClassStart, ClassMid, NextClassSoon, EndSoon10, EndSoon1, ClassEnd, DayEnd, SpecialPeriod
    };
}

public static class ReminderPresets
{
    public static readonly IReadOnlyList<ReminderPreset> All = new List<ReminderPreset>
    {
        // 标准：上课/下课/课间/课前预告都给，但不打扰早晚自习（最贴近多数老师的用法）
        new ReminderPreset("标准（推荐）", "上课、下课、课间、课前预告都有；不打扰早晚自习",
            ClassStart: true, ClassMid: true, NextClassSoon: true,
            EndSoon10: true, EndSoon1: true, ClassEnd: true,
            DayEnd: true, SpecialPeriod: false),

        // 简洁：只留"上课了""下课了"两句，其余全关
        new ReminderPreset("简洁（只留上下课）", "只在上课、下课时各提醒一次，最安静",
            ClassStart: false, ClassMid: true, NextClassSoon: false,
            EndSoon10: false, EndSoon1: false, ClassEnd: true,
            DayEnd: false, SpecialPeriod: false),

        new ReminderPreset("全部开启", "8 项提醒全开（含早晚自习等特殊时段）",
            ClassStart: true, ClassMid: true, NextClassSoon: true,
            EndSoon10: true, EndSoon1: true, ClassEnd: true,
            DayEnd: true, SpecialPeriod: true),

        new ReminderPreset("全部关闭", "不弹任何提醒（提示音与提醒方式仍保留）",
            ClassStart: false, ClassMid: false, NextClassSoon: false,
            EndSoon10: false, EndSoon1: false, ClassEnd: false,
            DayEnd: false, SpecialPeriod: false)
    };
}
