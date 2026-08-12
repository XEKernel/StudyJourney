using Avalonia.Controls;
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
