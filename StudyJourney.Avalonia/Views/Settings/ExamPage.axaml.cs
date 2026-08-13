using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ExamPage : UserControl, ISettingsPage
{
    public ExamPage()
    {
        InitializeComponent();
    }

    /// <summary>立即进入考试模式（统一入口，含课表栏互斥联动）</summary>
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

    // ── 颜色选择 ────────────────────────────────────────────
    private void PickExamSubjectColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamSubjectColorBox, ExamSubjectColorPreview);
    private void PickExamNameColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamNameColorBox, ExamNameColorPreview);
    private void PickExamCountdownNormalColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamCountdownNormalColorBox, ExamCountdownNormalColorPreview);
    private void PickExamCountdownWarningColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamCountdownWarningColorBox, ExamCountdownWarningColorPreview);
    private void PickExamCountdownCriticalColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamCountdownCriticalColorBox, ExamCountdownCriticalColorPreview);
    private void PickExamDistanceColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamDistanceColorBox, ExamDistanceColorPreview);
    private void PickExamInfoColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamInfoColorBox, ExamInfoColorPreview);
    private void PickExamInfoDimColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamInfoDimColorBox, ExamInfoDimColorPreview);
    private void PickExamProgressBarColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamProgressBarColorBox, ExamProgressBarColorPreview);
    private void PickExamProgressBarBgColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamProgressBarBgColorBox, ExamProgressBarBgColorPreview);
    private void PickExamNextSubjectColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamNextSubjectColorBox, ExamNextSubjectColorPreview);
    private void PickExamWarningColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamWarningColorBox, ExamWarningColorPreview);
    private void PickExamProgressPctColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamProgressPctColorBox, ExamProgressPctColorPreview);
    private void PickExamBackgroundColor_Click(object? sender, RoutedEventArgs e) => PickColor(ExamBackgroundColorBox, ExamBackgroundColorPreview);

    private void PickColor(TextBox box, Border preview)
    {
        var dlg = new ColorPickerDialog(box.Text ?? "#FFFFFFFF");
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) dlg.ShowDialog(owner); else dlg.Show();
        dlg.Closed += (_, _) =>
        {
            if (dlg.SelectedHex != null)
            {
                box.Text = dlg.SelectedHex;
                UpdateColorPreview(preview, dlg.SelectedHex);
            }
        };
    }

    private static void UpdateColorPreview(Border? preview, string? hex)
    {
        if (preview == null || string.IsNullOrEmpty(hex)) return;
        try { preview.Background = new SolidColorBrush(Color.Parse(hex)); } catch { }
    }

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

        // 颜色
        ExamSubjectColorBox.Text = s.ExamSubjectColor;
        ExamNameColorBox.Text = s.ExamNameColor;
        ExamCountdownNormalColorBox.Text = s.ExamCountdownNormalColor;
        ExamCountdownWarningColorBox.Text = s.ExamCountdownWarningColor;
        ExamCountdownCriticalColorBox.Text = s.ExamCountdownCriticalColor;
        ExamDistanceColorBox.Text = s.ExamDistanceColor;
        ExamInfoColorBox.Text = s.ExamInfoColor;
        ExamInfoDimColorBox.Text = s.ExamInfoDimColor;
        ExamProgressBarColorBox.Text = s.ExamProgressBarColor;
        ExamProgressBarBgColorBox.Text = s.ExamProgressBarBgColor;
        ExamNextSubjectColorBox.Text = s.ExamNextSubjectColor;
        ExamWarningColorBox.Text = s.ExamWarningColor;
        ExamProgressPctColorBox.Text = s.ExamProgressPctColor;
        ExamBackgroundColorBox.Text = s.ExamBackgroundColor;
        foreach (var (box, preview) in new[] {
            (ExamSubjectColorBox, ExamSubjectColorPreview), (ExamNameColorBox, ExamNameColorPreview),
            (ExamCountdownNormalColorBox, ExamCountdownNormalColorPreview), (ExamCountdownWarningColorBox, ExamCountdownWarningColorPreview),
            (ExamCountdownCriticalColorBox, ExamCountdownCriticalColorPreview), (ExamDistanceColorBox, ExamDistanceColorPreview),
            (ExamInfoColorBox, ExamInfoColorPreview), (ExamInfoDimColorBox, ExamInfoDimColorPreview),
            (ExamProgressBarColorBox, ExamProgressBarColorPreview), (ExamProgressBarBgColorBox, ExamProgressBarBgColorPreview),
            (ExamNextSubjectColorBox, ExamNextSubjectColorPreview), (ExamWarningColorBox, ExamWarningColorPreview),
            (ExamProgressPctColorBox, ExamProgressPctColorPreview), (ExamBackgroundColorBox, ExamBackgroundColorPreview)
        })
            UpdateColorPreview(preview, box.Text);

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

        s.ExamSubjectColor = ExamSubjectColorBox.Text ?? s.ExamSubjectColor;
        s.ExamNameColor = ExamNameColorBox.Text ?? s.ExamNameColor;
        s.ExamCountdownNormalColor = ExamCountdownNormalColorBox.Text ?? s.ExamCountdownNormalColor;
        s.ExamCountdownWarningColor = ExamCountdownWarningColorBox.Text ?? s.ExamCountdownWarningColor;
        s.ExamCountdownCriticalColor = ExamCountdownCriticalColorBox.Text ?? s.ExamCountdownCriticalColor;
        s.ExamDistanceColor = ExamDistanceColorBox.Text ?? s.ExamDistanceColor;
        s.ExamInfoColor = ExamInfoColorBox.Text ?? s.ExamInfoColor;
        s.ExamInfoDimColor = ExamInfoDimColorBox.Text ?? s.ExamInfoDimColor;
        s.ExamProgressBarColor = ExamProgressBarColorBox.Text ?? s.ExamProgressBarColor;
        s.ExamProgressBarBgColor = ExamProgressBarBgColorBox.Text ?? s.ExamProgressBarBgColor;
        s.ExamNextSubjectColor = ExamNextSubjectColorBox.Text ?? s.ExamNextSubjectColor;
        s.ExamWarningColor = ExamWarningColorBox.Text ?? s.ExamWarningColor;
        s.ExamProgressPctColor = ExamProgressPctColorBox.Text ?? s.ExamProgressPctColor;
        s.ExamBackgroundColor = ExamBackgroundColorBox.Text ?? s.ExamBackgroundColor;

        if (ExamCountdownFontFamilyBox.SelectedItem is string ff && !string.IsNullOrWhiteSpace(ff))
            s.ExamCountdownFontFamily = ff;
    }
}
