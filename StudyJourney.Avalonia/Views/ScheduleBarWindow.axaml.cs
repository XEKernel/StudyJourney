using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 课表悬浮栏：屏幕顶部横幅，显示当前课/下一节课/进度/时间（对齐学程 WPF ScheduleBarWindow）。
/// 数据来自 App.Schedule（ScheduleManager），设置保存后自动刷新。
/// </summary>
public partial class ScheduleBarWindow : Window
{
    private DispatcherTimer? _timer;

    private static readonly IBrush BrOrange = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44));
    private static readonly IBrush BrRed    = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
    private static readonly IBrush BrGreen  = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush BrGray   = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

    public ScheduleBarWindow()
    {
        InitializeComponent();
        App.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => App.SettingsChanged -= OnSettingsChanged;
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        ApplySettings();
        PositionToTop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void OnSettingsChanged()
    {
        ApplySettings();
        PositionToTop();
    }

    private void ApplySettings()
    {
        Opacity = Math.Clamp(App.Settings.ScheduleBarOpacity, 0.1, 1.0);
        double fs = App.Settings.ScheduleBarFontSize;
        if (fs <= 0) fs = 14;
        CurrentTimeTb.FontSize = fs;
        DateTb.FontSize = fs * 0.65;
        StatusTb.FontSize = fs * 0.75;
        NextCountdownTb.FontSize = fs * 0.75;
        ProgressPctTb.FontSize = fs * 0.65;
    }

    /// <summary>贴屏幕顶部，宽度 = 工作区宽度</summary>
    private void PositionToTop()
    {
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(new PixelPoint(0, 0), new PixelSize(1920, 1080));
        Width = area.Width;
        Position = new PixelPoint(area.X, area.Y);
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var manager = App.Schedule;

        CurrentTimeTb.Text = now.ToString("HH:mm:ss");
        DateTb.Text = now.ToString("MM月dd日 ddd");

        var cur  = manager.GetCurrentEntry(now);
        var next = manager.GetNextEntry(now);
        var timeToNext = manager.GetTimeToNextEntry(now);

        if (cur != null)
        {
            StatusTb.Text = $"正在上课：{cur.Subject}";
            StatusTb.Foreground = BrGreen;

            var pct = manager.GetCurrentProgress(now);
            if (pct.HasValue)
            {
                ProgressBar.Value = pct.Value * 100;
                ProgressBar.IsVisible = true;
                ProgressPctTb.Text = $"{pct.Value * 100:F0}%";
            }
            else
            {
                ProgressBar.IsVisible = false;
                ProgressPctTb.Text = "";
            }

            var remain = manager.GetTimeToEndOfCurrent(now);
            NextCountdownTb.Text = remain.HasValue
                ? $"下课剩余 {remain.Value.Hours:D2}:{remain.Value.Minutes:D2}:{remain.Value.Seconds:D2}"
                : "";
            NextCountdownTb.Foreground = BrGreen;
        }
        else if (next != null)
        {
            StatusTb.Text = "课间休息";
            StatusTb.Foreground = BrOrange;
            ProgressBar.IsVisible = false;

            if (timeToNext.HasValue)
            {
                var ts = timeToNext.Value;
                NextCountdownTb.Text = $"距 {next.Subject} {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                NextCountdownTb.Foreground = BrOrange;
            }
            else
            {
                NextCountdownTb.Text = $"下一节：{next.Subject} {next.StartTimeStr}";
                NextCountdownTb.Foreground = BrOrange;
            }
            ProgressPctTb.Text = "";
        }
        else
        {
            StatusTb.Text = "今日课程已结束";
            StatusTb.Foreground = BrGray;
            NextCountdownTb.Text = "";
            ProgressBar.IsVisible = false;
            ProgressPctTb.Text = "";
        }
    }
}
