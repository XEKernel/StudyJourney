using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 桌面小组件主窗口（对齐学程 WPF 主窗口）：
/// 无边框圆角卡片 + 倒计时 + 进度 + 自定义倒计时 + 每日一言；
/// 位置预设/透明度/字号/颜色/显示单位全部从 App.Settings 读取，设置保存后自动刷新。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private DispatcherTimer? _timer;
    private DispatcherTimer? _quoteTimer;
    private ScheduleBarWindow? _scheduleBar;
    private DateTime _gaokaoDate = new(2027, 6, 7, 9, 0, 0);
    private DateTime _startDate = new(2024, 8, 24);
    private bool _draggable;   // 自定义位置模式可拖动

    public MainWindow()
    {
        InitializeComponent();
        RefreshDates();

        // 设置变更 → 立即应用（设置窗口保存后触发）
        App.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => App.SettingsChanged -= OnSettingsChanged;
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        ApplySettings();
        PositionToPreset();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();

        // 每日一言：启动加载 + 定时刷新
        if (App.Settings.ShowDailyQuote)
        {
            _ = LoadQuoteAsync();
            StartQuoteTimer();
        }
    }

    // ── 每日一言（HTTP + 定时刷新，逻辑同 WPF MainWindowViewModel.FetchQuoteAsync）──
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
        RefreshDates();
        ApplySettings();
        PositionToPreset();
        Tick();
    }

    private void RefreshDates()
    {
        var s = App.Settings;
        if (DateTime.TryParse(s.GaokaoDateStr, out var g)) _gaokaoDate = g;
        if (DateTime.TryParse(s.StartDateStr, out var d)) _startDate = d;
    }

    /// <summary>应用静态样式（透明度/字号/颜色/置顶/显示单位）</summary>
    private void ApplySettings()
    {
        var s = App.Settings;
        Opacity = Math.Clamp(s.OverallOpacity, 0.1, 1.0);
        Topmost = s.AlwaysOnTop;

        double fs = s.FontSize;
        if (fs <= 0) fs = 40;
        ChinesePrefixTb.FontSize = fs;
        DaysTb.FontSize = fs;
        ChineseDaysTb.FontSize = fs;
        HoursTb.FontSize = fs;
        ChineseHoursTb.FontSize = fs;
        MinutesTb.FontSize = fs;
        ChineseMinutesTb.FontSize = fs;
        SecondsTb.FontSize = fs;
        ChineseSecondsTb.FontSize = fs;

        DaysTb.Foreground = new SolidColorBrush(s.NumberColor);
        HoursTb.Foreground = new SolidColorBrush(s.NumberColor);
        MinutesTb.Foreground = new SolidColorBrush(s.NumberColor);
        SecondsTb.Foreground = new SolidColorBrush(s.NumberColor);
        ChinesePrefixTb.Foreground = new SolidColorBrush(s.TextColor);
        ChineseDaysTb.Foreground = new SolidColorBrush(s.TextColor);
        ChineseHoursTb.Foreground = new SolidColorBrush(s.TextColor);
        ChineseMinutesTb.Foreground = new SolidColorBrush(s.TextColor);
        ChineseSecondsTb.Foreground = new SolidColorBrush(s.TextColor);
        ProgressBar.Foreground = new SolidColorBrush(s.ProgressBarColor);
        ProgressText.Foreground = new SolidColorBrush(s.TextColor);

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
        if (s.ShowScheduleBar && _scheduleBar == null)
        {
            _scheduleBar = new ScheduleBarWindow();
            _scheduleBar.Show();
        }
        else if (!s.ShowScheduleBar && _scheduleBar != null)
        {
            _scheduleBar.Close();
            _scheduleBar = null;
        }
    }

    /// <summary>每秒刷新倒计时与进度</summary>
    private void Tick()
    {
        var now = DateTime.Now;
        var s = App.Settings;
        var timeLeft = _gaokaoDate - now;
        bool positive = timeLeft.TotalSeconds > 0;

        DaysTb.Text = positive ? timeLeft.Days.ToString() : "0";
        HoursTb.Text = positive ? timeLeft.Hours.ToString("00") : "00";
        MinutesTb.Text = positive ? timeLeft.Minutes.ToString("00") : "00";
        SecondsTb.Text = positive ? timeLeft.Seconds.ToString("00") : "00";
        DaysEnTb.Text = DaysTb.Text;
        HoursEnTb.Text = HoursTb.Text;
        MinutesEnTb.Text = MinutesTb.Text;
        SecondsEnTb.Text = SecondsTb.Text;

        double totalDays = (_gaokaoDate - _startDate).TotalDays;
        double passed = (now - _startDate).TotalDays;
        double progress = Math.Clamp(passed / totalDays, 0, 1) * 100;
        ProgressBar.Value = progress;
        string fmt = "F" + s.ProgressDecimalDigits;
        ProgressText.Text = $"高中生活已过去 {progress.ToString(fmt)}%";

        UpdateCustomCountdown(now);
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

    // ── 位置预设（对齐学程 PositionWindow：0顶部/1中上/2居中/3中下/4底部/5自定义）──
    private void PositionToPreset()
    {
        var s = App.Settings;
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(new PixelPoint(0, 0), new PixelSize(1920, 1080));

        double x;
        double y;
        switch (s.PositionPreset)
        {
            case PositionPresetValues.Top:
                x = (area.Width - Width) / 2; y = 10; break;
            case PositionPresetValues.UpperCenter:
                x = (area.Width - Width) / 2; y = area.Height / 25.0; break;
            case PositionPresetValues.Center:
                x = (area.Width - Width) / 2; y = (area.Height - Height) / 2; break;
            case PositionPresetValues.LowerCenter:
                x = (area.Width - Width) / 2; y = area.Height * 0.65; break;
            case PositionPresetValues.Bottom:
                x = (area.Width - Width) / 2; y = area.Height - Height - 40; break;
            case PositionPresetValues.Custom:
                double cx = s.CustomPositionX < 0 ? (area.Width - Width) / 2 : s.CustomPositionX;
                double cy = s.CustomPositionY < 0 ? area.Height / 25.0 : s.CustomPositionY;
                x = cx; y = cy; break;
            default:
                x = (area.Width - Width) / 2; y = area.Height / 25.0; break;
        }

        Position = new PixelPoint((int)x, (int)y);
    }

    // ── 交互：自定义位置可拖动；双击打开设置 ────────────────
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

    private void OpenSettings()
    {
        var win = new SettingsWindow();
        win.Show(this);
    }

    // ── 右键菜单 ────────────────────────────────────────────
    private void ExamModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var win = new ExamModeWindow();
        win.Show();
    }

    private void OpenSettingsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void ExitMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        _scheduleBar?.Close();
        Close();
    }
}
