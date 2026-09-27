using System;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 「魔法值 0 = 关闭」→ 显式开关的映射（2026-09-27，规划 2.7 P1 收尾）。
/// 之前自动切换/刷新间隔用滑条 0 表示"不自动"，老师看不懂；
/// 现在 UI = CheckBox（开/关）+ 滑条（最小值 > 0），落盘格式不变（0 = 关）。
/// </summary>
public static class IntervalSwitch
{
    /// <summary>落盘值 → UI。关闭（≤0）→ 关 + 滑条给默认值；开启 → 开 + 滑条夹进 [min,max]</summary>
    public static (bool On, double Slider) ToUi(int saved, double fallback, double min, double max)
    {
        if (saved <= 0) return (false, Math.Clamp(fallback, min, max));
        return (true, Math.Clamp((double)saved, min, max));
    }

    /// <summary>UI → 落盘值。关 → 0（旧格式兼容）；开 → 滑条值夹进 [min,max] 再取整</summary>
    public static int FromUi(bool on, double slider, double min, double max)
        => on ? (int)Math.Clamp(slider, min, max) : 0;
}
