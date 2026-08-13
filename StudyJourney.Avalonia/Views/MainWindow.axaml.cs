using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 灵动岛顶部状态栏（主窗口）：深色扁平 + 大圆角胶囊，整合三模块 ——
/// 模块一（时间/天气）、模块二（课程栏）、模块三（月考/口语/高考三个倒计时圆环）。
/// 保留桌面小组件行为：位置预设/点击穿透/上课隐藏/有窗口隐藏/自启动/托盘/快捷键。
/// </summary>
public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;
    private DispatcherTimer? _maximizeCheckTimer;
    private DispatcherTimer? _weatherTimer;
    private DispatcherTimer? _classEndRestoreTimer;
    private ExamModeWindow? _examModeWindow;
    private bool _draggable;

    // ── Win32：前台窗口 / 点击穿透 ─────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private static readonly IntPtr WS_EX_TRANSPARENT = new(0x20);
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    // ── 隐藏状态跟踪 ─────────────────────────────────────────
    private bool _hiddenByMaximize;
    private bool _hiddenByScheduleOrExam;
    private string? _cachedHideSubjects;
    private HashSet<string> _cachedHiddenSet = new(StringComparer.OrdinalIgnoreCase);

    // ── 点击穿透 / 定位 ──────────────────────────────────────
    private bool _clickThroughEnabled;
    private bool _isPositioning;

    // ── 关闭淡出 ─────────────────────────────────────────────
    private bool _isExiting;
    private bool _isClosing;

    // ── 自启动 ───────────────────────────────────────────────
    private bool _lastAutoStart;
    private const string AutoStartKeyName = "GaokaoCountdown";

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.AppIcon;

        App.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            App.SettingsChanged -= OnSettingsChanged;
            _maximizeCheckTimer?.Stop();
            _classEndRestoreTimer?.Stop();
            _weatherTimer?.Stop();
        };

        PositionChanged += Window_PositionChanged;
    }

    // ── App 调用的公开接口（快捷键/托盘）────────────────────
    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); ApplyWindowLayer(); }
    }

    public void EnterExamMode()
    {
        if (_examModeWindow != null) { _examModeWindow.Activate(); return; }

        var todayExams = App.Schedule.GetTodayExams();
        if (todayExams.Count == 0)
        {
            _ = App.ShowMessageAsync("考试模式", "今天没有安排考试。");
            return;
        }

        var now = Helpers.TimeSimulator.Now;
        if (App.Schedule.GetCurrentExamSubject(now) == null &&
            App.Schedule.GetNextExamSubject(now) == null)
        {
            _ = App.ShowMessageAsync("考试模式", "今天的考试已全部结束。");
            return;
        }

        _examModeWindow = new ExamModeWindow();
        _examModeWindow.Closed += (_, _) => _examModeWindow = null;
        _examModeWindow.Show();
    }

    public void ExitExamMode()
    {
        _examModeWindow?.Close();
        _examModeWindow = null;
    }

    // ── 初始化 ───────────────────────────────────────────────
    private void Window_Opened(object? sender, EventArgs e)
    {
        _lastAutoStart = GetAutoStartFromRegistry();
        App.Settings.AutoStart = _lastAutoStart;

        ApplySettings();
        PositionToPreset();
        ApplyClickThrough();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // 有窗口隐藏检测
        _maximizeCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _maximizeCheckTimer.Tick += (_, _) => MaximizeCheckTimer_Tick();
        _maximizeCheckTimer.Start();

        Tick();
        _ = LoadWeatherAsync();
        StartWeatherTimer();
    }

    // ── 桌面小组件：前台有窗口时隐藏（桌面同一层，不挡其他窗口）──
    private void MaximizeCheckTimer_Tick()
    {
        if (App.Settings.AlwaysOnTop) return;
        if (!App.Settings.HideWhenMaximized) return;

        var myHwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        IntPtr foreground = Helpers.WindowLayerHelper.ForegroundWindow;

        bool hasOtherWindow = foreground != IntPtr.Zero &&
                              foreground != myHwnd &&
                              !Helpers.WindowLayerHelper.IsDesktop(foreground);

        if (hasOtherWindow && IsVisible)
        {
            _hiddenByMaximize = true;
            Hide();
        }
        else if (!hasOtherWindow && _hiddenByMaximize)
        {
            _hiddenByMaximize = false;
            Show();
            ApplyWindowLayer();
        }
    }

    // ── 天气 ─────────────────────────────────────────────────
    private async Task LoadWeatherAsync()
    {
        try
        {
            var s = App.Settings;
            var result = await WeatherService.FetchAsync(s.WeatherCity, s.WeatherAdcode);
            if (result == null) return;
            WeatherTb.Text = $"{Helpers.ColorUtils.GetWeatherEmoji(result.WeatherIcon)} {result.Temperature}°";
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
        if (_lastAutoStart != App.Settings.AutoStart)
        {
            _lastAutoStart = App.Settings.AutoStart;
            ApplyAutoStart(_lastAutoStart);
        }

        ApplySettings();
        PositionToPreset();
        ApplyClickThrough();
        Tick();
    }

    private void ApplySettings()
    {
        var s = App.Settings;
        Opacity = Math.Clamp(s.OverallOpacity, 0.1, 1.0);

        _draggable = s.PositionPreset == PositionPresetValues.Custom;
    }

    /// <summary>每秒刷新：时间 / 课程 / 倒计时圆环；并处理上课隐藏</summary>
    private void Tick()
    {
        var now = Helpers.TimeSimulator.Now;
        var s = App.Settings;

        // 时间（模块一）
        TimeTb.Text = now.ToString("HH:mm:ss");

        // 上课/考试期间隐藏主窗口（可设置科目白名单）
        var curEntry = App.Schedule.GetCurrentEntry(now);
        bool isInClass = s.HideDuringClass && curEntry != null;
        if (isInClass && !string.IsNullOrWhiteSpace(s.HideSubjects))
        {
            if (s.HideSubjects != _cachedHideSubjects)
            {
                _cachedHideSubjects = s.HideSubjects;
                _cachedHiddenSet = s.HideSubjects
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            isInClass = curEntry != null && _cachedHiddenSet.Contains(curEntry.Subject);
        }
        bool isInExam = _examModeWindow != null;
        bool shouldHide = isInClass || isInExam;

        if (shouldHide)
        {
            _classEndRestoreTimer?.Stop();
            _classEndRestoreTimer = null;
            if (IsVisible)
            {
                _hiddenByScheduleOrExam = true;
                Hide();
            }
            return;
        }
        if (_hiddenByScheduleOrExam)
        {
            _hiddenByScheduleOrExam = false;
            if (_classEndRestoreTimer == null)
            {
                _classEndRestoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
                _classEndRestoreTimer.Tick += (_, _) =>
                {
                    _classEndRestoreTimer?.Stop();
                    _classEndRestoreTimer = null;
                    Show();
                    Tick();
                };
                _classEndRestoreTimer.Start();
            }
            return;
        }
        _classEndRestoreTimer?.Stop();
        _classEndRestoreTimer = null;

        if (_hiddenByMaximize) return;

        // 课程（模块二）
        UpdateScheduleInfo(now);

        // 倒计时圆环（模块三）
        UpdateCountdownRings(now);
    }

    /// <summary>模块二：课程栏（已上科目 | 当前状态 | 未来科目）</summary>
    private void UpdateScheduleInfo(DateTime now)
    {
        var manager = App.Schedule;
        var today = manager.GetTodayEntries(now.Date);

        // 左：已上科目
        var prevSubjects = today
            .Where(e => e.GetEndDateTime(now.Date) <= now)
            .OrderBy(e => e.EndTime)
            .Select(e => e.Subject);
        PrevSubjectsTb.Text = string.Join("  ", prevSubjects);

        // 中：当前状态 + 倒计时
        var cur = manager.GetCurrentEntry(now);
        var next = manager.GetNextEntry(now);
        if (cur != null)
        {
            var remain = manager.GetTimeToEndOfCurrent(now);
            StatusTb.Text = remain.HasValue
                ? $"{cur.Subject} {FormatDuration(remain.Value)}"
                : cur.Subject;
        }
        else if (next != null)
        {
            var timeToNext = manager.GetTimeToNextEntry(now);
            StatusTb.Text = timeToNext.HasValue
                ? $"课间休息 {FormatDuration(timeToNext.Value)}"
                : "课间休息";
        }
        else
        {
            StatusTb.Text = today.Count > 0 ? "今日课程已结束" : "今日无课";
        }

        // 右：未来科目
        var nextSubjects = today
            .Where(e => e.GetStartDateTime(now.Date) > now)
            .OrderBy(e => e.StartTime)
            .Select(e => e.Subject);
        NextSubjectsTb.Text = string.Join("  ", nextSubjects);

        // 底部浅蓝进度条：上课进度 / 课间休息进度
        UpdateClassProgress(now, cur, next);
    }

    /// <summary>课程栏底部进度条：上课进度 / 课间休息进度</summary>
    private void UpdateClassProgress(DateTime now, ScheduleEntry? cur, ScheduleEntry? next)
    {
        if (cur != null)
        {
            var pct = App.Schedule.GetCurrentProgress(now);
            ClassProgressBar.Value = pct.HasValue ? pct.Value * 100 : 0;
            return;
        }

        if (next != null)
        {
            var prev = App.Schedule.GetTodayEntries(now.Date)
                .Where(e => e.GetEndDateTime(now.Date) <= now)
                .OrderByDescending(e => e.EndTime)
                .FirstOrDefault();
            if (prev != null)
            {
                var breakStart = prev.GetEndDateTime(now.Date);
                var breakTotal = next.GetStartDateTime(now.Date) - breakStart;
                ClassProgressBar.Value = breakTotal.TotalSeconds > 0
                    ? Math.Clamp((now - breakStart).TotalSeconds / breakTotal.TotalSeconds, 0, 1) * 100
                    : 0;
                return;
            }
        }
        ClassProgressBar.Value = 0;
    }

    /// <summary>圆环配色（自定义倒计时轮换）</summary>
    private static readonly string[] RingColors = { "#FFEB3B", "#4CAF50", "#2B6CB0" };

    /// <summary>模块三：高考（固定）+ 自定义倒计时（动态）</summary>
    private void UpdateCountdownRings(DateTime now)
    {
        var s = App.Settings;

        // 高考（固定，暗蓝圆环）
        if (DateTime.TryParse(s.GaokaoDateStr, out var gao))
            UpdateRing(gao, s.StartDateStr, GaokaoTb, GaokaoRingArc, "高考");

        // 自定义倒计时（动态圆环）
        RebuildCustomRings(now);
    }

    private void UpdateRing(DateTime target, string? startStr, TextBlock tb, Arc arc, string label)
    {
        var now = Helpers.TimeSimulator.Now;
        var remaining = target - now;
        int days = remaining.TotalSeconds > 0 ? (int)Math.Ceiling(remaining.TotalDays) : 0;
        tb.Text = $"{label} {days}天";

        DateTime start;
        if (!string.IsNullOrEmpty(startStr) && DateTime.TryParse(startStr, out var sd)) start = sd;
        else start = target.AddDays(-100);

        double total = (target - start).TotalDays;
        double passed = (now - start).TotalDays;
        double progress = total > 0 ? Math.Clamp(passed / total, 0, 1) : 0;
        arc.SweepAngle = progress * 360;
    }

    /// <summary>重建自定义倒计时圆环（来自设置页「自定义倒计时」）</summary>
    private void RebuildCustomRings(DateTime now)
    {
        RingHost.Children.Clear();
        var list = App.Settings.CustomCountdowns;
        if (list == null || list.Count == 0) return;

        int idx = 0;
        foreach (var cc in list)
        {
            if (!DateTime.TryParse(cc.DateStr, out var target) || target <= now) continue;
            RingHost.Children.Add(BuildCustomRing(cc.Name, target, now, RingColors[idx % RingColors.Length]));
            idx++;
        }
    }

    /// <summary>构建单个自定义倒计时圆环（圆环 + 图标 + 剩余天数）</summary>
    private static StackPanel BuildCustomRing(string name, DateTime target, DateTime now, string colorHex)
    {
        var color = Color.Parse(colorHex);
        var progressBrush = new SolidColorBrush(color);
        int days = Math.Max(0, (int)Math.Ceiling((target - now).TotalDays));
        double progress = Math.Clamp((now - target.AddDays(-100)).TotalDays / 100.0, 0, 1);

        var bgArc = new Arc
        {
            StartAngle = 0, SweepAngle = 360,
            Stroke = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), StrokeThickness = 4
        };
        var progArc = new Arc
        {
            StartAngle = -90, SweepAngle = progress * 360,
            Stroke = progressBrush, StrokeThickness = 4, StrokeLineCap = PenLineCap.Round
        };
        var iconTb = new TextBlock
        {
            Text = "📅", FontSize = 15,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };

        var ring = new Grid { Width = 38, Height = 38 };
        ring.Children.Add(bgArc);
        ring.Children.Add(progArc);
        ring.Children.Add(iconTb);

        var textTb = new TextBlock
        {
            Text = $"{name} {days}天", FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };

        var panel = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(ring);
        panel.Children.Add(textTb);
        return panel;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h{ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m{ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    // ── 位置预设（0顶部/1中上/2居中/3中下/4底部/5自定义）──────
    private void PositionToPreset()
    {
        var s = App.Settings;
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(new PixelPoint(0, 0), new PixelSize(1920, 1080));
        double w = Bounds.Width > 0 ? Bounds.Width : 850;
        double h = Bounds.Height > 0 ? Bounds.Height : 100;

        double x, y;
        switch (s.PositionPreset)
        {
            case PositionPresetValues.Top:         x = (area.Width - w) / 2; y = 10; break;
            case PositionPresetValues.UpperCenter: x = (area.Width - w) / 2; y = area.Height / 25.0; break;
            case PositionPresetValues.Center:      x = (area.Width - w) / 2; y = (area.Height - h) / 2; break;
            case PositionPresetValues.LowerCenter: x = (area.Width - w) / 2; y = area.Height * 0.65; break;
            case PositionPresetValues.Bottom:      x = (area.Width - w) / 2; y = area.Height - h - 40; break;
            case PositionPresetValues.Custom:
                x = s.CustomPositionX < 0 ? (area.Width - w) / 2 : s.CustomPositionX;
                y = s.CustomPositionY < 0 ? area.Height / 25.0 : s.CustomPositionY;
                break;
            default: x = (area.Width - w) / 2; y = area.Height / 25.0; break;
        }

        _isPositioning = true;
        Position = new PixelPoint((int)x, (int)(y + s.PositionOffsetY));
        _isPositioning = false;
    }

    private void Window_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_isPositioning) return;
        if (App.Settings.PositionPreset != PositionPresetValues.Custom) return;
        App.Settings.CustomPositionX = e.Point.X;
        App.Settings.CustomPositionY = e.Point.Y;
    }

    // ── 点击穿透 ────────────────────────────────────────────
    private void ApplyClickThrough()
    {
        bool shouldEnable = App.Settings.PositionPreset != PositionPresetValues.Custom;
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

    private void ApplyWindowLayer()
    {
        Topmost = App.Settings.AlwaysOnTop;
        if (!App.Settings.AlwaysOnTop)
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ── 自启动（注册表 HKCU\Run）────────────────────────────
    private static bool GetAutoStartFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AutoStartKeyName) != null;
        }
        catch { return false; }
    }

    private static void ApplyAutoStart(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                key.SetValue(AutoStartKeyName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AutoStartKeyName, throwOnMissingValue: false);
            }
        }
        catch { /* 注册表写入失败静默处理 */ }
    }

    // ── 交互 ─────────────────────────────────────────────────
    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_draggable)
            {
                BeginMoveDrag(e);
                return;
            }
            if (e.ClickCount >= 2)
            {
                OpenSettings();
            }
        }
    }

    private SettingsWindow? _settingWindow;

    public void OpenSettings()
    {
        if (_settingWindow != null)
        {
            try { _settingWindow.Activate(); return; }
            catch { _settingWindow = null; }
        }
        _settingWindow = new SettingsWindow();
        _settingWindow.Closed += (_, _) => _settingWindow = null;
        if (IsVisible) _settingWindow.Show(this);
        else _settingWindow.Show();
    }

    // ── 关闭淡出 ─────────────────────────────────────────────
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_isExiting || _isClosing) return;
        e.Cancel = true;

        _isClosing = true;
        var start = DateTime.Now;
        double fromOpacity = Opacity;
        var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        fadeTimer.Tick += (_, _) =>
        {
            double t = (DateTime.Now - start).TotalMilliseconds / 300.0;
            Opacity = Math.Max(0, fromOpacity * (1 - t));
            if (t >= 1.0)
            {
                fadeTimer.Stop();
                _isExiting = true;
                _isClosing = false;
                Close();
            }
        };
        fadeTimer.Start();
    }

    // ── 右键菜单 ─────────────────────────────────────────────
    private void ExamModeMenuItem_Click(object? sender, RoutedEventArgs e) => EnterExamMode();

    private void OpenSettingsMenuItem_Click(object? sender, RoutedEventArgs e) => OpenSettings();

    private void ExitMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }
}
