using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 考试模式全屏倒计时（对齐学程 WPF ExamModeWindow）：
/// 数据来自 App.Schedule；蜂鸣/警告/自动退出副作用在 View。
/// </summary>
public partial class ExamModeWindow : Window
{
    // 系统蜂鸣（WPF SystemSounds.Beep 底层即 user32.MessageBeep，Avalonia 无 System.Windows.Extensions）
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
    private const uint MB_ICONASTERISK = 0x40;

    private DispatcherTimer? _timer;
    private string _currentSubjectName = string.Empty;
    private bool _warnShown;
    private bool _autoExited;
    private int _lastBeepSecond = -1;

    public ExamModeWindow()
    {
        InitializeComponent();
        ApplyStyles();
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();

        _ = LoadWeatherAsync();
    }

    // ── 天气（下一场下方）────────────────────────────────────
    private async System.Threading.Tasks.Task LoadWeatherAsync()
    {
        try
        {
            var s = App.Settings;
            var result = await Services.WeatherService.FetchAsync(s.WeatherCity, s.WeatherAdcode);
            if (result == null) return;

            WeatherIconTb.Text = Helpers.ColorUtils.GetWeatherEmoji(result.WeatherIcon);
            WeatherCityTb.Text = result.Location;
            WeatherTb.Text = result.Weather;
            WeatherTempTb.Text = $"{result.Temperature}°";
            WeatherRow.IsVisible = true;
        }
        catch { }
    }

    private void ApplyStyles()
    {
        var s = App.Settings;
        SubjectTb.FontSize = s.ExamSubjectFontSize;
        ExamNameTb.FontSize = s.ExamNameFontSize;
        CountdownTb.FontSize = s.ExamCountdownFontSize;
        StartTimeTb.FontSize = s.ExamTimeInfoFontSize;
        EndTimeTb.FontSize = s.ExamTimeInfoFontSize;
        DurationTb.FontSize = s.ExamTimeInfoFontSize;
        NextSubjectTb.FontSize = s.ExamNextSubjectFontSize;
        WarningTb.FontSize = s.ExamWarningFontSize;
        EscHintTb.FontSize = s.ExamEscHintFontSize;
        ProgressBar.Height = s.ExamProgressBarHeight;

        SubjectTb.Foreground = ColorUtils.ParseBrush(s.ExamSubjectColor, "#FFFFFFFF");
        ExamNameTb.Foreground = ColorUtils.ParseBrush(s.ExamNameColor, "#AAFFFFFF");
        NextSubjectTb.Foreground = ColorUtils.ParseBrush(s.ExamNextSubjectColor, "#88FFFFFF");
        WarningTb.Foreground = ColorUtils.ParseBrush(s.ExamWarningColor, "#FFCC8800");
        ProgressBar.Foreground = ColorUtils.ParseBrush(s.ExamProgressBarColor, "#5B9BD5");
        ProgressBar.Background = ColorUtils.ParseBrush(s.ExamProgressBarBgColor, "#22FFFFFF");
        try { Background = new SolidColorBrush(Color.Parse(s.ExamBackgroundColor)); } catch { }
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        CurrentTimeTb.Text = now.ToString("HH:mm:ss");

        var cur = App.Schedule.GetCurrentExamSubject(now);
        if (cur.HasValue)
        {
            var (exam, subject) = cur.Value;
            ExamNameTb.Text = exam.Name;
            SubjectTb.Text = subject.Name;
            StartTimeTb.Text = subject.StartTimeStr;
            EndTimeTb.Text = subject.EndTimeStr;
            DurationTb.Text = $"共 {subject.Duration.TotalMinutes:F0} 分钟";

            var endDt = now.Date + subject.EndTime;
            var remaining = endDt - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            CountdownTb.Text = remaining.ToString(@"hh\:mm\:ss");
            CountdownTb.Foreground = remaining.TotalMinutes <= 5
                ? ColorUtils.ParseBrush(App.Settings.ExamCountdownCriticalColor, "#FFFF4444")
                : remaining.TotalMinutes <= 15
                    ? ColorUtils.ParseBrush(App.Settings.ExamCountdownWarningColor, "#FFCC8800")
                    : ColorUtils.ParseBrush(App.Settings.ExamCountdownNormalColor, "#FFFFFFFF");

            var elapsed = now - (now.Date + subject.StartTime);
            double pct = subject.Duration.TotalSeconds > 0
                         ? Math.Clamp(elapsed.TotalSeconds / subject.Duration.TotalSeconds, 0, 1)
                         : 0;
            ProgressBar.Value = pct * 100;
            ProgressPctTb.Text = $"{pct * 100:F1}% 已完成";

            var next = App.Schedule.GetNextExamSubject(now);
            NextSubjectTb.Text = next.HasValue
                ? $"下一场：{next.Value.Item2.Name}  {next.Value.Item2.StartTimeStr}"
                : "";

            // 15 分钟警告（一次）
            if (remaining.TotalSeconds > 0 && remaining.TotalMinutes <= 15 && !_warnShown)
            {
                _warnShown = true;
                WarningTb.IsVisible = true;
                MessageBeep(MB_ICONASTERISK);
            }
            // 5 分钟每秒蜂鸣
            if (remaining.TotalSeconds > 0 && remaining.TotalMinutes <= 5)
            {
                int sec = (int)remaining.TotalSeconds;
                if (sec != _lastBeepSecond)
                {
                    _lastBeepSecond = sec;
                    MessageBeep(MB_ICONASTERISK);
                }
            }
            // 科目切换重置
            if (subject.Name != _currentSubjectName)
            {
                _currentSubjectName = subject.Name;
                _warnShown = false;
                _lastBeepSecond = -1;
                WarningTb.IsVisible = false;
            }
        }
        else
        {
            var next = App.Schedule.GetNextExamSubject(now);
            if (next.HasValue)
            {
                var (exam, subject) = next.Value;
                ExamNameTb.Text = exam.Name;
                SubjectTb.Text = subject.Name;
                var startDt = now.Date + subject.StartTime;
                var remaining = startDt - now;
                CountdownTb.Text = remaining > TimeSpan.Zero ? $"距开考 {remaining:hh\\:mm\\:ss}" : "--:--";
                CountdownTb.Foreground = ColorUtils.ParseBrush(App.Settings.ExamDistanceColor, "#AAFFFFFF");
                ProgressBar.Value = 0;
                ProgressPctTb.Text = "";
                StartTimeTb.Text = subject.StartTimeStr;
                EndTimeTb.Text = subject.EndTimeStr;
                DurationTb.Text = $"共 {subject.Duration.TotalMinutes:F0} 分钟";
                NextSubjectTb.Text = "";
                WarningTb.IsVisible = false;
            }
            else
            {
                ExamNameTb.Text = "今日考试";
                SubjectTb.Text = "考试已结束";
                CountdownTb.Text = "00:00";
                CountdownTb.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ProgressBar.Value = 100;
                ProgressPctTb.Text = "100%";
                NextSubjectTb.Text = "";
                WarningTb.IsVisible = false;

                // 最后一场结束 → 3 秒后自动退出
                if (!_autoExited)
                {
                    _autoExited = true;
                    var autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    autoClose.Tick += (_, _) => { autoClose.Stop(); if (IsLoaded) Close(); };
                    autoClose.Start();
                }
            }
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_currentSubjectName))
            {
                // 简单确认：ESC 双击退出（第一次提示）
                if (!_escPressed)
                {
                    _escPressed = true;
                    EscHintTb.Text = "再按一次 ESC 退出考试模式";
                    return;
                }
            }
            Close();
        }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
    private bool _escPressed;

    private void ExitBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
