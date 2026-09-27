using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class SchedulePage : UserControl, ISettingsPage
{
    public SchedulePage()
    {
        // ⚠ _loading 初值 true：XAML 自带的 IsChecked 会触发 Changed 事件，不挡掉会误判成"老师改过"
        InitializeComponent();

        // ── 提醒快捷档（v2.22.0，规划 2.7 P2）──────────────────
        foreach (var p in ReminderPresets.All) ReminderPresetCombo.Items.Add(p.Name);
        if (ReminderPresetCombo.ItemCount > 0) ReminderPresetCombo.SelectedIndex = 0;

        foreach (var cb in ToggleCheckBoxes)
            cb.IsCheckedChanged += (_, _) => MarkDirty();
        EnableReminderSoundCheck.IsCheckedChanged += (_, _) => MarkDirty();
        ReminderStyleCapsule.IsCheckedChanged += (_, _) => MarkDirty();
        ReminderSoundPathBox.TextChanged += (_, _) => MarkDirty();

        _loading = false;
        _dirty = false;

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

    /// <summary>#8：未保存修改标记（与位置页/API 页/考试页同一套做法）</summary>
    private bool _dirty;
    private bool _loading = true;
    public bool IsDirty => _dirty;
    private void MarkDirty() { if (!_loading) _dirty = true; }

    /// <summary>
    /// 8 个提醒开关，**顺序必须与 `ReminderPreset.Toggles` 一致**
    /// （预备铃 / 上课 / 课间 / 距下课 10 分钟 / 距下课 1 分钟 / 下课 / 一天结束 / 特殊时段）。
    /// 套用档位与 Load/Apply 都走这个数组，避免两处顺序写岔。
    /// </summary>
    private CheckBox[] ToggleCheckBoxes => new[]
    {
        RemindClassStartCheck, RemindClassMidCheck, RemindNextClassSoonCheck,
        RemindClassEndSoon10Check, RemindClassEndSoonCheck, RemindClassEndCheck,
        RemindDayEndCheck, RemindSpecialPeriodCheck
    };

    /// <summary>套用提醒档位（只改界面，等待「保存设置」落盘）</summary>
    private void ApplyReminderPresetBtn_Click(object? sender, RoutedEventArgs e)
    {
        int idx = ReminderPresetCombo.SelectedIndex;
        if (idx < 0 || idx >= ReminderPresets.All.Count) return;
        var toggles = ReminderPresets.All[idx].Toggles;
        var boxes = ToggleCheckBoxes;
        if (toggles.Length != boxes.Length) return;      // 防呆：顺序表对不上就什么都不做
        for (int i = 0; i < boxes.Length; i++) boxes[i].IsChecked = toggles[i];
        MarkDirty();
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
                // ⚠ 按 DateStr 在**当前**列表实例里重新查找，不要直接 Remove(captured)：
                // 课表可能被外部重载（远程 PUT /api/schedule → Schedule.Reload() 会换掉 _data 实例），
                // 那时 captured 已不在新列表里，Remove 返回 false → 用户看到"点删除没反应"且无任何报错。
                var list = App.Schedule.Data.MakeupDays ??= new();
                var hit = list.FirstOrDefault(x => x.DateStr == captured.DateStr);
                if (hit == null)
                {
                    Helpers.AppLogger.Warn($"[调休] 要删除的 {captured.DateStr} 已不在列表中（可能被外部重载），仅刷新界面");
                    ReloadMakeupList();
                    return;
                }
                list.Remove(hit);
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
        _loading = true;
        try
        {
            EnableReminderSoundCheck.IsChecked = s.EnableReminderSound;
            ReminderSoundPathBox.Text = s.ReminderSoundPath;
            var boxes = ToggleCheckBoxes;
            bool[] values = { s.RemindClassStart, s.RemindClassMid, s.RemindNextClassSoon,
                              s.RemindClassEndSoon10, s.RemindClassEndSoon, s.RemindClassEnd,
                              s.RemindDayEnd, s.RemindSpecialPeriod };
            for (int i = 0; i < boxes.Length && i < values.Length; i++) boxes[i].IsChecked = values[i];
            ReminderStyleCapsule.IsChecked = s.ReminderStyle != 1;
            ReminderStyleToast.IsChecked = s.ReminderStyle == 1;
        }
        finally { _loading = false; }
        _dirty = false;
    }

    public void Apply(AppSettings s)
    {
        s.EnableReminderSound = EnableReminderSoundCheck.IsChecked == true;
        s.ReminderSoundPath = ReminderSoundPathBox.Text ?? "";
        var boxes = ToggleCheckBoxes;
        if (boxes.Length == 8)
        {
            s.RemindClassStart = boxes[0].IsChecked == true;
            s.RemindClassMid = boxes[1].IsChecked == true;
            s.RemindNextClassSoon = boxes[2].IsChecked == true;
            s.RemindClassEndSoon10 = boxes[3].IsChecked == true;
            s.RemindClassEndSoon = boxes[4].IsChecked == true;
            s.RemindClassEnd = boxes[5].IsChecked == true;
            s.RemindDayEnd = boxes[6].IsChecked == true;
            s.RemindSpecialPeriod = boxes[7].IsChecked == true;
        }
        s.ReminderStyle = ReminderStyleToast.IsChecked == true ? 1 : 0;
    }
}
