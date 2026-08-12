using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 课表悬浮栏：屏幕顶部横幅，显示当前课/下一节课/进度/时间/天气（对齐学程 WPF ScheduleBarWindow）。
/// 含上课自动收缩为进度条（紧凑模式）、下课倒计时展开、点击穿透、多显示器支持。
/// </summary>
public partial class ScheduleBarWindow : Window
{
    private DispatcherTimer? _timer;
    private DispatcherTimer? _weatherTimer;
    private DispatcherTimer? _expandTimer;

    // ── 紧凑/展开状态 ────────────────────────────────────────
    private bool _isCompact;
    private bool _countdownExpanded;
    private string _lastStatusText = "";

    // ── 点击穿透 ─────────────────────────────────────────────
    private bool _clickThroughEnabled;

    // ── 颜色（对齐 WPF 版）───────────────────────────────────
    private static readonly IBrush BrOrange = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44));
    private static readonly IBrush BrRed    = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
    private static readonly IBrush BrGreen  = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush BrGray   = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

    // ── Win32：点击穿透 ──────────────────────────────────────
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private const int GWL_EXSTYLE = -20;
    private static readonly IntPtr WS_EX_TRANSPARENT = new(0x20);
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MB_ICONASTERISK = 0x40;

    public ScheduleBarWindow()
    {
        InitializeComponent();
        App.SettingsChanged += OnSettingsChanged;
        if (App.Reminders != null) App.Reminders.Countdown60Tick += OnCountdown60Tick;
        Closed += (_, _) =>
        {
            App.SettingsChanged -= OnSettingsChanged;
            if (App.Reminders != null) App.Reminders.Countdown60Tick -= OnCountdown60Tick;
            _weatherTimer?.Stop();
            _expandTimer?.Stop();
        };
    }

    /// <summary>60 秒下课倒计时：到设定秒数展开 + 提示音（对齐 WPF）</summary>
    private void OnCountdown60Tick(object? sender, int remaining)
    {
        if (remaining > 0)
        {
            Countdown60Tb.Text = $"⏰ 还有 {remaining}s 下课！";
            Countdown60Tb.IsVisible = true;

            int expandAt = App.Settings.CountdownExpandSeconds;
            if (expandAt <= 0 || expandAt > 60) expandAt = 30;
            if (remaining <= expandAt && _isCompact && !_countdownExpanded)
            {
                _countdownExpanded = true;
                SetExpanded();
                AutoCompactTimer(10);
                if (App.Settings.EnableCountdownSound)
                {
                    try { MessageBeep(MB_ICONASTERISK); } catch { }
                }
            }
        }
        else
        {
            _countdownExpanded = false;
            Countdown60Tb.IsVisible = false;
            // 倒计时结束：若在上课且开启自动收缩 → 收缩
            if (App.Settings.ScheduleBarAutoCollapse && App.Schedule.GetCurrentEntry(Helpers.TimeSimulator.Now) != null)
                SetCompact();
        }
    }

    /// <summary>提醒事件触发展开（MainWindow 订阅后调用）</summary>
    public void ExpandOnReminder(ReminderType type)
    {
        if (!_isCompact) return;

        bool shouldExpand = type switch
        {
            ReminderType.ClassEndSoon or ReminderType.ClassEnd or ReminderType.NextClassSoon or ReminderType.DayEnd => true,
            _ => false
        };
        if (!shouldExpand) return;

        // 下课延迟 2 分钟再展开（老师需要操作 PPT/关窗口）
        if (type == ReminderType.ClassEnd)
        {
            _expandTimer?.Stop();
            _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _expandTimer.Tick += (_, _) =>
            {
                _expandTimer?.Stop();
                _expandTimer = null;
                SetExpanded();
                AutoCompactTimer(10);
            };
            _expandTimer.Start();
            return;
        }

        SetExpanded();
        AutoCompactTimer(10);
    }

    private void AutoCompactTimer(int seconds)
    {
        _expandTimer?.Stop();
        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer?.Stop();
            _expandTimer = null;
            if (App.Schedule.GetCurrentEntry(Helpers.TimeSimulator.Now) != null && App.Settings.ScheduleBarAutoCollapse)
                SetCompact();
        };
        _expandTimer.Start();
    }

    /// <summary>手动展开按钮（紧凑模式下点击展开箭头）</summary>
    private void ExpandBtn_Click(object? sender, RoutedEventArgs e)
    {
        SetExpanded();
        AutoCompactTimer(15);
    }

    // ── 紧凑/展开切换（带动画）───────────────────────────────
    private void SetCompact()
    {
        if (_isCompact) return;
        _isCompact = true;
        FullInfoRoot.IsVisible = false;
        CompactRow.IsVisible = true;
        ExpandBtn.IsVisible = true;
        CompactRow.Opacity = 1;
        PositionToTop();
    }

    private void SetExpanded()
    {
        if (!_isCompact) return;
        _isCompact = false;
        CompactRow.IsVisible = false;
        FullInfoRoot.IsVisible = true;
        ExpandBtn.IsVisible = false;
        FullInfoRoot.Opacity = 1;
        PositionToTop();
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        ApplySettings();
        PositionToTop();
        ApplyClickThrough();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();

        _ = LoadWeatherAsync();
        StartWeatherTimer();
    }

    // ── 天气（WeatherService + 定时刷新 + 完整字段）───────────
    private async Task LoadWeatherAsync()
    {
        try
        {
            var s = App.Settings;
            var result = await WeatherService.FetchAsync(s.WeatherCity, s.WeatherAdcode);
            if (result == null) return;

            WeatherIconTb.Text = ColorUtils.GetWeatherEmoji(result.WeatherIcon);
            WeatherTb.Text = result.Weather;
            WeatherWindTb.Text = string.IsNullOrEmpty(result.WindDirection) ? "" : $"{result.WindDirection}风 {result.WindPower}级";
            WeatherHumidityTb.Text = result.Humidity > 0 ? $"湿度 {result.Humidity}%" : "";
            WeatherTempTb.Text = $"{result.Temperature}°";
            WeatherCityTb.Text = result.Location;
            WeatherRow.IsVisible = true;

            // 字号与颜色（对齐 WPF：描述/风/湿度用 Info 色）
            double fs = s.WeatherFontSize;
            if (fs <= 0) fs = 14;
            WeatherIconTb.FontSize = fs * 0.86;
            WeatherTempTb.FontSize = fs * 0.8;
            WeatherCityTb.FontSize = fs * 0.72;
            WeatherTb.FontSize = fs * 0.72;
            WeatherWindTb.FontSize = fs * 0.65;
            WeatherHumidityTb.FontSize = fs * 0.65;

            WeatherTempTb.Foreground = ColorUtils.ParseBrush(s.WeatherTempColor, "#FFFF8844");
            WeatherCityTb.Foreground = ColorUtils.ParseBrush(s.WeatherCityColor, "#FFFFFFFF");
            WeatherTb.Foreground = ColorUtils.ParseBrush(s.WeatherInfoColor, "#FFCCCCDD");
            WeatherWindTb.Foreground = ColorUtils.ParseBrush(s.WeatherInfoColor, "#FFCCCCDD");
            WeatherHumidityTb.Foreground = ColorUtils.ParseBrush(s.WeatherInfoColor, "#FFCCCCDD");
            WeatherIconTb.Foreground = ColorUtils.ParseBrush(s.WeatherIconColor, "#FFFFAA00");
        }
        catch { /* 网络异常静默 */ }
    }

    private void StartWeatherTimer()
    {
        _weatherTimer?.Stop();
        int min = App.Settings.WeatherRefreshInterval;
        if (min <= 0) return;
        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(min) };
        _weatherTimer.Tick += async (_, _) => await LoadWeatherAsync();
        _weatherTimer.Start();
    }

    private void OnSettingsChanged()
    {
        ApplySettings();
        PositionToTop();
        ApplyClickThrough();
    }

    private void ApplySettings()
    {
        Opacity = Math.Clamp(App.Settings.ScheduleBarOpacity, 0.1, 1.0);
        Topmost = App.Settings.ScheduleBarAlwaysOnTop;
        double fs = App.Settings.ScheduleBarFontSize;
        if (fs <= 0) fs = 14;
        CurrentTimeTb.FontSize = fs;
        DateTb.FontSize = fs * 0.65;
        StatusTb.FontSize = fs * 0.75;
        NextCountdownTb.FontSize = fs * 0.75;
        ProgressPctTb.FontSize = fs * 0.65;
        Countdown60Tb.FontSize = fs * 0.8;
        CompactStatusTb.FontSize = fs * 0.7;
    }

    /// <summary>贴所在屏幕顶部，宽度 = 所在显示器宽度（多显示器支持）</summary>
    private void PositionToTop()
    {
        // Avalonia 12：Screens 是实例类，ScreenFromWindow 取窗口所在显示器
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(new PixelPoint(0, 0), new PixelSize(1920, 1080));

        double w = App.Settings.ScheduleBarWidth > 0 ? App.Settings.ScheduleBarWidth : area.Width;
        Width = w;
        Position = new PixelPoint(area.X + (int)((area.Width - w) / 2), area.Y);
    }

    /// <summary>点击穿透（设置可开关）</summary>
    private void ApplyClickThrough()
    {
        bool shouldEnable = App.Settings.ScheduleBarClickThrough;
        if (_clickThroughEnabled == shouldEnable) return;
        _clickThroughEnabled = shouldEnable;

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if (shouldEnable) exStyle |= WS_EX_TRANSPARENT;
        else exStyle &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Refresh()
    {
        var now = Helpers.TimeSimulator.Now;
        var manager = App.Schedule;

        CurrentTimeTb.Text = now.ToString("HH:mm:ss");
        DateTb.Text = now.ToString("MM月dd日 ddd");

        var cur = manager.GetCurrentEntry(now);
        var next = manager.GetNextEntry(now);
        var timeToNext = manager.GetTimeToNextEntry(now);

        // 今日课程（用于节次计数与课后列表）
        var todayEntries = manager.GetTodayEntries(now.Date);
        int total = todayEntries.Count;
        int curIndex = cur != null ? todayEntries.FindIndex(e => e.Period == cur.Period) + 1 : -1;
        bool isLast = cur != null && next == null;

        if (cur != null)
        {
            StatusTb.Text = isLast ? $"最后一节课：{cur.Subject}" : $"正在上课：{cur.Subject}";
            StatusTb.Foreground = BrGreen;

            var pct = manager.GetCurrentProgress(now);
            if (pct.HasValue)
            {
                ProgressBar.Value = pct.Value * 100;
                ProgressBar.IsVisible = true;
                CompactProgressBar.Value = pct.Value * 100;
                CompactProgressBar.IsVisible = true;
                ProgressPctTb.Text = total > 0 && curIndex > 0
                    ? $"{pct.Value * 100:F0}% · 第{curIndex}/{total}节"
                    : $"{pct.Value * 100:F0}%";
                CompactStatusTb.Text = $"正在上课：{cur.Subject} {pct.Value * 100:F0}%";
            }
            else
            {
                ProgressBar.IsVisible = false;
                CompactProgressBar.IsVisible = false;
                ProgressPctTb.Text = "";
                CompactStatusTb.Text = $"正在上课：{cur.Subject}";
            }

            var remain = manager.GetTimeToEndOfCurrent(now);
            NextCountdownTb.Text = remain.HasValue
                ? (isLast
                    ? $"最后一节 · 下课剩余 {remain.Value.Hours:D2}:{remain.Value.Minutes:D2}:{remain.Value.Seconds:D2}"
                    : $"下课剩余 {remain.Value.Hours:D2}:{remain.Value.Minutes:D2}:{remain.Value.Seconds:D2}")
                : "";
            NextCountdownTb.Foreground = BrGreen;

            // 自动收缩：上课 → 紧凑模式（对齐 WPF ScheduleBarAutoCollapse）
            if (App.Settings.ScheduleBarAutoCollapse && !_isCompact && _expandTimer == null && !Countdown60Tb.IsVisible)
                SetCompact();
        }
        else if (next != null)
        {
            StatusTb.Text = "课间休息";
            StatusTb.Foreground = BrOrange;
            ProgressBar.IsVisible = false;
            CompactProgressBar.IsVisible = false;

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

            // 下课 → 展开（对齐 WPF）
            if (_isCompact) SetExpanded();
        }
        else
        {
            // 今日课程已结束：显示今天上过的课程列表（用户需求）
            StatusTb.Text = total > 0 ? "今日课程已结束" : "今日无课";
            StatusTb.Foreground = BrGray;
            NextCountdownTb.Text = total > 0 ? BuildTodayList(todayEntries) : "";
            NextCountdownTb.Foreground = BrGray;
            ProgressBar.IsVisible = false;
            CompactProgressBar.IsVisible = false;
            ProgressPctTb.Text = total > 0 ? $"共 {total} 节" : "";
            CompactStatusTb.Text = total > 0 ? $"今日课程已结束（{total} 节）" : "今日无课";
            if (_isCompact) SetExpanded();
        }

        // 状态文本变化 → 脉冲（对齐 WPF PulseOpacity）
        if (StatusTb.Text != _lastStatusText)
        {
            _lastStatusText = StatusTb.Text;
            PulseOpacity(StatusTb);
        }
    }

    /// <summary>今日课程列表文本（"08:00 语文 · 08:55 数学 …"）</summary>
    private static string BuildTodayList(System.Collections.Generic.List<ScheduleEntry> entries)
        => string.Join("  ", entries.Select(e =>
        {
            var s = e.StartTimeStr;
            return string.IsNullOrWhiteSpace(e.Subject) ? s : $"{s} {e.Subject}";
        }));

    /// <summary>状态文本快速脉冲动画（透明度 1→0.3→1）</summary>
    private static void PulseOpacity(TextBlock element)
    {
        element.Opacity = 1;
        var start = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.Now - start).TotalMilliseconds;
            if (elapsed >= 230)
            {
                timer.Stop();
                element.Opacity = 1;
                return;
            }
            // 80ms 降到 0.3，再 150ms 回到 1
            element.Opacity = elapsed < 80
                ? 1 - 0.7 * (elapsed / 80)
                : 0.3 + 0.7 * ((elapsed - 80) / 150);
        };
        timer.Start();
    }
}
