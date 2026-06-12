using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Hardcodet.Wpf.TaskbarNotification;
namespace GaokaoCountdown
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? timer;
        private TaskbarIcon? notifyIcon;
        private AppSettings settings;

        // ── 动态日期 ───────────────────────────────────────────
        private DateTime gaokaoDate;
        private DateTime startDate;

        // ── Win32 API ─────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

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

        private static readonly IntPtr HWND_BOTTOM    = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST  = new IntPtr(-1);
        private const uint SWP_NOSIZE    = 0x0001;
        private const uint SWP_NOMOVE    = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        // 基准尺寸
        private const int BaseFontSize     = 40;
        private const int BaseWindowWidth  = 850;
        private const int BaseWindowHeight = 150;

        // ── 上次 tick 的值（用于判断是否需要脉冲动画） ──
        private int _lastDays, _lastHours, _lastMinutes, _lastSeconds;

        // ── 最大化检测：记录上次隐藏状态，避免重复操作 ──
        private bool _hiddenByMaximize = false;
        private DispatcherTimer? _maximizeCheckTimer;

        // ── 设置窗口单例引用 ─────────────────────────────────
        private SettingWindow? _settingWindow;

        // ── 设置代理属性 ─────────────────────────────────────
        public string ChinesePrefix      { get => settings.ChinesePrefix;      set => settings.ChinesePrefix      = value; }
        public string ChineseDaysText    { get => settings.ChineseDaysText;    set => settings.ChineseDaysText    = value; }
        public string ChineseHoursText   { get => settings.ChineseHoursText;   set => settings.ChineseHoursText   = value; }
        public string ChineseMinutesText { get => settings.ChineseMinutesText; set => settings.ChineseMinutesText = value; }
        public string ChineseSecondsText { get => settings.ChineseSecondsText; set => settings.ChineseSecondsText = value; }

        public string EnglishPrefix      { get => settings.EnglishPrefix;      set => settings.EnglishPrefix      = value; }
        public string EnglishDaysText    { get => settings.EnglishDaysText;    set => settings.EnglishDaysText    = value; }
        public string EnglishHoursText   { get => settings.EnglishHoursText;   set => settings.EnglishHoursText   = value; }
        public string EnglishMinutesText { get => settings.EnglishMinutesText; set => settings.EnglishMinutesText = value; }
        public string EnglishSecondsText { get => settings.EnglishSecondsText; set => settings.EnglishSecondsText = value; }

        public FontFamily CountdownFontFamily { get; set; }
        public int    CountdownFontSize { get => settings.FontSize;         set => settings.FontSize         = value; }
        public Color  NumberColor       { get => settings.NumberColor;      set => settings.NumberColor      = value; }
        public Color  TextColor         { get => settings.TextColor;        set => settings.TextColor        = value; }
        public Color  ProgressBarColor  { get => settings.ProgressBarColor; set => settings.ProgressBarColor = value; }

        public bool   ShowEnglishLine  { get => settings.ShowEnglishLine;  set => settings.ShowEnglishLine  = value; }
        public bool   ShowProgressBar  { get => settings.ShowProgressBar;  set => settings.ShowProgressBar  = value; }
        public bool   ShowProgressText { get => settings.ShowProgressText; set => settings.ShowProgressText = value; }
        public bool   ShowDays         { get => settings.ShowDays;         set => settings.ShowDays         = value; }
        public bool   ShowHours        { get => settings.ShowHours;        set => settings.ShowHours        = value; }
        public bool   ShowMinutes      { get => settings.ShowMinutes;      set => settings.ShowMinutes      = value; }
        public bool   ShowSeconds      { get => settings.ShowSeconds;      set => settings.ShowSeconds      = value; }
        public double OverallOpacity   { get => settings.OverallOpacity;   set => settings.OverallOpacity   = value; }

        public int    PositionPreset   { get => settings.PositionPreset;   set => settings.PositionPreset   = value; }
        public double CustomPositionX  { get => settings.CustomPositionX;  set => settings.CustomPositionX  = value; }
        public double CustomPositionY  { get => settings.CustomPositionY;  set => settings.CustomPositionY  = value; }
        public double PositionOffsetY  { get => settings.PositionOffsetY;  set => settings.PositionOffsetY  = value; }
        public bool   AlwaysOnTop      { get => settings.AlwaysOnTop;      set => settings.AlwaysOnTop      = value; }

        public string GaokaoDateStr    { get => settings.GaokaoDateStr;    set => settings.GaokaoDateStr    = value; }
        public string StartDateStr     { get => settings.StartDateStr;     set => settings.StartDateStr     = value; }
        public int    ProgressDecimalDigits { get => settings.ProgressDecimalDigits; set => settings.ProgressDecimalDigits = value; }
        public bool   EnableAnimations { get => settings.EnableAnimations; set => settings.EnableAnimations = value; }
        public bool   AutoStart
        {
            get => settings.AutoStart;
            set
            {
                settings.AutoStart = value;
                ApplyAutoStart(value);
            }
        }
        public bool   HideWhenMaximized { get => settings.HideWhenMaximized; set => settings.HideWhenMaximized = value; }

        // ── 注册表自启动键名 ─────────────────────────────────
        private const string AutoStartKeyName = "GaokaoCountdown";

        /// <summary>读取当前注册表实际状态（与 settings 可能不同步时以此为准）</summary>
        public static bool GetAutoStartFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue(AutoStartKeyName) != null;
            }
            catch { return false; }
        }

        /// <summary>将自启动状态写入注册表</summary>
        public static void ApplyAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    // 使用当前程序路径，带引号防止路径含空格
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                    key.SetValue(AutoStartKeyName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AutoStartKeyName, throwOnMissingValue: false);
                }
            }
            catch { /* 注册表写入失败静默处理 */ }
        }

        // ── 入场动画 ─────────────────────────────────────────
        private bool _introPlayed = false;  // 只播放一次
        private DispatcherTimer? _introTimer;
        private DateTime _introStart;
        private const double IntroDurationMs = 1250.0;

        // 入场动画时每个数字的目标值
        private int _introDays, _introHours, _introMinutes, _introSeconds;
        private double _introProgress;  // 进度条目标值(0~100)


        // ── 构造函数 ───────────────────────────────────────────
        public MainWindow()
        {
            settings = AppSettings.Load();
            CountdownFontFamily = new FontFamily(settings.FontFamily);
            RefreshDateFields();

            // 启动时以注册表实际状态同步设置（防止手动删除注册表后不一致）
            settings.AutoStart = GetAutoStartFromRegistry();

            InitializeComponent();
            SetupTrayIcon();
            SetupTimer();
            UpdateCountdown();
            PositionWindow();
            UpdateCountdownDisplay();
        }

        public void RefreshDateFields()
        {
            if (!DateTime.TryParse(settings.GaokaoDateStr, out gaokaoDate))
                gaokaoDate = new DateTime(2027, 6, 7, 9, 0, 0);
            if (!DateTime.TryParse(settings.StartDateStr, out startDate))
                startDate = new DateTime(2024, 8, 24);
        }

        // ── 保存 ───────────────────────────────────────────────
        public void SaveSettings()
        {
            settings.FontFamily         = CountdownFontFamily.Source;
            settings.NumberColorHex    = NumberColor.ToString();
            settings.TextColorHex      = TextColor.ToString();
            settings.ProgressBarColorHex = ProgressBarColor.ToString();
            settings.Save();
        }

        // ── 托盘图标 ───────────────────────────────────────────
        private void SetupTrayIcon()
        {
            notifyIcon = new TaskbarIcon();
            notifyIcon.ToolTipText = "高考倒计时";
            var contextMenu = new ContextMenu();
            var showHideItem = new MenuItem { Header = "显示 / 隐藏" };
            showHideItem.Click += (s, e) => ToggleVisibility();
            var settingsItem = new MenuItem { Header = "设置" };
            settingsItem.Click += (s, e) => OpenSettings();
            var exitItem = new MenuItem { Header = "退出" };
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(showHideItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);
            notifyIcon.ContextMenu = contextMenu;
            notifyIcon.TrayMouseDoubleClick += (s, e) => ToggleVisibility();
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible) { Hide(); }
            else { Show(); Activate(); ApplyWindowLayer(); }
        }

        private void OpenSettings()
        {
            // 若设置窗口已打开，则激活而不重复创建
            if (_settingWindow != null && _settingWindow.IsLoaded)
            {
                _settingWindow.Activate();
                if (_settingWindow.WindowState == WindowState.Minimized)
                    _settingWindow.WindowState = WindowState.Normal;
                return;
            }

            _settingWindow = new SettingWindow(this);
            _settingWindow.Owner = this;
            _settingWindow.Closed += (s, e) => _settingWindow = null;
            _settingWindow.Show();  // 非模态，允许与主窗口同时操作
        }

        private void ExitApplication()
        {
            notifyIcon?.Dispose();
            Application.Current.Shutdown();
        }

        // ── 窗口层级 ───────────────────────────────────────────
        public void ApplyWindowLayer()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (AlwaysOnTop)
            {
                Topmost = true;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            else
            {
                Topmost = false;
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
        }

        // ── 定时器 ─────────────────────────────────────────────
        private void SetupTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => UpdateCountdown();
            timer.Start();

            // 最大化检测定时器（每 500ms 检查一次前台窗口状态）
            _maximizeCheckTimer = new DispatcherTimer();
            _maximizeCheckTimer.Interval = TimeSpan.FromMilliseconds(500);
            _maximizeCheckTimer.Tick += MaximizeCheckTimer_Tick;
            _maximizeCheckTimer.Start();
        }

        private void MaximizeCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!HideWhenMaximized) return;

            IntPtr foreground = GetForegroundWindow();
            // 排除本程序自身的窗口
            var myHwnd = new WindowInteropHelper(this).Handle;
            if (foreground == myHwnd || foreground == IntPtr.Zero) return;

            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(foreground, ref placement);
            bool isForegroundMaximized = placement.showCmd == SW_SHOWMAXIMIZED;

            if (isForegroundMaximized && Visibility == Visibility.Visible)
            {
                _hiddenByMaximize = true;
                Hide();
            }
            else if (!isForegroundMaximized && _hiddenByMaximize)
            {
                _hiddenByMaximize = false;
                Show();
                ApplyWindowLayer();
            }
        }

        // ══════════════════════════════════════════════════════
        //  每秒触发：更新倒计时数字 + 动画
        // ══════════════════════════════════════════════════════
        private void UpdateCountdown()
        {
            DateTime now = DateTime.Now;
            TimeSpan timeLeft = gaokaoDate - now;

            int days    = timeLeft.TotalSeconds > 0 ? timeLeft.Days      : 0;
            int hours   = timeLeft.TotalSeconds > 0 ? timeLeft.Hours     : 0;
            int minutes = timeLeft.TotalSeconds > 0 ? timeLeft.Minutes   : 0;
            int seconds = timeLeft.TotalSeconds > 0 ? timeLeft.Seconds   : 0;

            // ── 入场动画进行中：跳过文本更新，等动画结束 ────
            bool introRunning = _introTimer != null;

            if (!introRunning)
            {
                // ── 更新数字文本（中文）─────────────────────────────
                DaysTb.Text    = days.ToString();
                HoursTb.Text   = hours.ToString("00");
                MinutesTb.Text = minutes.ToString("00");
                SecondsTb.Text = seconds.ToString("00");

                // ── 更新数字文本（英文）─────────────────────────────
                DaysEnTb.Text    = days.ToString();
                HoursEnTb.Text   = hours.ToString("00");
                MinutesEnTb.Text = minutes.ToString("00");
                SecondsEnTb.Text = seconds.ToString("00");
            }

            // ── 脉冲动画：仅当值变化时触发（入场动画期间跳过）──
            if (EnableAnimations && !introRunning)
            {
                if (days != _lastDays && ShowDays)       PulseNumber(DaysTb,    true);
                if (hours != _lastHours && ShowHours)    PulseNumber(HoursTb,   true);
                if (minutes != _lastMinutes && ShowMinutes) PulseNumber(MinutesTb, true);
                if (ShowSeconds) PulseNumber(SecondsTb, false);

                if (days != _lastDays && ShowDays)       PulseNumber(DaysEnTb,    false);
                if (hours != _lastHours && ShowHours)    PulseNumber(HoursEnTb,   false);
                if (minutes != _lastMinutes && ShowMinutes) PulseNumber(MinutesEnTb, false);
                if (ShowSeconds) PulseNumber(SecondsEnTb, false);
            }

            _lastDays    = days;
            _lastHours   = hours;
            _lastMinutes = minutes;
            _lastSeconds = seconds;

            if (timeLeft.TotalSeconds <= 0)
                timer?.Stop();

            // ── 进度 ───────────────────────────────────────────────
            double totalDays   = (gaokaoDate - startDate).TotalDays;
            double daysPassed  = (now - startDate).TotalDays;
            double progress    = Math.Min(1, Math.Max(0, daysPassed / totalDays));
            // 入场动画期间不覆盖进度条（进度条正在动画中）
            if (!introRunning)
                ProgressBar.Value = progress * 100;

            string fmt = "F" + ProgressDecimalDigits;
            double pct = progress * 100.0;
            ProgressText.Text   = $"高中生活已过去 {pct.ToString(fmt)}%";
            ProgressTextEn.Text = $"High school life has passed {pct.ToString(fmt)}%.";

            // 字体同步
            ProgressText.FontFamily   = CountdownFontFamily;
            ProgressTextEn.FontFamily = CountdownFontFamily;

            UpdateCountdownDisplay();
        }

        // ══════════════════════════════════════════════════════
        //  数字脉冲动画：缩放 + 透明度（轻量、流畅、不卡 GPU）
        //  去除 DropShadowEffect 动画（BlurRadius 极吃 GPU）
        // ══════════════════════════════════════════════════════
        private void PulseNumber(TextBlock tb, bool isChinese)
        {
            if (tb.RenderTransform is not ScaleTransform st) return;

            // 先停止上一次同属性的动画，避免叠加冲突
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            tb.BeginAnimation(TextBlock.OpacityProperty,  null);

            // ── 缩放：1 → 1.08 → 1（三段关键帧 + SineEase）──
            var scaleAnim = new DoubleAnimationUsingKeyFrames();
            scaleAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1,    TimeSpan.Zero));
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1,    TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            });

            // ── 透明度：1 → 0.72 → 1 ──────────────────────────
            var opAnim = new DoubleAnimationUsingKeyFrames();
            opAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1,    TimeSpan.Zero));
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.72, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            });
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1,    TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            });

            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            tb.BeginAnimation(TextBlock.OpacityProperty,  opAnim);
        }

        // ══════════════════════════════════════════════════════
        //  刷新所有静态显示（文本/颜色/字体/显隐）
        // ══════════════════════════════════════════════════════
        public void UpdateCountdownDisplay()
        {
            // ── 文本内容 ──────────────────────────────────────────
            ChinesePrefixTb.Text = ChinesePrefix;
            ChineseDaysTb.Text   = ChineseDaysText;
            ChineseHoursTb.Text  = ChineseHoursText;
            ChineseMinutesTb.Text = ChineseMinutesText;
            ChineseSecondsTb.Text = ChineseSecondsText;

            EnglishPrefixTb.Text  = EnglishPrefix;
            EnglishDaysTb.Text    = EnglishDaysText;
            EnglishHoursTb.Text   = EnglishHoursText;
            EnglishMinutesTb.Text = EnglishMinutesText;
            EnglishSecondsTb.Text = EnglishSecondsText;

            // ── 颜色刷 ──────────────────────────────────────────
            var textBrush   = new SolidColorBrush(TextColor);
            var numberBrush = new SolidColorBrush(NumberColor);

            ChinesePrefixTb.Foreground  = textBrush;
            ChineseDaysTb.Foreground    = textBrush;
            ChineseHoursTb.Foreground   = textBrush;
            ChineseMinutesTb.Foreground = textBrush;
            ChineseSecondsTb.Foreground = textBrush;

            EnglishPrefixTb.Foreground  = textBrush;
            EnglishDaysTb.Foreground    = textBrush;
            EnglishHoursTb.Foreground   = textBrush;
            EnglishMinutesTb.Foreground = textBrush;
            EnglishSecondsTb.Foreground = textBrush;

            DaysTb.Foreground    = numberBrush;
            HoursTb.Foreground   = numberBrush;
            MinutesTb.Foreground = numberBrush;
            SecondsTb.Foreground = numberBrush;

            DaysEnTb.Foreground    = numberBrush;
            HoursEnTb.Foreground   = numberBrush;
            MinutesEnTb.Foreground = numberBrush;
            SecondsEnTb.Foreground = numberBrush;

            // 发光颜色同步
            if (DaysTb.Effect is DropShadowEffect g1)     g1.Color = NumberColor;
            if (HoursTb.Effect is DropShadowEffect g2)    g2.Color = NumberColor;
            if (MinutesTb.Effect is DropShadowEffect g3)  g3.Color = NumberColor;
            if (SecondsTb.Effect is DropShadowEffect g4)  g4.Color = NumberColor;

            ProgressText.Foreground   = textBrush;
            ProgressTextEn.Foreground = textBrush;

            // ── 进度条颜色 & 发光 ──────────────────────────────
            ProgressBar.Foreground = new SolidColorBrush(ProgressBarColor);
            if (ProgressBar.Effect is DropShadowEffect pg)
                pg.Color = ProgressBarColor;

            // ── 字体与大小 ──────────────────────────────────────
            ChinesePanel.Children.OfType<TextBlock>().ToList().ForEach(tb =>
            {
                tb.FontFamily = CountdownFontFamily;
                if (tb == DaysTb || tb == HoursTb || tb == MinutesTb || tb == SecondsTb)
                    tb.FontSize = CountdownFontSize;
                else
                    tb.FontSize = CountdownFontSize;
            });
            EnglishPanel.Children.OfType<TextBlock>().ToList().ForEach(tb =>
            {
                tb.FontFamily = CountdownFontFamily;
            });
            // 直接设置数字块字号（中文行）
            DaysTb.FontSize    = CountdownFontSize;
            HoursTb.FontSize   = CountdownFontSize;
            MinutesTb.FontSize = CountdownFontSize;
            SecondsTb.FontSize = CountdownFontSize;
            // 文字块字号（中文行）
            ChinesePrefixTb.FontSize = CountdownFontSize;
            ChineseDaysTb.FontSize   = CountdownFontSize;
            ChineseHoursTb.FontSize  = CountdownFontSize;
            ChineseMinutesTb.FontSize = CountdownFontSize;
            ChineseSecondsTb.FontSize = CountdownFontSize;

            // 英文行字号
            double enSize = CountdownFontSize * 0.4;
            DaysEnTb.FontSize    = enSize;
            HoursEnTb.FontSize   = enSize;
            MinutesEnTb.FontSize = enSize;
            SecondsEnTb.FontSize = enSize;
            EnglishPrefixTb.FontSize  = enSize;
            EnglishDaysTb.FontSize    = enSize;
            EnglishHoursTb.FontSize   = enSize;
            EnglishMinutesTb.FontSize = enSize;
            EnglishSecondsTb.FontSize = enSize;

            ProgressText.FontSize   = CountdownFontSize * 0.25;
            ProgressTextEn.FontSize = ProgressText.FontSize * 0.9;
            ProgressText.FontFamily   = CountdownFontFamily;
            ProgressTextEn.FontFamily = CountdownFontFamily;

            // 更新缩放中心（动态字号时居中）
            UpdateScaleCenters();

            // ── 显示 / 隐藏行 ──────────────────────────────────
            ChinesePanel.Visibility = Visibility.Visible;  // 中文行始终可见（现在是用户主要信息）
            EnglishPanel.Visibility = ShowEnglishLine ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.Visibility = ShowProgressBar  ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Visibility    = ShowProgressText ? Visibility.Visible : Visibility.Collapsed;
            ProgressTextEn.Visibility = (ShowProgressText && ShowEnglishLine) ? Visibility.Visible : Visibility.Collapsed;

            // ── 时间部分（天/时/分/秒）可见性 ──────────────────
            // 中文行：数字 + 标签 同步
            var daysVis    = ShowDays    ? Visibility.Visible : Visibility.Collapsed;
            var hoursVis   = ShowHours   ? Visibility.Visible : Visibility.Collapsed;
            var minutesVis = ShowMinutes ? Visibility.Visible : Visibility.Collapsed;
            var secondsVis = ShowSeconds ? Visibility.Visible : Visibility.Collapsed;

            DaysTb.Visibility         = daysVis;
            ChineseDaysTb.Visibility  = daysVis;
            HoursTb.Visibility        = hoursVis;
            ChineseHoursTb.Visibility = hoursVis;
            MinutesTb.Visibility         = minutesVis;
            ChineseMinutesTb.Visibility  = minutesVis;
            SecondsTb.Visibility         = secondsVis;
            ChineseSecondsTb.Visibility  = secondsVis;

            // 英文行：数字 + 标签 同步（英文标签中数字已包含在 TextBlock 前，单独控制）
            DaysEnTb.Visibility       = daysVis;
            EnglishDaysTb.Visibility  = daysVis;
            HoursEnTb.Visibility      = hoursVis;
            EnglishHoursTb.Visibility = hoursVis;
            MinutesEnTb.Visibility       = minutesVis;
            EnglishMinutesTb.Visibility  = minutesVis;
            SecondsEnTb.Visibility       = secondsVis;
            EnglishSecondsTb.Visibility  = secondsVis;

            // ── 透明度 ──────────────────────────────────────────
            this.Opacity = Math.Clamp(OverallOpacity, 0.1, 1.0);

            // ── 窗口尺寸自适应 ──────────────────────────────────
            double scaleFactor = (double)CountdownFontSize / BaseFontSize;
            this.Width  = BaseWindowWidth  * scaleFactor;
            this.Height = BaseWindowHeight * scaleFactor * 1.4;
            ProgressBar.Height = 9 * scaleFactor;

            // ── 重新定位 ──────────────────────────────────────────
            PositionWindow();
        }

        /// <summary>动态更新所有数字 TextBlock 的缩放中心，使其居中</summary>
        private void UpdateScaleCenters()
        {
            foreach (var tb in new[] { DaysTb, HoursTb, MinutesTb, SecondsTb, DaysEnTb, HoursEnTb, MinutesEnTb, SecondsEnTb })
            {
                // 用 ActualWidth/ActualHeight 的一半作为中心
                // 但动画运行时可能 ActualWidth 不准确，用期望字号的一半近似
                double cx = (tb.FontSize) / 2.0;
                double cy = (tb.FontSize) / 2.0;
                if (tb.RenderTransform is ScaleTransform st)
                {
                    st.CenterX = cx;
                    st.CenterY = cy;
                    st.ScaleX = 1;
                    st.ScaleY = 1;
                }
            }
        }

        // ══════════════════════════════════════════════════════
        //  窗口定位
        // ══════════════════════════════════════════════════════
        public void PositionWindow()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            double x, y;

            if (PositionPreset == 5 && CustomPositionX >= 0 && CustomPositionY >= 0)
            {
                Left = CustomPositionX;
                Top  = CustomPositionY;
                return;
            }

            x = (sw - Width) / 2;
            switch (PositionPreset)
            {
                case 0: y = 10; break;
                case 1: y = sh / 25.0; break;
                case 2: y = (sh - Height) / 2; break;
                case 3: y = sh * 0.65; break;
                case 4: y = sh - Height - 40; break;
                default: y = sh / 25.0; break;
            }
            Left = x;
            Top  = y + PositionOffsetY;
        }

        // ══════════════════════════════════════════════════════
        //  事件
        // ══════════════════════════════════════════════════════
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWindowLayer();
            if (EnableAnimations && !_introPlayed)
                PlayIntroAnimation();
        }

        // ══════════════════════════════════════════════════════
        //  入场动画：数字 0→实际值滚动 + 进度条 0→当前值
        //  持续 1250ms，PowerEaseOut(Power=8) 强力先快后慢
        // ══════════════════════════════════════════════════════
        private void PlayIntroAnimation()
        {
            _introPlayed = true;

            // 记录当前真实目标值
            DateTime now = DateTime.Now;
            TimeSpan timeLeft = gaokaoDate - now;
            _introDays    = timeLeft.TotalSeconds > 0 ? timeLeft.Days    : 0;
            _introHours   = timeLeft.TotalSeconds > 0 ? timeLeft.Hours   : 0;
            _introMinutes = timeLeft.TotalSeconds > 0 ? timeLeft.Minutes : 0;
            _introSeconds = timeLeft.TotalSeconds > 0 ? timeLeft.Seconds : 0;

            double totalDays  = (gaokaoDate - startDate).TotalDays;
            double daysPassed = (now - startDate).TotalDays;
            _introProgress = Math.Min(100, Math.Max(0, daysPassed / totalDays * 100.0));

            // ── 进度条动画：0 → 当前值，1.25s 强力缓出 ──────────
            ProgressBar.Value = 0;
            var pbAnim = new DoubleAnimation(0, _introProgress,
                new Duration(TimeSpan.FromMilliseconds(IntroDurationMs)))
            {
                EasingFunction = new PowerEase { Power = 8, EasingMode = EasingMode.EaseOut }
            };
            ProgressBar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, pbAnim);

            // ── 数字滚动：用 DispatcherTimer 逐帧更新文本 ──────
            _introStart = DateTime.Now;
            _introTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)  // ~60fps
            };
            _introTimer.Tick += IntroTimer_Tick;
            _introTimer.Start();
        }

        private void IntroTimer_Tick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _introStart).TotalMilliseconds;
            double t = Math.Min(1.0, elapsed / IntroDurationMs);

            // PowerEaseOut (Power=8): 1 - (1-t)^8，先快后慢更明显
            double eased = 1.0 - Math.Pow(1.0 - t, 8);

            int days    = (int)Math.Round(eased * _introDays);
            int hours   = (int)Math.Round(eased * _introHours);
            int minutes = (int)Math.Round(eased * _introMinutes);
            int seconds = (int)Math.Round(eased * _introSeconds);

            DaysTb.Text    = days.ToString();
            HoursTb.Text   = hours.ToString("00");
            MinutesTb.Text = minutes.ToString("00");
            SecondsTb.Text = seconds.ToString("00");

            DaysEnTb.Text    = days.ToString();
            HoursEnTb.Text   = hours.ToString("00");
            MinutesEnTb.Text = minutes.ToString("00");
            SecondsEnTb.Text = seconds.ToString("00");

            if (t >= 1.0)
            {
                // 动画结束，确保最终值精确
                DaysTb.Text    = _introDays.ToString();
                HoursTb.Text   = _introHours.ToString("00");
                MinutesTb.Text = _introMinutes.ToString("00");
                SecondsTb.Text = _introSeconds.ToString("00");

                DaysEnTb.Text    = _introDays.ToString();
                HoursEnTb.Text   = _introHours.ToString("00");
                MinutesEnTb.Text = _introMinutes.ToString("00");
                SecondsEnTb.Text = _introSeconds.ToString("00");

                _introTimer!.Stop();
                _introTimer = null;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
