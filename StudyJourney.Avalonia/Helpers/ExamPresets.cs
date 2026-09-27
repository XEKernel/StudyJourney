using System.Collections.Generic;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 考试模式「一键方案」（2026-09-27，规划 2.7 P2「考试页 9 字号 + 14 色过载」）。
///
/// 起因：老师打开考试页看到 **23 个控件**（9 个字号滑条 + 14 个色板）平铺，无从下手；
/// 而实际需求九成是"整屏看得清" —— 也就是一套「配色 + 字号」。
/// 现在给出 4 套现成方案，一键套用；仍可展开「逐项微调」自己改。
///
/// ⚠ 这里是**纯数据 + 校验用枚举**，不依赖任何控件；套用逻辑在 `ExamPage`。
/// ⚠ 每套方案的数值必须落在 `ExamPage.axaml` 里对应 Slider 的 Min/Max 之内
///   （越界会被 Slider 静默夹取 → 老师套用了却"和描述不一样"），自检里有断言钉住这一点。
/// </summary>
public sealed record ExamPreset
{
    public required string Name { get; init; }
    public required string Hint { get; init; }

    // ── 14 色 ───────────────────────────────────────────────
    public required string SubjectColor { get; init; }
    public required string NameColor { get; init; }
    public required string CountdownNormalColor { get; init; }
    public required string CountdownWarningColor { get; init; }
    public required string CountdownCriticalColor { get; init; }
    public required string DistanceColor { get; init; }
    public required string InfoColor { get; init; }
    public required string InfoDimColor { get; init; }
    public required string ProgressBarColor { get; init; }
    public required string ProgressBarBgColor { get; init; }
    public required string NextSubjectColor { get; init; }
    public required string WarningColor { get; init; }
    public required string ProgressPctColor { get; init; }
    public required string BackgroundColor { get; init; }

    // ── 9 个尺寸 ─────────────────────────────────────────────
    public required double WindowFontSize { get; init; }
    public required double SubjectFontSize { get; init; }
    public required double CountdownFontSize { get; init; }
    public required double NameFontSize { get; init; }
    public required double TimeInfoFontSize { get; init; }
    public required double NextSubjectFontSize { get; init; }
    public required double WarningFontSize { get; init; }
    public required double EscHintFontSize { get; init; }
    public required double ProgressBarHeight { get; init; }

    /// <summary>14 色（标签 + 值），供自检逐个校验并给出可读失败信息</summary>
    public IEnumerable<(string Label, string Hex)> Colors()
    {
        yield return ("科目名颜色", SubjectColor);
        yield return ("考试名称颜色", NameColor);
        yield return ("倒计时正常颜色", CountdownNormalColor);
        yield return ("倒计时警告色", CountdownWarningColor);
        yield return ("倒计时临界色", CountdownCriticalColor);
        yield return ("距开考倒计时颜色", DistanceColor);
        yield return ("信息文字颜色", InfoColor);
        yield return ("标签弱化颜色", InfoDimColor);
        yield return ("进度条颜色", ProgressBarColor);
        yield return ("进度条背景颜色", ProgressBarBgColor);
        yield return ("下一场文字颜色", NextSubjectColor);
        yield return ("警告文字颜色", WarningColor);
        yield return ("进度百分比颜色", ProgressPctColor);
        yield return ("窗口背景颜色", BackgroundColor);
    }

    /// <summary>9 个尺寸（标签 + 值 + 该控件在 ExamPage.axaml 里的 Min/Max），供自检做区间校验</summary>
    public IEnumerable<(string Label, double Value, double Min, double Max)> Sizes()
    {
        yield return ("窗口字体", WindowFontSize, 16, 72);
        yield return ("科目名字号", SubjectFontSize, 30, 120);
        yield return ("倒计时字号", CountdownFontSize, 60, 200);
        yield return ("考试名称字号", NameFontSize, 12, 60);
        yield return ("时间信息字号", TimeInfoFontSize, 10, 40);
        yield return ("下一场字号", NextSubjectFontSize, 10, 48);
        yield return ("警告文字字号", WarningFontSize, 10, 48);
        yield return ("ESC 提示字号", EscHintFontSize, 8, 28);
        yield return ("进度条高度", ProgressBarHeight, 6, 24);
    }
}

