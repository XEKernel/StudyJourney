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

        // 按科目设置（v2.26.0）：两个下拉的选项是固定的，列表内容随数据重建
        PresencePresetCombo.ItemsSource = Helpers.SubjectPresentationRules.PresetNames;
        PresencePresetCombo.SelectedIndex = 0;
        RebuildPresenceList();

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

    // 按科目设置（v2.26.0）的编辑副本（与 App.Settings 隔离，点保存才写回）
    private readonly List<Models.SubjectPresentation> _presences = new();
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

    /// <summary>打开课表编辑窗口（自定义课表；2026-09-27 改为走全局单例入口）</summary>
    private void EditScheduleBtn_Click(object? sender, RoutedEventArgs e)
        => App.OpenScheduleEditorGlobal();

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

            // 按科目设置：从设置拷一份编辑副本（脱钩，点保存才写回）
            _presences.Clear();
            foreach (var p in s.SubjectPresentations ?? new List<Models.SubjectPresentation>())
                _presences.Add(new Models.SubjectPresentation
                {
                    Subject = p.Subject, MuteSound = p.MuteSound, HideWindow = p.HideWindow,
                });
            LoadPresenceSubjects();
            RebuildPresenceList();
            RefreshListeningUi();
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
        s.SubjectPresentations = _presences;   // v2.26.0 按科目设置
    }

    // ── 按科目设置（v2.26.0）────────────────────────────────

    /// <summary>科目下拉：取「课表里实际出现过的科目」∪「已配过的科目」——
    /// 老师不用手打科目名，也避免打错一个字就静默失效。课表为空时退化为只列已配项。</summary>
    private void LoadPresenceSubjects()
    {
        var names = new List<string>();
        try
        {
            var entries = App.Schedule.Data.Entries;
            if (entries != null)
                foreach (var e in entries)
                {
                    var sub = (e.Subject ?? "").Trim();
                    if (sub.Length > 0 && !names.Contains(sub)) names.Add(sub);
                }
        }
        catch { /* 课表未就绪 → 只列已配项 */ }

        foreach (var p in _presences)
        {
            var sub = (p.Subject ?? "").Trim();
            if (sub.Length > 0 && !names.Contains(sub)) names.Add(sub);
        }

        int keep = PresenceSubjectCombo.SelectedIndex;
        PresenceSubjectCombo.ItemsSource = names;
        PresenceSubjectCombo.SelectedIndex = keep >= 0 && keep < names.Count ? keep : (names.Count > 0 ? 0 : -1);
    }

    /// <summary>重建「已配科目」列表（每行：科目 + 档位 + 删除）</summary>
    private void RebuildPresenceList()
    {
        if (PresenceList == null) return;
        PresenceList.Children.Clear();

        foreach (var p in _presences.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };

            var name = new TextBlock
            {
                Text = p.Subject, FontSize = 12, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 0);

            var preset = new TextBlock
            {
                Text = Helpers.SubjectPresentationRules.PresetNames[
                    Helpers.SubjectPresentationRules.PresetIndexOf(p.MuteSound, p.HideWindow)],
                FontSize = 11, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Foreground = new global::Avalonia.Media.SolidColorBrush(
                    global::Avalonia.Media.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            };
            Grid.SetColumn(preset, 1);

            var del = new Button { Content = "✕", FontSize = 11, Padding = new global::Avalonia.Thickness(9, 2) };
            del.Click += (_, _) =>
            {
                _presences.Remove(p);
                RebuildPresenceList();
                MarkDirty();
            };
            Grid.SetColumn(del, 2);

            row.Children.Add(name);
            row.Children.Add(preset);
            row.Children.Add(del);
            PresenceList.Children.Add(row);
        }

        if (PresenceEmptyTb != null) PresenceEmptyTb.IsVisible = _presences.Count == 0;
    }

    // ══ 听力播放器（v2.27.0，规划 2.9）══════════════════════════════════
    // 与「调休」同一套路：改动**即时落盘**（存独立的 listening.json），不走 ISettingsPage 的
    // dirty/Apply 流程 —— 它不是 settings.json 的一部分，「恢复默认设置」也不会碰它。

    /// <summary>把听力数据回显到卡片（来源列表 / 例外日 / 自动关秒数 / 音量）</summary>
    private void RefreshListeningUi()
    {
        var svc = App.Listening;
        if (svc == null || ListeningSourceList == null) return;

        var data = svc.Data;

        // ① 来源列表
        ListeningSourceList.Children.Clear();
        foreach (var src in svc.Sources.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

            var info = new StackPanel { Spacing = 1 };
            bool isActive = string.Equals(src.Id, data.ActiveSourceId, StringComparison.Ordinal);
            info.Children.Add(new TextBlock
            {
                Text = (isActive ? "● " : "○ ") + src.Name + (src.Once ? "（临时）" : ""),
                FontSize = 12,
                FontWeight = isActive ? global::Avalonia.Media.FontWeight.SemiBold
                                      : global::Avalonia.Media.FontWeight.Normal,
            });
            info.Children.Add(new TextBlock
            {
                Text = src.Directory + "　" + ListeningProgressOf(src),
                FontSize = 10,
                Foreground = new global::Avalonia.Media.SolidColorBrush(
                    global::Avalonia.Media.Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)),
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            });
            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var useBtn = new Button
            {
                Content = isActive ? "当前" : "设为当前",
                FontSize = 11,
                Padding = new global::Avalonia.Thickness(9, 2),
                IsEnabled = !isActive,
            };
            useBtn.Click += (_, _) => { App.Listening?.SetActive(src.Id); RefreshListeningUi(); };
            Grid.SetColumn(useBtn, 1);
            row.Children.Add(useBtn);

            var delBtn = new Button { Content = "✕", FontSize = 11, Padding = new global::Avalonia.Thickness(9, 2) };
            delBtn.Click += (_, _) =>
            {
                App.Listening?.RemoveSource(src.Id);
                RefreshListeningUi();
            };
            Grid.SetColumn(delBtn, 2);
            row.Children.Add(delBtn);

            ListeningSourceList.Children.Add(row);
        }
        ListeningEmptyTb.IsVisible = svc.Sources.Count == 0;

        // ② 例外日（每个可单独删除）
        ListeningSkipList.Children.Clear();
        foreach (var d in (data.SkipDates ?? new List<string>()).OrderBy(x => x).ToList())
        {
            var b = new Button
            {
                Content = d + " ✕",
                FontSize = 10,
                Padding = new global::Avalonia.Thickness(7, 2),
                Margin = new global::Avalonia.Thickness(0, 0, 4, 4),
            };
            var key = d;
            b.Click += (_, _) =>
            {
                App.Listening?.Data.SkipDates.RemoveAll(x => string.Equals((x ?? "").Trim(), key, StringComparison.Ordinal));
                App.Listening?.Save();
                RefreshListeningUi();
            };
            ListeningSkipList.Children.Add(b);
        }

        // ③ 播放行为
        ListeningSkipIfExamCheck.IsChecked = data.SkipIfExam;
        ListeningAutoCloseBox.Value = Math.Clamp(data.AutoCloseSeconds, -1, 60);
        ListeningVolumeSlider.Value = Math.Clamp(data.Volume, 0, 100);
    }

    /// <summary>来源的进度文本（上次播到哪一份 / 是否听完）</summary>
    private static string ListeningProgressOf(Models.ListeningSource src)
    {
        var cands = Helpers.ListeningRules.CandidatesOf(src);
        if (cands.Count == 0) return "（目录里没有音频）";
        if (Helpers.ListeningRules.IsAtEnd(src, cands)) return $"已听完（{cands.Count} 份）";
        var last = (src.LastFile ?? "").Trim();
        if (last.Length == 0) return $"未开始（共 {cands.Count} 份）";
        var (idx, total) = Helpers.ListeningRules.PositionOf(last, cands);
        string name = System.IO.Path.GetFileName(last);
        return src.Finished
            ? $"上次听完 {idx}/{total}「{name}」→ 下次播下一份"
            : $"上次听到 {idx}/{total}「{name}」（未听完，下次续播）";
    }

    private async void ListeningBrowseDir_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // UserControl 上没有现成的 StorageProvider，要经 TopLevel 拿（且全名 Avalonia.Platform.* 会被相对解析）
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择听力录音所在的文件夹",
                AllowMultiple = false,
            });
            var dir = dirs?.Count > 0 ? dirs[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(dir)) ListeningDirBox.Text = dir;
        }
        catch (Exception ex) { Helpers.AppLogger.Error("听力：选目录失败", ex); }
    }

    private void ListeningAddSource_Click(object? sender, RoutedEventArgs e)
    {
        var dir = (ListeningDirBox.Text ?? "").Trim();
        if (dir.Length == 0 || !System.IO.Directory.Exists(dir))
        {
            ListeningHintTb.Text = "⚠ 先选一个存在的文件夹";
            return;
        }
        var name = (ListeningNameBox.Text ?? "").Trim();
        var src = App.Listening?.AddSource(name, dir, ListeningOnceCheck.IsChecked == true);
        if (src == null) { ListeningHintTb.Text = "⚠ 听力服务未启动"; return; }

        var cands = Helpers.ListeningRules.CandidatesOf(src);
        ListeningHintTb.Text = cands.Count == 0
            ? "已添加，但这个文件夹里没找到音频文件"
            : $"已添加：{cands.Count} 份录音";
        ListeningNameBox.Text = "";
        ListeningDirBox.Text = "";
        RefreshListeningUi();
    }

    private void ListeningAddSkipDate_Click(object? sender, RoutedEventArgs e)
    {
        var svc = App.Listening;
        if (svc == null) return;
        if (ListeningSkipDate.SelectedDate is not { } sel)
        {
            ListeningHintTb.Text = "⚠ 先选一个日期";
            return;
        }
        string key = sel.Date.ToString("yyyy-MM-dd");
        svc.Data.SkipDates ??= new List<string>();
        if (!svc.Data.SkipDates.Any(x => string.Equals((x ?? "").Trim(), key, StringComparison.Ordinal)))
        {
            svc.Data.SkipDates.Add(key);
            svc.Save();
            ListeningHintTb.Text = $"{key} 已加为例外日（那天不自动播、进度不动）";
        }
        RefreshListeningUi();
    }

    private void ListeningClearSkipDates_Click(object? sender, RoutedEventArgs e)
    {
        var svc = App.Listening;
        if (svc == null) return;
        svc.Data.SkipDates?.Clear();
        svc.Save();
        ListeningHintTb.Text = "已清空例外日";
        RefreshListeningUi();
    }

    private void ListeningAutoClose_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        var svc = App.Listening;
        if (svc == null) return;
        svc.Data.AutoCloseSeconds = (int)Math.Clamp(ListeningAutoCloseBox.Value ?? 3, -1, 60);
        svc.Save();
    }

    private void ListeningVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        var svc = App.Listening;
        if (svc == null) return;
        svc.Data.Volume = (int)Math.Clamp(ListeningVolumeSlider.Value, 0, 100);
        svc.Save();
    }

    private void ListeningPlayNow_Click(object? sender, RoutedEventArgs e)
        => ListeningPlayNow();

    private void ListeningOpenPlayer_Click(object? sender, RoutedEventArgs e)
        => App.OpenListeningPlayerGlobal();

    /// <summary>「现在听一次」：按当前活跃来源解析该播哪一份并直接播（等于手动触发一次，但不改任何日程）</summary>
    private void ListeningPlayNow()
    {
        var svc = App.Listening;
        if (svc == null) { ListeningHintTb.Text = "⚠ 听力服务未启动"; return; }

        var file = svc.ResolveAuto(DateTime.Now, out var reason);
        if (file == null)
        {
            ListeningHintTb.Text = "⚠ " + reason;
            return;
        }
        var src = svc.ActiveSource;
        Views.ListeningPlayerWindow.PlayFile(file, src?.Id ?? "");
        ListeningHintTb.Text = $"正在播：{System.IO.Path.GetFileName(file)}";
        RefreshListeningUi();
    }

    private void PresenceAddBtn_Click(object? sender, RoutedEventArgs e)
    {
        var subject = PresenceSubjectCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(subject))
        {
            PresenceEmptyTb.Text = "⚠ 课表里还没有科目可选 —— 先去「课表编辑」加几节课，或先用「＋ 新建规则」把课表填上";
            PresenceEmptyTb.IsVisible = true;
            return;
        }
        PresenceEmptyTb.Text = "（还没有按科目的设置）";

        bool changed = Helpers.SubjectPresentationRules.Upsert(
            _presences, subject, PresencePresetCombo.SelectedIndex);
        RebuildPresenceList();
        if (changed) MarkDirty();
    }
}
