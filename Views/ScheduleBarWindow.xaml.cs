using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
using GaokaoCountdown.Helpers;
namespace GaokaoCountdown.Views
{
    public partial class ScheduleBarWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ScheduleManager _manager;
        private readonly ReminderService _reminder;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _weatherTimer;

        // ── Win32（点击穿透 + 定位）─────────────────────────────
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private const int GWL_EXSTYLE      = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOMOVE       = 0x0002;
        private const uint SWP_NOSIZE       = 0x0001;
        private const uint SWP_NOACTIVATE   = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW   = 0x0040;

        public ScheduleBarWindow(AppSettings settings, ScheduleManager manager, ReminderService reminder)
        {
            _settings = settings;
            _manager  = manager;
            _reminder = reminder;
            InitializeComponent();

            // 订阅 60 秒倒计时
            _reminder.Countdown60Tick += OnCountdown60Tick;

            // ContentRendered：此时 SizeToContent 已完成，再定位一次（DPI 正确）
            ContentRendered += OnContentRendered;
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            // 只需一次：Loaded 里已调过，但这里确保 DPI 正确
            PositionToTop();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            ApplyFontSizes();
            PositionToTop();

            // ── 窗口入场淡入动画（FadeHelper 自动清理动画持有，避免 Opacity 残留）──
            Helpers.FadeHelper.FadeIn(this, 0, _settings.ScheduleBarOpacity, 400);

            StartTimer();
            Refresh();
            _ = LoadWeatherAsync();
            StartWeatherTimer();
        }

        // ── 应用字体大小 ──────────────────────────────────────
        public void ApplyFontSizes()
        {
            double baseFont = _settings.ScheduleBarFontSize; // default 14
            if (baseFont <= 0) baseFont = 14;

            CurrentTimeTb.FontSize = baseFont;
            DateTb.FontSize = baseFont * 0.65;
            StatusTb.FontSize = baseFont * 0.65;
            NextCountdownTb.FontSize = baseFont * 0.72;
            Countdown60Tb.FontSize = baseFont * 0.8;
            ProgressLabelTb.FontSize = baseFont * 0.65;
            ProgressPctTb.FontSize = baseFont * 0.65;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
            _weatherTimer?.Stop();
            _expandTimer?.Stop();
            _reminder.Countdown60Tick -= OnCountdown60Tick;
            ContentRendered -= OnContentRendered;
        }

        // ── 定位：宽度 = 所在显示器物理宽度，顶部贴边 ─────────
        private void PositionToTop()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // 获取窗口所在显示器的物理宽度
            IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            int screenW = 1920; // 兜底
            if (hMon != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMon, ref mi))
                    screenW = mi.rcMonitor.Width;
            }

            // 窗口高度由 SizeToContent 决定
            int h = (int)ActualHeight;
            if (h <= 0) h = 36;

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, screenW, h,
                SWP_SHOWWINDOW | SWP_NOACTIVATE);
        }

        // ── 应用设置（透明度 / 穿透 / 置顶）────────────────────
        public void ApplySettings()
        {
            Opacity = _settings.ScheduleBarOpacity;
            Topmost = _settings.ScheduleBarAlwaysOnTop;
            ApplyClickThrough(_settings.ScheduleBarClickThrough);
        }

        private bool _clickThroughEnabled = false;
        private void ApplyClickThrough(bool enable)
        {
            if (_clickThroughEnabled == enable) return;
            _clickThroughEnabled = enable;
            if (!IsLoaded) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enable) ex |= WS_EX_TRANSPARENT;
            else        ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

    }
}
