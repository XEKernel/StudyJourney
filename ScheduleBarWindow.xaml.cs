using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    public partial class ScheduleBarWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ScheduleManager _manager;
        private readonly ReminderService _reminder;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _weatherTimer;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

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

            // ── 窗口入场淡入动画 ──
            Opacity = 0;
            var fadeIn = new DoubleAnimation(0, _settings.ScheduleBarOpacity,
                TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) => Opacity = _settings.ScheduleBarOpacity;
            BeginAnimation(Window.OpacityProperty, fadeIn);

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

        // ── 定时刷新 ──────────────────────────────────────────
        private DispatcherTimer? _expandTimer;
        private bool _isCompact = false;

        // ── "快上课"闪烁状态 ──────────────────────────────────
        private bool _flashVisible = true;
        private const int FLASH_THRESHOLD_SECONDS = 60;
        private bool _showTomorrowPreview = false;
        private bool _tomorrowChecked = false;   // 防止每天重复检查

        // ── 课节卡片缓存（避免每秒重建 UI）─────────────────────
        private DateTime _lastBuildDate = DateTime.MinValue;
        /// <summary>(entry, card, 节次Label, 课程Label, 时间Label)</summary>
        private readonly List<(ScheduleEntry Entry, Border Card, TextBlock PeriodLabel,
                               TextBlock SubjectLabel, TextBlock TimeLabel)> _periodCardRefs = new();

        // ── 缓存画刷（避免每秒 new SolidColorBrush）─────────────
        private static readonly SolidColorBrush BrOrange   = new(Color.FromRgb(0xFF, 0x88, 0x44));
        private static readonly SolidColorBrush BrRed      = new(Color.FromRgb(0xFF, 0x44, 0x44));
        private static readonly SolidColorBrush BrGreen    = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrGray     = new(Color.FromRgb(0xAA, 0xAA, 0xAA));
        private static readonly SolidColorBrush BrLightGray= new(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush BrWhite    = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush BrLtGreen  = new(Color.FromRgb(0xA5, 0xD6, 0xA7));
        private static readonly SolidColorBrush BrLtBlue   = new(Color.FromRgb(0x90, 0xCA, 0xF9));
        private static readonly SolidColorBrush BrFlashBg  = new(Color.FromRgb(0x55, 0x15, 0x00));
        private static readonly SolidColorBrush BrIndicator= new(Color.FromRgb(0x4C, 0xAF, 0x50));

        // ── 定时刷新 ──────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
        }

        // ── 核心刷新方法 ──────────────────────────────────────
        private void Refresh()
        {
            var now = DateTime.Now;

            // 当前时间
            CurrentTimeTb.Text = now.ToString("HH:mm:ss");
            DateTb.Text = now.ToString("MM月dd日 ddd");

            // 当前/下节信息
            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);
            var timeToNext = _manager.GetTimeToNextEntry(now);

            // ── "快上课"闪烁检测 ──
            bool isFlashing = timeToNext.HasValue
                && timeToNext.Value.TotalSeconds <= FLASH_THRESHOLD_SECONDS
                && timeToNext.Value.TotalSeconds > 0;
            if (isFlashing)
                _flashVisible = !_flashVisible;
            else
                _flashVisible = true;

            // 重建课节列表（仅在日期变更或首次时重建，其余仅更新状态）
            if (_lastBuildDate != now.Date)
            {
                _lastBuildDate = now.Date;
                RebuildPeriodPanel(now);
            }
            else
            {
                UpdatePeriodCardStates(now);
            }

            // 状态文本（快上课时显示特殊提示）
            if (isFlashing && next != null)
            {
                int remainSec = (int)timeToNext!.Value.TotalSeconds;
                StatusTb.Text = $"即将上课：{next.Subject}";
                StatusTb.Foreground = BrOrange;
                NextCountdownTb.Text = $"还有 {remainSec}s";
                NextCountdownTb.Foreground = BrRed;
            }
            else if (cur != null)
            {
                StatusTb.Text = $"正在上课：{cur.Subject}";
                StatusTb.Foreground = BrGreen;
            }
            else if (next != null)
            {
                StatusTb.Text = "课间休息";
                StatusTb.Foreground = BrOrange;
            }
            else
            {
                StatusTb.Text = "今日课程已结束";
                StatusTb.Foreground = BrGray;
            }

            // 距下节课倒计时（非闪烁时显示正常倒计时）
            if (!isFlashing && timeToNext.HasValue)
            {
                var ts = timeToNext.Value;
                NextCountdownTb.Text = $"距下节课 {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                NextCountdownTb.Foreground = BrOrange;
            }
            else if (!isFlashing)
            {
                NextCountdownTb.Text = string.Empty;
            }

            // 当前课进度条
            var pct = _manager.GetCurrentProgress(now);
            if (pct.HasValue && cur != null)
            {
                ProgressRow.Visibility = Visibility.Visible;
                ProgressLabelTb.Text   = cur.Subject;
                CurrentClassProgress.Value = pct.Value * 100;
                ProgressPctTb.Text     = $"{pct.Value * 100:F0}%";

                // 同步更新紧凑模式进度条
                CompactProgress.Value = pct.Value * 100;
                CompactPctTb.Text = $"{pct.Value * 100:F0}%";
                CompactSubjectTb.Text = cur.Subject;
                var remaining = _manager.GetTimeToEndOfCurrent(now);
                CompactRemainingTb.Text = remaining.HasValue
                    ? $"剩余 {remaining.Value.Hours:D2}:{remaining.Value.Minutes:D2}:{remaining.Value.Seconds:D2}"
                    : "";
            }
            else
            {
                ProgressRow.Visibility = Visibility.Collapsed;
            }

            // ── 自动收缩/展开 ──
            if (_settings.ScheduleBarAutoCollapse)
            {
                bool inClass = cur != null;
                if (inClass && !_isCompact && _expandTimer == null)
                {
                    SetCompact();
                }
                else if (!inClass && _isCompact)
                {
                    SetExpanded();
                }
            }
            else if (_isCompact)
            {
                // 设置关闭了自动收缩，立即展开
                SetExpanded();
            }

            // ── 放学后 / 周末显示明天课程 ──
            bool todayDone = cur == null && next == null;
            if (todayDone && !_tomorrowChecked)
            {
                _tomorrowChecked = true;
                var tomorrowEntries = _manager.GetTodayEntries(now.Date.AddDays(1));
                if (tomorrowEntries.Count > 0)
                {
                    _showTomorrowPreview = true;
                    _lastBuildDate = DateTime.MinValue; // 强制重建
                    RebuildPeriodPanel(now);
                    _lastBuildDate = now.Date;
                }
            }
            else if (!todayDone)
            {
                _showTomorrowPreview = false;
                _tomorrowChecked = false;
            }
        }

        // ── 重建课节卡片（仅日期变更时调用）─────────────────────
        private void RebuildPeriodPanel(DateTime now)
        {
            PeriodPanel.Children.Clear();
            _periodCardRefs.Clear();
            var entries = _manager.GetTodayEntries(now.Date);
            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);

            double baseFont = _settings.ScheduleBarFontSize;
            if (baseFont <= 0) baseFont = 14;
            double periodLabelSize = baseFont * 0.65;
            double subjectSize     = baseFont * 0.8;
            double timeSize        = baseFont * 0.65;

            // 放学后直接展示明天课程，不显示今天的
            if (_showTomorrowPreview)
            {
                var tomorrowEntries = _manager.GetTodayEntries(now.Date.AddDays(1));
                if (tomorrowEntries.Count > 0)
                {
                    var header = new TextBlock
                    {
                        Text = "明天课程",
                        FontSize = periodLabelSize * 1.2,
                        Foreground = BrOrange,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 6, 0)
                    };
                    PeriodPanel.Children.Add(header);
                    foreach (var entry in tomorrowEntries)
                        BuildCard(entry, false, false, periodLabelSize, subjectSize, timeSize);
                }
                else
                {
                    var empty = new TextBlock
                    {
                        Text = "明日无课",
                        FontSize = periodLabelSize * 1.2,
                        Foreground = BrGray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    PeriodPanel.Children.Add(empty);
                }
                return;
            }

            if (entries.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "今日无课",
                    FontSize = periodLabelSize * 1.2,
                    Foreground = BrGray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                PeriodPanel.Children.Add(empty);
                return;
            }

            foreach (var entry in entries)
            {
                bool isCur  = cur  == entry;
                bool isNext = next == entry;
                BuildCard(entry, isCur, isNext, periodLabelSize, subjectSize, timeSize);
            }
        }

        private void BuildCard(ScheduleEntry entry, bool isCur, bool isNext,
                               double periodLabelSize, double subjectSize, double timeSize)
        {
            var cardStyle = isCur ? (Style)FindResource("PeriodCardActive")
                          : isNext ? (Style)FindResource("PeriodCardNext")
                          : (Style)FindResource("PeriodCard");

            var card = new Border { Style = cardStyle };

            if (isNext && !_flashVisible)
            {
                card.Opacity = 0.25;
                card.BorderBrush = BrOrange;
                card.Background = BrFlashBg;
            }

            var periodTb = new TextBlock
            {
                Text = entry.Type switch
                {
                    PeriodType.Morning => "早自习",
                    PeriodType.Evening => "晚自习",
                    PeriodType.Reading => "晚读",
                    PeriodType.Noon    => "午自习",
                    _                  => $"第 {entry.Period} 节"
                },
                FontSize = periodLabelSize,
                Foreground = isCur ? BrLtGreen : isNext ? BrLtBlue : BrLightGray,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var subjectTb = new TextBlock
            {
                Text = entry.Subject,
                FontSize = subjectSize,
                FontWeight = isCur ? FontWeights.Bold : FontWeights.Normal,
                Foreground = BrWhite,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var timeTb = new TextBlock
            {
                Text = $"{entry.StartTimeStr}-{entry.EndTimeStr}",
                FontSize = timeSize,
                Foreground = BrLightGray,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();
            stack.Children.Add(periodTb);
            stack.Children.Add(subjectTb);
            stack.Children.Add(timeTb);
            card.Child = stack;

            if (isCur)
            {
                var outer = new Grid();
                outer.Children.Add(card);
                var indicator = new Border
                {
                    Height = 2,
                    Background = BrIndicator,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(0, 0, 4, 4)
                };
                outer.Children.Add(indicator);
                PeriodPanel.Children.Add(outer);
            }
            else
            {
                PeriodPanel.Children.Add(card);
            }

            _periodCardRefs.Add((entry, card, periodTb, subjectTb, timeTb));
        }

        /// <summary>同一天内仅更新卡片视觉状态（不重建控件）</summary>
        private void UpdatePeriodCardStates(DateTime now)
        {
            if (_periodCardRefs.Count == 0) return;
            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);

            foreach (var (entry, card, periodTb, subjectTb, timeTb) in _periodCardRefs)
            {
                bool isCur  = cur  == entry;
                bool isNext = next == entry;

                // 更新卡片样式
                card.Style = isCur ? (Style)FindResource("PeriodCardActive")
                           : isNext ? (Style)FindResource("PeriodCardNext")
                           : (Style)FindResource("PeriodCard");

                // 闪烁状态
                if (isNext && !_flashVisible)
                {
                    card.Opacity = 0.25;
                    card.BorderBrush = BrOrange;
                    card.Background = BrFlashBg;
                }
                else
                {
                    card.Opacity = 1;
                    card.ClearValue(Border.BorderBrushProperty);
                    card.ClearValue(Border.BackgroundProperty);
                }

                // 文字颜色
                periodTb.Foreground = isCur ? BrLtGreen : isNext ? BrLtBlue : BrLightGray;
                subjectTb.FontWeight = isCur ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        // ── 60 秒倒计时回调 ───────────────────────────────────
        private void OnCountdown60Tick(object? sender, int remaining)
        {
            Dispatcher.Invoke(() =>
            {
                if (remaining > 0)
                {
                    if (Countdown60Panel.Visibility != Visibility.Visible)
                    {
                        Countdown60Panel.Visibility = Visibility.Visible;
                        Countdown60Panel.Opacity = 0;
                        if (Countdown60Panel.RenderTransform is not ScaleTransform)
                        {
                            Countdown60Panel.RenderTransform = new ScaleTransform(0.8, 0.8);
                            Countdown60Panel.RenderTransformOrigin = new Point(0.5, 0.5);
                        }
                        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        var scaleIn = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(250))
                        {
                            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                        };
                        Countdown60Panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        if (Countdown60Panel.RenderTransform is ScaleTransform st)
                        {
                            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
                        }
                    }
                    Countdown60Tb.Text = $"下课倒计时 {remaining}s";
                }
                else
                {
                    // 淡出后隐藏
                    if (Countdown60Panel.Visibility == Visibility.Visible)
                    {
                        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                        };
                        fadeOut.Completed += (_, _) =>
                        {
                            Countdown60Panel.Visibility = Visibility.Collapsed;
                        };
                        Countdown60Panel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                }
            });
        }

        // ── 紧凑/展开模式（带动画过渡）───────────────────────

        /// <summary>切换到紧凑模式（仅显示进度条），带交叉淡入淡出</summary>
        private void SetCompact()
        {
            if (_isCompact) return;
            _isCompact = true;

            // 先淡出完整模式
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                FullInfoRoot.Visibility = Visibility.Collapsed;
                CompactRow.Visibility = Visibility.Visible;
                CompactRow.Opacity = 0;

                // 淡入紧凑模式
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                CompactRow.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                PositionToTop();
            };
            FullInfoRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>切换到完整模式，带交叉淡入淡出</summary>
        private void SetExpanded()
        {
            if (!_isCompact) return;
            _isCompact = false;

            // 先淡出紧凑模式
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                CompactRow.Visibility = Visibility.Collapsed;
                FullInfoRoot.Visibility = Visibility.Visible;
                FullInfoRoot.Opacity = 0;

                // 淡入完整模式
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                FullInfoRoot.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                PositionToTop();
            };
            CompactRow.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        /// <summary>手动展开按钮（紧凑模式下点击展开箭头）</summary>
        private void ExpandBtn_Click(object sender, RoutedEventArgs e)
        {
            _expandTimer?.Stop();
            _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _expandTimer.Tick += (_, _) =>
            {
                _expandTimer?.Stop();
                _expandTimer = null;
                var cur = _manager.GetCurrentEntry(DateTime.Now);
                if (cur != null && _settings.ScheduleBarAutoCollapse)
                    SetCompact();
            };
            _expandTimer.Start();
            SetExpanded();
        }

        /// <summary>提醒时临时展开 10 秒，之后若仍在课上则恢复紧凑</summary>
        public void ExpandOnReminder(ReminderType type)
        {
            if (!_isCompact) return;

            bool shouldExpand = type switch
            {
                ReminderType.ClassEndSoon  => true,
                ReminderType.ClassEnd      => true,
                ReminderType.NextClassSoon => true,
                ReminderType.DayEnd        => true,
                _ => false
            };
            if (!shouldExpand) return;

            SetExpanded();
            _expandTimer?.Stop();
            _expandTimer = null;
            _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _expandTimer.Tick += (_, _) =>
            {
                _expandTimer?.Stop();
                _expandTimer = null;
                var cur = _manager.GetCurrentEntry(DateTime.Now);
                if (cur != null) SetCompact();
            };
            _expandTimer.Start();
        }

        // ── 天气加载（复用 WeatherWindow 逻辑）───────────────
        public async System.Threading.Tasks.Task LoadWeatherAsync()
        {
            try
            {
                string city = string.IsNullOrWhiteSpace(_settings.WeatherCity)
                    ? "北京" : _settings.WeatherCity.Trim();
                string adcode = (_settings.WeatherAdcode ?? "").Trim();
                string url = $"https://uapis.cn/api/v1/misc/weather?city={Uri.EscapeDataString(city)}" +
                             $"&adcode={Uri.EscapeDataString(adcode)}" +
                             "&extended=false&forecast=false&hourly=false&minutely=false&indices=false&lang=zh";

                var json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string rCity     = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                string district  = root.TryGetProperty("district", out var d) ? d.GetString() ?? "" : "";
                string weather   = root.TryGetProperty("weather", out var w) ? w.GetString() ?? "" : "";
                string wIcon     = root.TryGetProperty("weather_icon", out var wi) ? wi.GetString() ?? "" : "";
                int temperature  = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number
                    ? (int)t.GetDouble() : 0;
                string windDir   = root.TryGetProperty("wind_direction", out var wd) ? wd.GetString() ?? "" : "";
                string windPower = root.TryGetProperty("wind_power", out var wp) ? wp.GetString() ?? "" : "";
                int humidity     = root.TryGetProperty("humidity", out var h) && h.ValueKind == JsonValueKind.Number
                    ? (int)h.GetDouble() : 0;

                string location = !string.IsNullOrWhiteSpace(district) ? district
                    : !string.IsNullOrWhiteSpace(rCity) ? rCity : city;

                await Dispatcher.InvokeAsync(() =>
                {
                    // 应用天气字体大小
                    double weatherFs = _settings.WeatherFontSize;
                    if (weatherFs <= 0) weatherFs = 14;
                    WeatherIconTb.FontSize = weatherFs * 0.86;
                    WeatherCityTb.FontSize = weatherFs * 0.72;
                    WeatherTb.FontSize = weatherFs * 0.72;
                    WeatherTempTb.FontSize = weatherFs * 0.8;
                    WeatherWindTb.FontSize = weatherFs * 0.65;
                    WeatherHumidityTb.FontSize = weatherFs * 0.65;

                    // 应用天气颜色
                    WeatherCityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherCityColor, "#FFFFFFFF");
                    WeatherTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherWindTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherHumidityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherTempTb.Foreground = ColorUtils.ParseColor(_settings.WeatherTempColor, "#FFFF8844");
                    WeatherIconTb.Foreground = ColorUtils.ParseColor(_settings.WeatherIconColor, "#FFFFAA00");

                    WeatherIconTb.Text = ColorUtils.GetWeatherEmoji(wIcon);
                    WeatherCityTb.Text = location;
                    WeatherTb.Text = weather;
                    WeatherTempTb.Text = $"{temperature}°";
                    WeatherWindTb.Text = !string.IsNullOrWhiteSpace(windDir)
                        ? $"{windDir} {windPower}".Trim() : "--";
                    WeatherHumidityTb.Text = humidity > 0 ? $"{humidity}%" : "--";
                    if (WeatherRow.Visibility != Visibility.Visible)
                    {
                        WeatherRow.Visibility = Visibility.Visible;
                        WeatherRow.Opacity = 0;
                        var weatherFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        WeatherRow.BeginAnimation(UIElement.OpacityProperty, weatherFadeIn);
                    }
                });
            }
            catch { /* 网络异常静默 */ }
        }

        private void StartWeatherTimer()
        {
            _weatherTimer?.Stop();
            int intervalMin = _settings.WeatherRefreshInterval;
            if (intervalMin <= 0) return;
            _weatherTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMin)
            };
            _weatherTimer.Tick += async (_, _) => await LoadWeatherAsync();
            _weatherTimer.Start();
        }

        // GetWeatherEmoji / ParseColor 已移至共享 ColorUtils 类
    }
}
