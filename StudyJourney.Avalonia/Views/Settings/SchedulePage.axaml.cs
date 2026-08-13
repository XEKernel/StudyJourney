using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class SchedulePage : UserControl, ISettingsPage
{
    public SchedulePage()
    {
        InitializeComponent();
    }

    /// <summary>打开课表编辑窗口（自定义课表）</summary>
    private void EditScheduleBtn_Click(object? sender, RoutedEventArgs e)
    {
        var win = new Views.ScheduleEditorWindow();
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) win.Show(owner);
        else win.Show();
    }

    /// <summary>浏览选择提醒音 wav 文件（对齐 WPF BrowseReminderSound_Click）</summary>
    private async void BrowseReminderSound_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择提醒音文件（wav）",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("音频文件") { Patterns = new[] { "*.wav", "*.mp3", "*.wma" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
                }
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) ReminderSoundPathBox.Text = path;
        }
        catch { }
    }

    public void Load(AppSettings s)
    {
        EnableReminderSoundCheck.IsChecked = s.EnableReminderSound;
        ReminderSoundPathBox.Text = s.ReminderSoundPath;
        RemindClassStartCheck.IsChecked = s.RemindClassStart;
        RemindClassMidCheck.IsChecked = s.RemindClassMid;
        RemindClassEndSoonCheck.IsChecked = s.RemindClassEndSoon;
        RemindClassEndCheck.IsChecked = s.RemindClassEnd;
        RemindNextClassSoonCheck.IsChecked = s.RemindNextClassSoon;
        RemindDayEndCheck.IsChecked = s.RemindDayEnd;
        RemindSpecialPeriodCheck.IsChecked = s.RemindSpecialPeriod;
    }

    public void Apply(AppSettings s)
    {
        s.EnableReminderSound = EnableReminderSoundCheck.IsChecked == true;
        s.ReminderSoundPath = ReminderSoundPathBox.Text ?? "";
        s.RemindClassStart = RemindClassStartCheck.IsChecked == true;
        s.RemindClassMid = RemindClassMidCheck.IsChecked == true;
        s.RemindClassEndSoon = RemindClassEndSoonCheck.IsChecked == true;
        s.RemindClassEnd = RemindClassEndCheck.IsChecked == true;
        s.RemindNextClassSoon = RemindNextClassSoonCheck.IsChecked == true;
        s.RemindDayEnd = RemindDayEndCheck.IsChecked == true;
        s.RemindSpecialPeriod = RemindSpecialPeriodCheck.IsChecked == true;
    }
}
