using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;

namespace StudyJourney.Avalonia.Views;

/// <summary>阶段 0 骨架窗口：验证 FluentAvalonia 主题 + 无边框透明置顶 + 点击穿透 + 倒计时渲染</summary>
public partial class MainWindow : FAAppWindow
{
    // ── Win32：点击穿透（与 WPF 版同一套 API）────────────────
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private bool _clickThrough;
    private DispatcherTimer? _timer;
    private DateTime _gaokaoDate = new(2027, 6, 7, 9, 0, 0);
    private DateTime _startDate = new(2024, 8, 24);

    public MainWindow()
    {
        InitializeComponent();

        // 手动放到屏幕左下角，避开 IDE 桌面遮挡，方便截图与查看
        Position = new global::Avalonia.PixelPoint(40, 480);

        // FA 标题栏：内容延伸到标题栏区域，WinUI 3 风格（FA 3.x 无 TitleBarHitTestType）
        TitleBar.ExtendsContentIntoTitleBar = true;

        // 从全局设置加载真实日期
        RefreshDates();
    }

    /// <summary>从全局设置解析高考/起算日期（与 WPF 版一致）</summary>
    private void RefreshDates()
    {
        var s = App.Settings;
        if (DateTime.TryParse(s.GaokaoDateStr, out var g)) _gaokaoDate = g;
        if (DateTime.TryParse(s.StartDateStr, out var d)) _startDate = d;
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    /// <summary>每秒刷新倒计时（读全局 App.Settings 的真实日期与格式）</summary>
    private void Tick()
    {
        var now = DateTime.Now;
        var timeLeft = _gaokaoDate - now;

        var s = App.Settings;
        // 显示单位按设置过滤（天/时/分/秒）
        if (s.ShowDays) DaysTb.Text = timeLeft.TotalSeconds > 0 ? timeLeft.Days.ToString() : "0";
        if (s.ShowHours) HoursTb.Text = timeLeft.TotalSeconds > 0 ? timeLeft.Hours.ToString("00") : "00";
        if (s.ShowMinutes) MinutesTb.Text = timeLeft.TotalSeconds > 0 ? timeLeft.Minutes.ToString("00") : "00";
        if (s.ShowSeconds) SecondsTb.Text = timeLeft.TotalSeconds > 0 ? timeLeft.Seconds.ToString("00") : "00";

        double totalDays = (_gaokaoDate - _startDate).TotalDays;
        double passed = (now - _startDate).TotalDays;
        double progress = Math.Clamp(passed / totalDays, 0, 1) * 100;
        if (s.ShowProgressBar) ProgressBar.Value = progress;
        if (s.ShowProgressText)
        {
            string fmt = "F" + s.ProgressDecimalDigits;
            ProgressText.Text = $"高中生活已过去 {progress.ToString(fmt)}%";
        }
    }

    // ── 四件套验证控件 ───────────────────────────────────────
    private void TopmostSwitch_Changed(object? sender, RoutedEventArgs e)
    {
        Topmost = TopmostSwitch.IsChecked == true;
    }

    private void OpacitySlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Opacity = e.NewValue;
    }

    private void ClickThroughBtn_Click(object? sender, RoutedEventArgs e)
    {
        ToggleClickThrough();
    }

    private void ExitBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenSettingsBtn_Click(object? sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow();
        win.Show(this);
    }

    /// <summary>切换 WS_EX_TRANSPARENT 点击穿透（Avalonia 拿 HWND 后走 Win32）</summary>
    private void ToggleClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        _clickThrough = !_clickThrough;
        int ex = GetWindowLong(handle, GWL_EXSTYLE);
        if (_clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(handle, GWL_EXSTYLE, ex);
        ClickThroughBtn.Content = _clickThrough ? "点击穿透：开" : "点击穿透：关";
    }
}