public static class ExamPresets
{
    public static readonly IReadOnlyList<ExamPreset> All = new List<ExamPreset>
    {
        // 1. 默认 = 与出厂设置完全一致的深色黑板（选它 = 回到初始外观）
        new ExamPreset
        {
            Name = "默认（深色黑板）",
            Hint = "出厂外观：深蓝黑底 + 白字，蓝橙红三级提示",
            SubjectColor = "#FFFFFFFF", NameColor = "#AAFFFFFF",
            CountdownNormalColor = "#FFFFFFFF", CountdownWarningColor = "#FFCC8800",
            CountdownCriticalColor = "#FFCC4400", DistanceColor = "#FF8899CC",
            InfoColor = "#88FFFFFF", InfoDimColor = "#44FFFFFF",
            ProgressBarColor = "#FF5B9BD5", ProgressBarBgColor = "#22FFFFFF",
            NextSubjectColor = "#88FFFFFF", WarningColor = "#FFCC8800",
            ProgressPctColor = "#66FFFFFF", BackgroundColor = "#FF060B14",
            WindowFontSize = 32, SubjectFontSize = 64, CountdownFontSize = 120, NameFontSize = 28,
            TimeInfoFontSize = 16, NextSubjectFontSize = 22, WarningFontSize = 20,
            EscHintFontSize = 12, ProgressBarHeight = 12
        },

        // 2. 高对比：纯黑底 + 大字号，适合教室后排 / 投影偏暗的场子
        new ExamPreset
        {
            Name = "高对比（大屏远看）",
            Hint = "纯黑底、字更大更亮，黄/红警示；投影偏暗或教室大时用",
            SubjectColor = "#FFFFFFFF", NameColor = "#DDFFFFFF",
            CountdownNormalColor = "#FFFFFFFF", CountdownWarningColor = "#FFFFD400",
            CountdownCriticalColor = "#FFFF3B30", DistanceColor = "#FFCCCCCC",
            InfoColor = "#CCFFFFFF", InfoDimColor = "#66FFFFFF",
            ProgressBarColor = "#FFFFD400", ProgressBarBgColor = "#33FFFFFF",
            NextSubjectColor = "#CCFFFFFF", WarningColor = "#FFFFD400",
            ProgressPctColor = "#AAFFFFFF", BackgroundColor = "#FF000000",
            WindowFontSize = 40, SubjectFontSize = 80, CountdownFontSize = 160, NameFontSize = 34,
            TimeInfoFontSize = 20, NextSubjectFontSize = 28, WarningFontSize = 26,
            EscHintFontSize = 16, ProgressBarHeight = 18
        },

        // 3. 护眼：柔和深绿底、低对比白字，看久了不刺眼
        new ExamPreset
        {
            Name = "护眼（柔和深绿）",
            Hint = "墨绿底 + 柔白字，长时间盯屏不刺眼",
            SubjectColor = "#FFE8F5E9", NameColor = "#B3E8F5E9",
            CountdownNormalColor = "#FFD7F0DC", CountdownWarningColor = "#FFE6B85C",
            CountdownCriticalColor = "#FFE0733F", DistanceColor = "#FF9CBFA8",
            InfoColor = "#A6CFD8C0", InfoDimColor = "#555F8A6E",
            ProgressBarColor = "#FF6FBF8B", ProgressBarBgColor = "#2AFFFFFF",
            NextSubjectColor = "#A6CFD8C0", WarningColor = "#FFE6B85C",
            ProgressPctColor = "#77FFFFFF", BackgroundColor = "#FF0E1A14",
            WindowFontSize = 32, SubjectFontSize = 64, CountdownFontSize = 110, NameFontSize = 26,
            TimeInfoFontSize = 16, NextSubjectFontSize = 22, WarningFontSize = 20,
            EscHintFontSize = 12, ProgressBarHeight = 12
        },

        // 4. 明亮教室：浅底深字（有自然光的教室 / 屏幕反光时比深色底更清楚）
        new ExamPreset
        {
            Name = "明亮教室（浅底深字）",
            Hint = "浅灰底 + 深蓝黑字；教室光线强、屏幕反光时用",
            SubjectColor = "#FF10233A", NameColor = "#9910233A",
            CountdownNormalColor = "#FF10233A", CountdownWarningColor = "#FFB26A00",
            CountdownCriticalColor = "#FFC62828", DistanceColor = "#FF33506E",
            InfoColor = "#9910233A", InfoDimColor = "#55506E88",
            ProgressBarColor = "#FF2B6CB0", ProgressBarBgColor = "#222B6CB0",
            NextSubjectColor = "#9910233A", WarningColor = "#FFB26A00",
            // 浅底上百分比必须用深色，否则 #66FFFFFF 几乎看不见
            ProgressPctColor = "#6610233A", BackgroundColor = "#FFF2F4F7",
            WindowFontSize = 32, SubjectFontSize = 64, CountdownFontSize = 120, NameFontSize = 28,
            TimeInfoFontSize = 16, NextSubjectFontSize = 22, WarningFontSize = 20,
            EscHintFontSize = 12, ProgressBarHeight = 12
        }
    };
}
