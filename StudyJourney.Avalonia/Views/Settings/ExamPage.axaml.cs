using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;   // FontManager
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ExamPage : UserControl, ISettingsPage
{
    public ExamPage()
    {
        InitializeComponent();
    }

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

    // ── 滑条联动 ────────────────────────────────────────────
    private void ExamModeFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamModeFontSizeText, e.NewValue);
    private void ExamSubjectFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamSubjectFontSizeText, e.NewValue);
    private void ExamCountdownFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamCountdownFontSizeText, e.NewValue);
    private void ExamNameFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamNameFontSizeText, e.NewValue);
    private void ExamTimeInfoFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamTimeInfoFontSizeText, e.NewValue);
    private void ExamNextSubjectFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamNextSubjectFontSizeText, e.NewValue);
    private void ExamWarningFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamWarningFontSizeText, e.NewValue);
    private void ExamEscHintFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamEscHintFontSizeText, e.NewValue);
    private void ExamProgressBarHeightSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamProgressBarHeightText, e.NewValue);

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
