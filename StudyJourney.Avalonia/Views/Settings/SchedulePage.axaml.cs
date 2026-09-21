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

        // ── 调休（补课日）初始化 ──────────────────────────────
        // 数据存在 schedule.json（ScheduleData.MakeupDays），不是 settings.json ——
        // 它属于"课表/日历"范畴，跟着课表走更合理；所以不走 ISettingsPage 的 Load/Apply，
        // 增删即刻落盘（列表型配置即时生效比"记得点保存"更不容易出错）。
        for (int d = 1; d <= 7; d++)
            MakeupDayCombo.Items.Add(MakeupDay.WeekName(d));
        MakeupDayCombo.SelectedIndex = 4;              // 默认"周五"，最常见的补课目标
        MakeupDatePicker.SelectedDate = DateTimeOffset.Now;
        ReloadMakeupList();
    }

    /// <summary>按当前 MakeupDays 重建列表（行内含删除按钮）</summary>
    private void ReloadMakeupList()
    {
        MakeupListPanel.Children.Clear();
        var days = App.Schedule.Data.MakeupDays ??= new();
        // 按日期排序，方便查看
        days.Sort((a, b) => string.CompareOrdinal(a.DateStr, b.DateStr));

        foreach (var m in days)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = m.Display,
                FontSize = 13,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            });

            var del = new Button { Content = "删除", FontSize = 12, Padding = new global::Avalonia.Thickness(8, 2) };
            var captured = m;
            del.Click += (_, _) =>
            {
                App.Schedule.Data.MakeupDays.Remove(captured);
                App.Schedule.Save();
                ReloadMakeupList();
                Helpers.AppLogger.Info($"[调休] 已删除：{captured.Display}");
            };
            Grid.SetColumn(del, 1);
            row.Children.Add(del);
            MakeupListPanel.Children.Add(row);
        }

        MakeupEmptyTb.IsVisible = days.Count == 0;
    }

    private void MakeupAdd_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (MakeupDatePicker.SelectedDate is not { } sel)
            {
                Helpers.AppLogger.Warn("[调休] 未选择日期，忽略");
                return;
            }
            if (MakeupDayCombo.SelectedIndex < 0) return;

            string key = sel.Date.ToString("yyyy-MM-dd");
            int target = MakeupDayCombo.SelectedIndex + 1;
            var days = App.Schedule.Data.MakeupDays ??= new();

            var exist = days.FirstOrDefault(x => x.DateStr == key);
            if (exist != null)
            {
                // 同一天再次添加 = 修改补哪一天（比"报错说已存在"更好用）
                exist.DayOfWeek = target;
                Helpers.AppLogger.Info($"[调休] 已修改：{key} 补 {MakeupDay.WeekName(target)}");
            }
            else
            {
                days.Add(new MakeupDay { DateStr = key, DayOfWeek = target });
                Helpers.AppLogger.Info($"[调休] 已添加：{key} 补 {MakeupDay.WeekName(target)}");
            }

            App.Schedule.Save();       // 触发 DataChanged → 提醒/自动化立即按新映射工作
            ReloadMakeupList();
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("[调休] 添加失败", ex);
        }
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
                Title = "选择提醒音文件（仅支持 wav）",
                AllowMultiple = false,
                // #15 修复：PlaySoundW (winmm) 只支持 wav，mp3/wma 选了也无声 → 过滤器收窄避免误导
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("WAV 音频") { Patterns = new[] { "*.wav" } },
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
        RemindClassEndSoon10Check.IsChecked = s.RemindClassEndSoon10;
        RemindClassEndSoonCheck.IsChecked = s.RemindClassEndSoon;
        RemindClassEndCheck.IsChecked = s.RemindClassEnd;
        RemindNextClassSoonCheck.IsChecked = s.RemindNextClassSoon;
        RemindDayEndCheck.IsChecked = s.RemindDayEnd;
        RemindSpecialPeriodCheck.IsChecked = s.RemindSpecialPeriod;
        ReminderStyleCapsule.IsChecked = s.ReminderStyle != 1;
        ReminderStyleToast.IsChecked = s.ReminderStyle == 1;
    }

    public void Apply(AppSettings s)
    {
        s.EnableReminderSound = EnableReminderSoundCheck.IsChecked == true;
        s.ReminderSoundPath = ReminderSoundPathBox.Text ?? "";
        s.RemindClassStart = RemindClassStartCheck.IsChecked == true;
        s.RemindClassMid = RemindClassMidCheck.IsChecked == true;
        s.RemindClassEndSoon10 = RemindClassEndSoon10Check.IsChecked == true;
        s.RemindClassEndSoon = RemindClassEndSoonCheck.IsChecked == true;
        s.RemindClassEnd = RemindClassEndCheck.IsChecked == true;
        s.RemindNextClassSoon = RemindNextClassSoonCheck.IsChecked == true;
        s.RemindDayEnd = RemindDayEndCheck.IsChecked == true;
        s.RemindSpecialPeriod = RemindSpecialPeriodCheck.IsChecked == true;
        s.ReminderStyle = ReminderStyleToast.IsChecked == true ? 1 : 0;
    }
}
