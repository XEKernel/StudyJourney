using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;   // FontManager
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ExamPage : UserControl, ISettingsPage
{
    public ExamPage()
    {
        // ⚠ _loading 初值为 true：InitializeComponent 里 XAML 自带的 Value/IsChecked 会触发
        // 各控件的 Changed 事件，不挡掉就会在"刚 new 出来"时就被判定成"老师改过"。
        InitializeComponent();

        // 一键方案（v2.22.0）：把 9 字号 + 14 色的组合收敛成 4 套现成方案
        foreach (var p in ExamPresets.All) ExamPresetCombo.Items.Add(p.Name);
        if (ExamPresetCombo.ItemCount > 0) ExamPresetCombo.SelectedIndex = 0;

        // 14 个色板：老师通过色板改色时标记未保存（Load 回填不会触发这个事件）
        foreach (var sw in new[]
                 {
                     ExamSubjectSwatch, ExamNameSwatch, ExamCountdownNormalSwatch,
                     ExamCountdownWarningSwatch, ExamCountdownCriticalSwatch, ExamDistanceSwatch,
                     ExamInfoSwatch, ExamInfoDimSwatch, ExamProgressBarSwatch, ExamProgressBarBgSwatch,
                     ExamNextSubjectSwatch, ExamWarningSwatch, ExamProgressPctSwatch, ExamBackgroundSwatch
                 })
            sw.ValueChanged += (_, _) => MarkDirty();

        ExamCountdownFontFamilyBox.SelectionChanged += (_, _) => MarkDirty();

        _loading = false;
        _dirty = false;
    }

    /// <summary>#8：未保存修改标记（与位置页/API 页同一套做法）</summary>
    private bool _dirty;
    private bool _loading = true;
    public bool IsDirty => _dirty;
    private void MarkDirty() { if (!_loading) _dirty = true; }

    /// <summary>立即进入考试模式（统一入口）</summary>
    private void EnterExamModeBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.EnterExamModeGlobal();
    }

    /// <summary>退出考试模式（对齐 WPF ExitExamMode_Click）</summary>
    private void ExitExamModeBtn_Click(object? sender, RoutedEventArgs e)
    {
        App.ExitExamModeGlobal();
    }

    // ── 一键方案 ────────────────────────────────────────────
    /// <summary>套用整套「字号 + 颜色」到各控件（不直接落盘 —— 由「保存设置」统一 Apply）</summary>
    private void ApplyExamPresetBtn_Click(object? sender, RoutedEventArgs e)
    {
        int idx = ExamPresetCombo.SelectedIndex;
        if (idx < 0 || idx >= ExamPresets.All.Count) return;
        var p = ExamPresets.All[idx];

        ExamModeFontSizeSlider.Value = p.WindowFontSize;
        ExamSubjectFontSizeSlider.Value = p.SubjectFontSize;
        ExamCountdownFontSizeSlider.Value = p.CountdownFontSize;
        ExamNameFontSizeSlider.Value = p.NameFontSize;
        ExamTimeInfoFontSizeSlider.Value = p.TimeInfoFontSize;
        ExamNextSubjectFontSizeSlider.Value = p.NextSubjectFontSize;
        ExamWarningFontSizeSlider.Value = p.WarningFontSize;
        ExamEscHintFontSizeSlider.Value = p.EscHintFontSize;
        ExamProgressBarHeightSlider.Value = p.ProgressBarHeight;

        ExamSubjectSwatch.Value = p.SubjectColor;
        ExamNameSwatch.Value = p.NameColor;
        ExamCountdownNormalSwatch.Value = p.CountdownNormalColor;
        ExamCountdownWarningSwatch.Value = p.CountdownWarningColor;
        ExamCountdownCriticalSwatch.Value = p.CountdownCriticalColor;
        ExamDistanceSwatch.Value = p.DistanceColor;
        ExamInfoSwatch.Value = p.InfoColor;
        ExamInfoDimSwatch.Value = p.InfoDimColor;
        ExamProgressBarSwatch.Value = p.ProgressBarColor;
        ExamProgressBarBgSwatch.Value = p.ProgressBarBgColor;
        ExamNextSubjectSwatch.Value = p.NextSubjectColor;
        ExamWarningSwatch.Value = p.WarningColor;
        ExamProgressPctSwatch.Value = p.ProgressPctColor;
        ExamBackgroundSwatch.Value = p.BackgroundColor;

        // 色板赋值不触发 ValueChanged（Load 回填也是同一条路径）→ 这里显式标脏
        MarkDirty();
    }

    // ── 滑条联动（顺带标脏）────────────────────────────────
    private void ExamModeFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamModeFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamSubjectFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamSubjectFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamCountdownFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamCountdownFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamNameFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamNameFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamTimeInfoFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamTimeInfoFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamNextSubjectFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamNextSubjectFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamWarningFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamWarningFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamEscHintFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamEscHintFontSizeText, e.NewValue); MarkDirty(); }
    private void ExamProgressBarHeightSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { UpdateLabel(ExamProgressBarHeightText, e.NewValue); MarkDirty(); }

    private static void UpdateLabel(TextBlock? tb, double value)
    {
        if (tb != null) tb.Text = ((int)value).ToString();
    }

    // ── 颜色 ────────────────────────────────────────────────
    // 2026-09-25（规划 2.7 ③）：原来的「hex 输入框 + 预览方块 + 选择…按钮」三件套
    // （本页 14 组、全项目 20 组）换成 ColorSwatch 色板：整行可点、显示当前颜色、
    // 值非法时画成空心并标注 → 老师再也不用看 / 手打 `#AARRGGBB`。
    // 取值时不做任何"格式化改写"（防止把 #8899CC 顺手改成 #FF8899CC），只挡非法值。
    private static string PickColor(ColorSwatch sw, string fallback)
        => ColorSwatch.TryParse(sw.Value, out _) ? sw.Value : fallback;

    // ── Load / Apply ────────────────────────────────────────
    public void Load(AppSettings s)
    {
        _loading = true;
        try
        {
            EnableExamModeCheck.IsChecked = s.EnableExamMode;
            AutoEnterExamModeCheck.IsChecked = s.AutoEnterExamMode;
            ExamModeFontSizeSlider.Value = s.ExamModeFontSize;
            ExamSubjectFontSizeSlider.Value = s.ExamSubjectFontSize;
            ExamCountdownFontSizeSlider.Value = s.ExamCountdownFontSize;
            ExamNameFontSizeSlider.Value = s.ExamNameFontSize;
            ExamTimeInfoFontSizeSlider.Value = s.ExamTimeInfoFontSize;
            ExamNextSubjectFontSizeSlider.Value = s.ExamNextSubjectFontSize;
            ExamWarningFontSizeSlider.Value = s.ExamWarningFontSize;
            ExamEscHintFontSizeSlider.Value = s.ExamEscHintFontSize;
            ExamProgressBarHeightSlider.Value = s.ExamProgressBarHeight;

            // 颜色（14 项）
            ExamSubjectSwatch.Value = s.ExamSubjectColor;
            ExamNameSwatch.Value = s.ExamNameColor;
            ExamCountdownNormalSwatch.Value = s.ExamCountdownNormalColor;
            ExamCountdownWarningSwatch.Value = s.ExamCountdownWarningColor;
            ExamCountdownCriticalSwatch.Value = s.ExamCountdownCriticalColor;
            ExamDistanceSwatch.Value = s.ExamDistanceColor;
            ExamInfoSwatch.Value = s.ExamInfoColor;
            ExamInfoDimSwatch.Value = s.ExamInfoDimColor;
            ExamProgressBarSwatch.Value = s.ExamProgressBarColor;
            ExamProgressBarBgSwatch.Value = s.ExamProgressBarBgColor;
            ExamNextSubjectSwatch.Value = s.ExamNextSubjectColor;
            ExamWarningSwatch.Value = s.ExamWarningColor;
            ExamProgressPctSwatch.Value = s.ExamProgressPctColor;
            ExamBackgroundSwatch.Value = s.ExamBackgroundColor;

            // 倒计时字体族（系统字体）
            if (ExamCountdownFontFamilyBox.Items.Count == 0)
            {
                foreach (var ff in FontManager.Current.SystemFonts)
                    ExamCountdownFontFamilyBox.Items.Add(ff.Name);
            }
            ExamCountdownFontFamilyBox.SelectedItem = s.ExamCountdownFontFamily;
        }
        finally { _loading = false; }
        _dirty = false;
    }

    public void Apply(AppSettings s)
    {
        s.EnableExamMode = EnableExamModeCheck.IsChecked == true;
        s.AutoEnterExamMode = AutoEnterExamModeCheck.IsChecked == true;
        s.ExamModeFontSize = ExamModeFontSizeSlider.Value;
        s.ExamSubjectFontSize = ExamSubjectFontSizeSlider.Value;
        s.ExamCountdownFontSize = ExamCountdownFontSizeSlider.Value;
        s.ExamNameFontSize = ExamNameFontSizeSlider.Value;
        s.ExamTimeInfoFontSize = ExamTimeInfoFontSizeSlider.Value;
        s.ExamNextSubjectFontSize = ExamNextSubjectFontSizeSlider.Value;
        s.ExamWarningFontSize = ExamWarningFontSizeSlider.Value;
        s.ExamEscHintFontSize = ExamEscHintFontSizeSlider.Value;
        s.ExamProgressBarHeight = ExamProgressBarHeightSlider.Value;

        s.ExamSubjectColor = PickColor(ExamSubjectSwatch, s.ExamSubjectColor);
        s.ExamNameColor = PickColor(ExamNameSwatch, s.ExamNameColor);
        s.ExamCountdownNormalColor = PickColor(ExamCountdownNormalSwatch, s.ExamCountdownNormalColor);
        s.ExamCountdownWarningColor = PickColor(ExamCountdownWarningSwatch, s.ExamCountdownWarningColor);
        s.ExamCountdownCriticalColor = PickColor(ExamCountdownCriticalSwatch, s.ExamCountdownCriticalColor);
        s.ExamDistanceColor = PickColor(ExamDistanceSwatch, s.ExamDistanceColor);
        s.ExamInfoColor = PickColor(ExamInfoSwatch, s.ExamInfoColor);
        s.ExamInfoDimColor = PickColor(ExamInfoDimSwatch, s.ExamInfoDimColor);
        s.ExamProgressBarColor = PickColor(ExamProgressBarSwatch, s.ExamProgressBarColor);
        s.ExamProgressBarBgColor = PickColor(ExamProgressBarBgSwatch, s.ExamProgressBarBgColor);
        s.ExamNextSubjectColor = PickColor(ExamNextSubjectSwatch, s.ExamNextSubjectColor);
        s.ExamWarningColor = PickColor(ExamWarningSwatch, s.ExamWarningColor);
        s.ExamProgressPctColor = PickColor(ExamProgressPctSwatch, s.ExamProgressPctColor);
        s.ExamBackgroundColor = PickColor(ExamBackgroundSwatch, s.ExamBackgroundColor);

        if (ExamCountdownFontFamilyBox.SelectedItem is string ff && !string.IsNullOrWhiteSpace(ff))
            s.ExamCountdownFontFamily = ff;
    }
}
