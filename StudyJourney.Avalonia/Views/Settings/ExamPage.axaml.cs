using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ExamPage : UserControl, ISettingsPage
{
    public ExamPage()
    {
        InitializeComponent();
    }

    /// <summary>立即进入考试模式（显眼入口，不依赖右键菜单/托盘）</summary>
    private void EnterExamModeBtn_Click(object? sender, RoutedEventArgs e)
    {
        var win = new Views.ExamModeWindow();
        win.Show();
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

    private void ExamProgressBarHeightSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateLabel(ExamProgressBarHeightText, e.NewValue);

    private static void UpdateLabel(TextBlock? tb, double value)
    {
        if (tb != null) tb.Text = ((int)value).ToString();
    }

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
        ExamProgressBarHeightSlider.Value = s.ExamProgressBarHeight;
        ExamBackgroundColorBox.Text = s.ExamBackgroundColor;
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
        s.ExamProgressBarHeight = ExamProgressBarHeightSlider.Value;
        s.ExamBackgroundColor = ExamBackgroundColorBox.Text ?? s.ExamBackgroundColor;
    }
}
