using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 桌面小组件主窗口（对齐学程 WPF 主窗口）：
/// 无边框圆角卡片 + 倒计时 + 进度 + 自定义倒计时 + 每日一言；
/// 位置预设/透明度/字号/颜色/显示单位全部从 App.Settings 读取，设置保存后自动刷新。
/// 含点击穿透 / 上课隐藏 / 最大化隐藏 / 自启动 / 入场与脉冲动画（对齐 WPF 版）。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private DispatcherTimer? _timer;
    private DispatcherTimer? _quoteTimer;
    private DispatcherTimer? _maximizeCheckTimer;
    private DispatcherTimer? _classEndRestoreTimer;
    private ScheduleBarWindow? _scheduleBar;
    private ExamModeWindow? _examModeWindow;
    private DateTime _gaokaoDate = new(2027, 6, 7, 9, 0, 0);
    private DateTime _startDate = new(2024, 8, 24);
    private bool _draggable;   // 自定义位置模式可拖动

    // ── Win32：最大化检测 / 点击穿透 ─────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern int GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public int ptMinPositionX;
        public int ptMinPositionY;
        public int ptMaxPositionX;
        public int ptMaxPositionY;
        public int rcNormalLeft;
        public int rcNormalTop;
        public int rcNormalRight;
        public int rcNormalBottom;
    }
    private const int SW_SHOWMAXIMIZED = 3;
    private const int GWL_EXSTYLE = -20;
    private static readonly IntPtr WS_EX_TRANSPARENT = new(0x20);
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    // ── 隐藏状态跟踪 ─────────────────────────────────────────
    private bool _hiddenByMaximize;
    private bool _hiddenByScheduleOrExam;
    private string? _cachedHideSubjects;
    private HashSet<string> _cachedHiddenSet = new(StringComparer.OrdinalIgnoreCase);

    // ── 点击穿透状态 ─────────────────────────────────────────
    private bool _clickThroughEnabled;
    private bool _isPositioning;

    // ── 入场 / 脉冲动画 ──────────────────────────────────────
    private DispatcherTimer? _introTimer;
    private DateTime _introStart;
    private const double IntroDurationMs = 1250.0;
    private int _introDays, _introHours, _introMinutes, _introSeconds;
    private double _introProgress;
    private int _lastDays = -1, _lastHours = -1, _lastMinutes = -1, _lastSeconds = -1;

    // ── 关闭淡出 ─────────────────────────────────────────────
    private bool _isExiting;
    private bool _isClosing;

    // ── 自启动 ───────────────────────────────────────────────
    private bool _lastAutoStart;
    private const string AutoStartKeyName = "GaokaoCountdown";

    // ── 基准尺寸（字号缩放用，SizeToContent 下仅作参考）───────
    private const int BaseFontSize = 40;

    // ── 缓存画刷避免每 tick 新建 ─────────────────────────────
    private SolidColorBrush _textBrushCache = new(Colors.White);
    private SolidColorBrush _numberBrushCache = new(Colors.Red);
    private SolidColorBrush _progressBrushCache = new(Colors.White);

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.AppIcon;
        RefreshDates();

        // 设置变更 → 立即应用（设置窗口保存后触发）
        App.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            App.SettingsChanged -= OnSettingsChanged;
            _maximizeCheckTimer?.Stop();
            _classEndRestoreTimer?.Stop();
        };

        // 自定义模式拖动后回写坐标
        PositionChanged += Window_PositionChanged;
    }

    // ── App 调用的公开接口（快捷键/托盘）────────────────────
    public bool IsScheduleBarVisible => _scheduleBar != null;

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); ApplyWindowLayer(); if (App.Settings.EnableAnimations) PlayIntroAnimation(); }
    }

    public void ToggleScheduleBarViaHotkey()
    {
        if (_scheduleBar != null)
        {
            _scheduleBar.Close();
            _scheduleBar = null;
        }
        else ShowScheduleBar();
    }

    public void EnterExamMode()
    {
        if (_examModeWindow != null) { _examModeWindow.Activate(); return; }
        if (App.Schedule.GetTodayExams().Count == 0) return;

        // 进入考试模式时隐藏课表栏
        if (App.Settings.ShowScheduleBar)
            HideScheduleBar();

        _examModeWindow = new ExamModeWindow();
        _examModeWindow.Closed += (_, _) =>
        {
            _examModeWindow = null;
            if (App.Settings.ShowScheduleBar && _scheduleBar == null) ShowScheduleBar();
        };
        _examModeWindow.Show();
    }

    // ── 初始化 ───────────────────────────────────────────────
    private void Window_Opened(object? sender, EventArgs e)
    {
        // 启动时以注册表实际状态同步设置
        _lastAutoStart = GetAutoStartFromRegistry();
        App.Settings.AutoStart = _lastAutoStart;

        ApplySettings();
        PositionToPreset();
        ApplyClickThrough();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // 最大化检测定时器
        _maximizeCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _maximizeCheckTimer.Tick += (_, _) => MaximizeCheckTimer_Tick();
        _maximizeCheckTimer.Start();

        Tick();
        if (App.Settings.EnableAnimations) PlayIntroAnimation();

        // 每日一言：启动加载 + 定时刷新
        if (App.Settings.ShowDailyQuote)
        {
            _ = LoadQuoteAsync();
            StartQuoteTimer();
        }
    }

    // ── 最大化检测：前台窗口最大化时隐藏 ─────────────────────
    private void MaximizeCheckTimer_Tick()
    {
        if (!App.Settings.HideWhenMaximized) return;

        IntPtr foreground = GetForegroundWindow();
        var myHwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (foreground == myHwnd || foreground == IntPtr.Zero) return;

        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(foreground, ref placement);
        bool isForegroundMaximized = placement.showCmd == SW_SHOWMAXIMIZED;

        if (isForegroundMaximized && IsVisible)
        {
            _hiddenByMaximize = true;
            Hide();
        }
        else if (!isForegroundMaximized && _hiddenByMaximize)
        {
            _hiddenByMaximize = false;
            Show();
            ApplyWindowLayer();
            if (App.Settings.EnableAnimations) PlayIntroAnimation();
        }
    }

    // ── 每日一言（HTTP + 定时刷新）───────────────────────────
    private async Task LoadQuoteAsync()
    {
        try
        {
            var s = App.Settings;
            string url = string.IsNullOrWhiteSpace(s.QuoteApiUrl)
                ? "https://uapis.cn/api/v1/saying" : s.QuoteApiUrl;
            string json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string field = string.IsNullOrWhiteSpace(s.QuoteTextFieldName) ? "text" : s.QuoteTextFieldName.Trim();
            if (!root.TryGetProperty(field, out var prop) || prop.ValueKind != JsonValueKind.String) return;
            string? text = prop.GetString();
            if (string.IsNullOrWhiteSpace(text)) return;

            DailyQuoteTb.Text = $"「{text.Trim()}」";
            DailyQuoteTb.IsVisible = true;
            DailyQuoteTb.FontSize = Math.Max(10, s.QuoteFontSize);
            DailyQuoteTb.FontStyle = s.QuoteItalic ? FontStyle.Italic : FontStyle.Normal;
            try { DailyQuoteTb.Foreground = new SolidColorBrush(Color.Parse(s.QuoteForegroundHex)); } catch { }
        }
        catch { /* 网络异常静默 */ }
    }

    private void StartQuoteTimer()
    {
        _quoteTimer?.Stop();
        int sec = App.Settings.QuoteAutoRefreshInterval;
        if (sec <= 0) return;
        _quoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        _quoteTimer.Tick += async (_, _) => await LoadQuoteAsync();
        _quoteTimer.Start();
    }

    private void DailyQuoteTb_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 单击一言 → 刷新（与 WPF 版点击刷新一致）
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
            _ = LoadQuoteAsync();
    }

    private void OnSettingsChanged()
    {
        // 自启动开关变化 → 写注册表
        if (_lastAutoStart != App.Settings.AutoStart)
        {
            _lastAutoStart = App.Settings.AutoStart;
            ApplyAutoStart(_lastAutoStart);
        }

        RefreshDates();
        ApplySettings();
        PositionToPreset();
        ApplyClickThrough();
        Tick();
    }

    private void RefreshDates()
    {
        var s = App.Settings;
        if (DateTime.TryParse(s.GaokaoDateStr, out var g)) _gaokaoDate = g;
        if (DateTime.TryParse(s.StartDateStr, out var d)) _startDate = d;
    }

    /// <summary>应用静态样式（透明度/字号/颜色/置顶/显示单位/字体/发光/窗口缩放）</summary>
    private void ApplySettings()
    {
        var s = App.Settings;
        Opacity = Math.Clamp(s.OverallOpacity, 0.1, 1.0);
        Topmost = s.AlwaysOnTop;

        double fs = s.FontSize;
        if (fs <= 0) fs = 40;
        double enSize = fs * 0.4;

        // 画刷缓存（颜色变更时重建）
        if (_textBrushCache.Color != s.TextColor) _textBrushCache = new SolidColorBrush(s.TextColor);
        if (_numberBrushCache.Color != s.NumberColor) _numberBrushCache = new SolidColorBrush(s.NumberColor);
        if (_progressBrushCache.Color != s.ProgressBarColor) _progressBrushCache = new SolidColorBrush(s.ProgressBarColor);

        // 字体族（应用到全部文本块）
        FontFamily ff = FontFamily.Default;
        try { if (!string.IsNullOrWhiteSpace(s.FontFamily)) ff = new FontFamily(s.FontFamily); } catch { }

        ApplyTextBlockStyle(ChinesePrefixTb, fs, _textBrushCache, ff);
        ApplyTextBlockStyle(ChineseDaysTb, fs, _textBrushCache, ff);
        ApplyTextBlockStyle(ChineseHoursTb, fs, _textBrushCache, ff);
        ApplyTextBlockStyle(ChineseMinutesTb, fs, _textBrushCache, ff);
        ApplyTextBlockStyle(ChineseSecondsTb, fs, _textBrushCache, ff);
        ApplyTextBlockStyle(DaysTb, fs, _numberBrushCache, ff);
        ApplyTextBlockStyle(HoursTb, fs, _numberBrushCache, ff);
        ApplyTextBlockStyle(MinutesTb, fs, _numberBrushCache, ff);
        ApplyTextBlockStyle(SecondsTb, fs, _numberBrushCache, ff);

        ApplyTextBlockStyle(EnglishPrefixTb, enSize, _textBrushCache, ff);
        ApplyTextBlockStyle(EnglishDaysTb, enSize, _textBrushCache, ff);
        ApplyTextBlockStyle(EnglishHoursTb, enSize, _textBrushCache, ff);
        ApplyTextBlockStyle(EnglishMinutesTb, enSize, _textBrushCache, ff);
        ApplyTextBlockStyle(EnglishSecondsTb, enSize, _textBrushCache, ff);
        ApplyTextBlockStyle(DaysEnTb, enSize, _numberBrushCache, ff);
        ApplyTextBlockStyle(HoursEnTb, enSize, _numberBrushCache, ff);
        ApplyTextBlockStyle(MinutesEnTb, enSize, _numberBrushCache, ff);
        ApplyTextBlockStyle(SecondsEnTb, enSize, _numberBrushCache, ff);

        ApplyTextBlockStyle(ProgressText, fs * 0.25, _textBrushCache, ff);

        ProgressBar.Foreground = _progressBrushCache;
        ProgressBar.Height = Math.Max(3, 9 * fs / BaseFontSize);

        // 发光效果（数字 + 进度条，颜色随设置）
        ApplyGlow(DaysTb, s.NumberColor);
        ApplyGlow(HoursTb, s.NumberColor);
        ApplyGlow(MinutesTb, s.NumberColor);
        ApplyGlow(SecondsTb, s.NumberColor);

        // 文本内容（中英文自定义文案）
        ChinesePrefixTb.Text = s.ChinesePrefix;
        ChineseDaysTb.Text = s.ChineseDaysText;
        ChineseHoursTb.Text = s.ChineseHoursText;
        ChineseMinutesTb.Text = s.ChineseMinutesText;
        ChineseSecondsTb.Text = s.ChineseSecondsText;
        EnglishPrefixTb.Text = s.EnglishPrefix;
        EnglishDaysTb.Text = s.EnglishDaysText;
        EnglishHoursTb.Text = s.EnglishHoursText;
        EnglishMinutesTb.Text = s.EnglishMinutesText;
        EnglishSecondsTb.Text = s.EnglishSecondsText;

        // 显示单位显隐
        DaysTb.IsVisible = s.ShowDays;
        ChineseDaysTb.IsVisible = s.ShowDays;
        HoursTb.IsVisible = s.ShowHours;
        ChineseHoursTb.IsVisible = s.ShowHours;
        MinutesTb.IsVisible = s.ShowMinutes;
        ChineseMinutesTb.IsVisible = s.ShowMinutes;
        SecondsTb.IsVisible = s.ShowSeconds;
        ChineseSecondsTb.IsVisible = s.ShowSeconds;
        EnglishRow.IsVisible = s.ShowEnglishLine;
        ProgressBar.IsVisible = s.ShowProgressBar;
        ProgressText.IsVisible = s.ShowProgressText;

        // 自定义位置模式可拖动
        _draggable = s.PositionPreset == PositionPresetValues.Custom;

        // 课表悬浮栏显隐（设置控制）
        if (s.ShowScheduleBar && _scheduleBar == null) ShowScheduleBar();
        else if (!s.ShowScheduleBar) HideScheduleBar();
    }

    private static void ApplyTextBlockStyle(TextBlock tb, double fontSize, IBrush brush, FontFamily ff)
    {
        tb.FontSize = fontSize;
        tb.Foreground = brush;
        tb.FontFamily = ff;
    }

    private static void ApplyGlow(TextBlock tb, Color color)
    {
        // Avalonia 12：WPF DropShadowEffect 对应 DropShadowDirectionEffect（ShadowDepth=0 即发光）
        if (tb.Effect is not DropShadowDirectionEffect)
        {
            tb.Effect = new DropShadowDirectionEffect
            {
                BlurRadius = 18,
                Color = color,
                Opacity = 0.55,
                ShadowDepth = 0,
                Direction = 0
            };
        }
        else
        {
            ((DropShadowDirectionEffect)tb.Effect).Color = color;
        }
    }

    /// <summary>每秒刷新倒计时与进度；上课/考试期间隐藏主窗口</summary>
    private void Tick()
    {
        var now = Helpers.TimeSimulator.Now;
        var s = App.Settings;

        // ── 上课期间隐藏主窗口（可设置科目白名单）──
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
            if (isInClass && !string.IsNullOrWhiteSpace(s.HideSubjects))
                _scheduleBar?.Hide();
            return;
        }
        if (_hiddenByScheduleOrExam)
        {
            // 退出隐藏模式 — 延迟 2 分钟恢复（给老师关 PPT 时间）
            _hiddenByScheduleOrExam = false;
            if (_classEndRestoreTimer == null)
            {
                _classEndRestoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
                _classEndRestoreTimer.Tick += (_, _) =>
                {
                    _classEndRestoreTimer?.Stop();
                    _classEndRestoreTimer = null;
                    Show();
                    if (App.Settings.EnableAnimations) PlayIntroAnimation();
                    _scheduleBar?.Show();
                    Tick();
                };
                _classEndRestoreTimer.Start();
            }
            return;
        }
        _classEndRestoreTimer?.Stop();
        _classEndRestoreTimer = null;

        if (_hiddenByMaximize) return;

        // ── 倒计时数据 ──────────────────────────────────────
        var timeLeft = _gaokaoDate - now;
        bool positive = timeLeft.TotalSeconds > 0;
        int days = positive ? timeLeft.Days : 0;
        int hours = positive ? timeLeft.Hours : 0;
        int minutes = positive ? timeLeft.Minutes : 0;
        int seconds = positive ? timeLeft.Seconds : 0;

        // 入场动画进行中：跳过文本更新
        bool introRunning = _introTimer != null;

        // 脉冲动画：仅当值变化时触发
        if (s.EnableAnimations && !introRunning)
        {
            if (days != _lastDays && s.ShowDays) PulseNumber(DaysTb);
            if (hours != _lastHours && s.ShowHours) PulseNumber(HoursTb);
            if (minutes != _lastMinutes && s.ShowMinutes) PulseNumber(MinutesTb);
            if (s.ShowSeconds) PulseNumber(SecondsTb);
        }
        _lastDays = days; _lastHours = hours; _lastMinutes = minutes; _lastSeconds = seconds;

        if (!introRunning)
        {
            DaysTb.Text = days.ToString();
            HoursTb.Text = hours.ToString("00");
            MinutesTb.Text = minutes.ToString("00");
            SecondsTb.Text = seconds.ToString("00");
            DaysEnTb.Text = DaysTb.Text;
            HoursEnTb.Text = HoursTb.Text;
            MinutesEnTb.Text = MinutesTb.Text;
            SecondsEnTb.Text = SecondsTb.Text;
        }

        // ── 进度条 ──────────────────────────────────────────
        double totalDays = (_gaokaoDate - _startDate).TotalDays;
        double passed = (now - _startDate).TotalDays;
        double progress = Math.Clamp(passed / totalDays, 0, 1) * 100;
        ProgressBar.Value = progress;
        string fmt = "F" + s.ProgressDecimalDigits;
        ProgressText.Text = $"高中生活已过去 {progress.ToString(fmt)}%";

        UpdateCustomCountdown(now);

        // 倒计时归零停止
        if (days == 0 && hours == 0 && minutes == 0 && seconds == 0)
            _timer?.Stop();
    }

    /// <summary>自定义倒计时（显示最近一个未来目标）</summary>
    private void UpdateCustomCountdown(DateTime now)
    {
        var list = App.Settings.CustomCountdowns;
        if (list == null || list.Count == 0)
        {
            CustomCountdownTb.IsVisible = false;
            return;
        }

        DateTime? nearest = null;
        string? name = null;
        foreach (var cc in list)
        {
            if (DateTime.TryParse(cc.DateStr, out var dt) && dt > now &&
                (nearest == null || dt < nearest))
            {
                nearest = dt;
                name = cc.Name;
            }
        }

        if (nearest == null)
        {
            CustomCountdownTb.IsVisible = false;
            return;
        }

        var ts = nearest.Value - now;
        CustomCountdownTb.Text = $"📅 {name} 还剩 {ts.Days} 天 {ts.Hours:D2}时{ts.Minutes:D2}分";
        CustomCountdownTb.IsVisible = true;
    }

    // ── 数字脉冲动画：缩放 + 透明度（DispatcherTimer 逐帧，轻量）──
    private void PulseNumber(TextBlock tb)
    {
        if (tb.RenderTransform is not ScaleTransform st)
        {
            tb.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            tb.RenderTransform = new ScaleTransform(1, 1);
            st = (ScaleTransform)tb.RenderTransform;
        }

        var start = DateTime.Now;
        var pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        pulseTimer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.Now - start).TotalMilliseconds;
            if (elapsed >= 250)
            {
                pulseTimer.Stop();
                st.ScaleX = 1; st.ScaleY = 1;
                tb.Opacity = 1;
                return;
            }
            double t = elapsed / 250.0;
            // 0→0.4 放大到 1.08 / 透明到 0.72，0.4→1 回落
            double scale = t < 0.4 ? 1 + 0.08 * (t / 0.4) : 1 + 0.08 * (1 - (t - 0.4) / 0.6);
            double op = t < 0.4 ? 1 - 0.28 * (t / 0.4) : 0.72 + 0.28 * ((t - 0.4) / 0.6);
            st.ScaleX = scale; st.ScaleY = scale;
            tb.Opacity = op;
        };
        pulseTimer.Start();
    }

    // ── 入场动画：数字 0→实际值滚动 + 进度条动画 ────────────
    private void PlayIntroAnimation()
    {
        _introTimer?.Stop();
        _introTimer = null;

        DateTime now = DateTime.Now;
        TimeSpan timeLeft = _gaokaoDate - now;
        _introDays = timeLeft.TotalSeconds > 0 ? timeLeft.Days : 0;
        _introHours = timeLeft.TotalSeconds > 0 ? timeLeft.Hours : 0;
        _introMinutes = timeLeft.TotalSeconds > 0 ? timeLeft.Minutes : 0;
        _introSeconds = timeLeft.TotalSeconds > 0 ? timeLeft.Seconds : 0;

        double totalDays = (_gaokaoDate - _startDate).TotalDays;
        double daysPassed = (now - _startDate).TotalDays;
        _introProgress = Math.Clamp(daysPassed / totalDays, 0, 1) * 100.0;

        ProgressBar.Value = 0;
        _introStart = DateTime.Now;
        _introTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _introTimer.Tick += IntroTimer_Tick;
        _introTimer.Start();
    }

    private void IntroTimer_Tick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.Now - _introStart).TotalMilliseconds;
        double t = Math.Min(1.0, elapsed / IntroDurationMs);
        double eased = 1.0 - Math.Pow(1.0 - t, 5);   // PowerEaseOut(Power=5)

        int days = (int)Math.Round(eased * _introDays);
        int hours = (int)Math.Round(eased * _introHours);
        int minutes = (int)Math.Round(eased * _introMinutes);
        int seconds = (int)Math.Round(eased * _introSeconds);

        DaysTb.Text = days.ToString();
        HoursTb.Text = hours.ToString("00");
        MinutesTb.Text = minutes.ToString("00");
        SecondsTb.Text = seconds.ToString("00");
        DaysEnTb.Text = DaysTb.Text;
        HoursEnTb.Text = HoursTb.Text;
        MinutesEnTb.Text = MinutesTb.Text;
        SecondsEnTb.Text = SecondsTb.Text;

        ProgressBar.Value = _introProgress * eased;

        if (t >= 1.0)
        {
            _introTimer!.Stop();
            _introTimer = null;
            DaysTb.Text = _introDays.ToString();
            HoursTb.Text = _introHours.ToString("00");
            MinutesTb.Text = _introMinutes.ToString("00");
            SecondsTb.Text = _introSeconds.ToString("00");
            DaysEnTb.Text = DaysTb.Text;
            HoursEnTb.Text = HoursTb.Text;
            MinutesEnTb.Text = MinutesTb.Text;
            SecondsEnTb.Text = SecondsTb.Text;
            ProgressBar.Value = _introProgress;
        }
    }

    // ── 位置预设（0顶部/1中上/2居中/3中下/4底部/5自定义）──────
    private void PositionToPreset()
    {
        var s = App.Settings;
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(new PixelPoint(0, 0), new PixelSize(1920, 1080));
        double w = Bounds.Width > 0 ? Bounds.Width : 850;
        double h = Bounds.Height > 0 ? Bounds.Height : 175;

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
        // 只在自定义模式（preset=5）时回写坐标（程序化定位时抑制）
        if (_isPositioning) return;
        if (App.Settings.PositionPreset != PositionPresetValues.Custom) return;
        App.Settings.CustomPositionX = e.Point.X;
        App.Settings.CustomPositionY = e.Point.Y;
    }

    // ── 点击穿透：预设模式穿透 / 自定义可交互 ────────────────
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
    }

    // ── 课表栏管理 ───────────────────────────────────────────
    private void ShowScheduleBar()
    {
        if (_scheduleBar != null) return;
        _scheduleBar = new ScheduleBarWindow();
        if (App.Reminders != null) App.Reminders.Reminder += OnReminder;
        _scheduleBar.Closed += (_, _) =>
        {
            if (App.Reminders != null) App.Reminders.Reminder -= OnReminder;
            _scheduleBar = null;
        };
        _scheduleBar.Show();
    }

    private void HideScheduleBar()
    {
        _scheduleBar?.Close();
        _scheduleBar = null;
    }

    /// <summary>提醒事件 → 课表栏临时展开（对齐 WPF MainWindow.OnReminder）</summary>
    private void OnReminder(object? sender, ReminderEventArgs e)
    {
        _scheduleBar?.ExpandOnReminder(e.Type);
    }

    // ── 自启动（注册表 HKCU\Run，P/Invoke advapi32）──────────
    private static bool GetAutoStartFromRegistry()
    {
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
            // 非自定义模式：双击打开设置
            if (e.ClickCount >= 2)
            {
                OpenSettings();
            }
        }
    }

    private SettingsWindow? _settingWindow;

    public void OpenSettings()
    {
        // 单例：重复打开时激活已有窗口（对齐 WPF 重入防护）
        if (_settingWindow != null)
        {
            try { _settingWindow.Activate(); return; }
            catch { _settingWindow = null; }
        }
        _settingWindow = new SettingsWindow();
        _settingWindow.Closed += (_, _) => _settingWindow = null;
        // 上课/考试隐藏时主窗口不可见，不能作为 owner（Avalonia 会抛 InvalidOperationException）
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
        HideScheduleBar();
        Close();
    }
}
