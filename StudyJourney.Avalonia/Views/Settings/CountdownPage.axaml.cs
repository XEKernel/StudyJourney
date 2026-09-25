using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class CountdownPage : UserControl, ISettingsPage
{
    /// <summary>自定义倒计时编辑副本（#8：Load 复制、Apply 写回，未保存的增删/编辑不触碰 App.Settings，
    /// 与 ServerPage 的 _teachers 同款心智模型）</summary>
    private System.Collections.ObjectModel.ObservableCollection<CustomCountdown> _countdowns = new();

    /// <summary>#8：自定义倒计时是否有未保存修改（增删/改名/改日期置位；切页与关窗时提示）</summary>
    private bool _countdownDirty;

    /// <summary>时刻未填时的默认值（09:00 是绝大多数考试的开工时间）</summary>
    private static readonly TimeSpan DefaultTime = new(9, 0, 0);

    public CountdownPage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        // 字体族（系统字体）
        if (FontFamilyBox.Items.Count == 0)
        {
            foreach (var ff in FontManager.Current.SystemFonts)
                FontFamilyBox.Items.Add(ff.Name);
        }
        FontFamilyBox.SelectedItem = s.FontFamily;

        FontSizeSlider.Value = s.FontSize;
        FontSizeText.Text = ((int)s.FontSize).ToString();
        OpacitySlider.Value = s.OverallOpacity;
        OpacityText.Text = $"{s.OverallOpacity * 100:F0}%";
        ShowProgressBarCheck.IsChecked = s.ShowProgressBar;
        ShowProgressTextCheck.IsChecked = s.ShowProgressText;
        ShowDaysCheck.IsChecked = s.ShowDays;
        ShowHoursCheck.IsChecked = s.ShowHours;
        ShowMinutesCheck.IsChecked = s.ShowMinutes;
        ShowSecondsCheck.IsChecked = s.ShowSeconds;

        // 日期（2026-09-25：手打格式串 → 日期/时刻选择器）
        var gao = DateTimeStr.ParseDateTime(s.GaokaoDateStr);
        GaokaoDatePicker.SelectedDate = DateTimeStr.ToOffset(gao);
        GaokaoTimePicker.SelectedTime = gao != null
            ? new TimeSpan(gao.Value.Hour, gao.Value.Minute, 0)
            : DefaultTime;
        StartDatePicker.SelectedDate = DateTimeStr.ToOffset(DateTimeStr.ParseDate(s.StartDateStr));
        RefreshDateHint();

        // #8：倒计时列表复制到副本编辑（与字体/日期等控件一致：改完点保存才落盘）
        _countdowns = new System.Collections.ObjectModel.ObservableCollection<CustomCountdown>(
            (s.CustomCountdowns ?? new()).Select(c => new CustomCountdown { Name = c.Name, DateStr = c.DateStr }));
        BuildCountdownRows();
        _countdownDirty = false;

        // 颜色（2026-09-25：hex 输入框 → 可点击色板）
        TextColorSwatch.Value = s.TextColor.ToString();
        AccentColorSwatch.Value = s.AccentColor.ToString();
    }

    public void Apply(AppSettings s)
    {
        if (FontFamilyBox.SelectedItem is string ff && !string.IsNullOrWhiteSpace(ff))
            s.FontFamily = ff;

        s.FontSize = (int)FontSizeSlider.Value;
        s.OverallOpacity = OpacitySlider.Value;
        s.ShowProgressBar = ShowProgressBarCheck.IsChecked == true;
        s.ShowProgressText = ShowProgressTextCheck.IsChecked == true;
        s.ShowDays = ShowDaysCheck.IsChecked == true;
        s.ShowHours = ShowHoursCheck.IsChecked == true;
        s.ShowMinutes = ShowMinutesCheck.IsChecked == true;
        s.ShowSeconds = ShowSecondsCheck.IsChecked == true;

        // 日期：选择器保证值一定合法（旧版"手打 → 格式非法 → 弹框提示"那条路径已不可能发生）；
        // 秒沿用原值（老师改别的设置时不该顺手把 09:00:30 悄悄变成 09:00:00）。
        var gaoDate = DateTimeStr.ToDate(GaokaoDatePicker.SelectedDate);
        var gaoTime = GaokaoTimePicker.SelectedTime ?? DefaultTime;
        int sec = gaoDate != null
            ? DateTimeStr.PreserveSecond(s.GaokaoDateStr, gaoDate.Value, gaoTime)
            : 0;
        s.GaokaoDateStr = DateTimeStr.ComposeDateTime(gaoDate, gaoTime, DefaultTime, sec);
        s.StartDateStr = DateTimeStr.ComposeDate(DateTimeStr.ToDate(StartDatePicker.SelectedDate));

        // 颜色：色板值理论上一定合法；不合法说明配置文件被外部改坏了 → 保留原值并明确提示
        if (ColorSwatch.TryParse(TextColorSwatch.Value, out var tc)) s.TextColor = tc;
        else _ = App.ShowMessageAsync("倒计时", $"文字颜色无法识别：{TextColorSwatch.Value}\n已保留原值。");

        if (ColorSwatch.TryParse(AccentColorSwatch.Value, out var ac)) s.AccentColor = ac;
        else _ = App.ShowMessageAsync("倒计时", $"强调色无法识别：{AccentColorSwatch.Value}\n已保留原值。");

        // #8：倒计时副本写回设置
        s.CustomCountdowns = _countdowns.Select(c => new CustomCountdown { Name = c.Name, DateStr = c.DateStr }).ToList();
        _countdownDirty = false;
    }

    /// <summary>#8：切页/关窗提示依据 —— 自定义倒计时是否有未保存修改</summary>
    public bool IsDirty => _countdownDirty;

    // ── 滑条联动：数字随滑动实时更新 ────────────────────────
    private void FontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (FontSizeText != null) FontSizeText.Text = ((int)e.NewValue).ToString();
    }

    private void OpacitySlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (OpacityText != null) OpacityText.Text = $"{e.NewValue * 100:F0}%";
    }

    // ── 日期 / 时刻（2026-09-25）────────────────────────────
    private void GaokaoDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e) => RefreshDateHint();
    private void GaokaoTimeChanged(object? sender, TimePickerSelectedValueChangedEventArgs e) => RefreshDateHint();
    private void StartDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e) { }

    private void ClearGaokaoDate_Click(object? sender, RoutedEventArgs e)
    {
        GaokaoDatePicker.SelectedDate = null;
        RefreshDateHint();
    }

    private void ClearStartDate_Click(object? sender, RoutedEventArgs e)
    {
        StartDatePicker.SelectedDate = null;
    }

    /// <summary>把"现在到底存的是什么"直接写出来 —— 老师不用猜选择器里那几个框是干嘛的</summary>
    private void RefreshDateHint()
    {
        var d = DateTimeStr.ToDate(GaokaoDatePicker.SelectedDate);
        GaokaoDateHintTb.Text = d == null
            ? "未设置日期 → 主窗口不显示高考倒计时。"
            : $"当前设定：{DateTimeStr.ComposeDateTime(d, GaokaoTimePicker.SelectedTime ?? DefaultTime, DefaultTime)}";
    }

    // ── 自定义倒计时：行内编辑器（名称 + 日期选择器 + 删除）──
    // 2026-09-25：原 DataGrid 的「日期」列是纯文本单元格，要老师手打 yyyy-MM-dd；
    // 且 DataGrid 在触屏上要"双击进入编辑态"才好改，老师嫌麻烦 → 改为每行直接可编辑。
    private void AddCountdownBtn_Click(object? sender, RoutedEventArgs e)
    {
        _countdowns.Add(new CustomCountdown
        {
            Name = "新倒计时",
            DateStr = DateTime.Today.AddDays(30).ToString(DateTimeStr.DateFormat)
        });
        _countdownDirty = true;
        BuildCountdownRows();
    }

    private void BuildCountdownRows()
    {
        CountdownListPanel.Children.Clear();

        foreach (var c in _countdowns)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,184,40") };

            var nameBox = new TextBox
            {
                Text = c.Name,
                FontSize = 13,
                MinHeight = 34,
                PlaceholderText = "名称",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            nameBox.TextChanged += (_, _) => { c.Name = nameBox.Text ?? ""; _countdownDirty = true; };

            var datePicker = new DatePicker
            {
                SelectedDate = DateTimeStr.ToOffset(DateTimeStr.ParseDate(c.DateStr)),
                FontSize = 13,
                MinHeight = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            datePicker.SelectedDateChanged += (_, _) =>
            {
                c.DateStr = DateTimeStr.ComposeDate(DateTimeStr.ToDate(datePicker.SelectedDate));
                _countdownDirty = true;
            };

            var delBtn = new Button
            {
                Content = "✕",
                FontSize = 11,
                Padding = new Thickness(6, 0),
                MinHeight = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            delBtn.Click += (_, _) =>
            {
                _countdowns.Remove(c);
                _countdownDirty = true;
                // ⚠ 不能在按钮自己的 Click 里把**它所在的这一行**从视觉树摘掉（事件路由还会访问已脱离的控件）
                // → 推迟到下一个消息循环再重建列表。
                Dispatcher.UIThread.Post(BuildCountdownRows, DispatcherPriority.Background);
            };

            Grid.SetColumn(nameBox, 0);
            Grid.SetColumn(datePicker, 1);
            Grid.SetColumn(delBtn, 2);
            row.Children.Add(nameBox);
            row.Children.Add(datePicker);
            row.Children.Add(delBtn);
            CountdownListPanel.Children.Add(row);
        }

        CountdownEmptyTb.IsVisible = _countdowns.Count == 0;
    }
}
