using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        private MenuItem? _trayScheduleItem; // 托盘"课表栏"菜单项引用
        private AppSettings settings;

        // ── 缓存的画刷（颜色变更时重建，避免每秒 new）───────────
        private SolidColorBrush _textBrushCache = new SolidColorBrush(Colors.White);
        private SolidColorBrush _numberBrushCache = new SolidColorBrush(Colors.Red);
        private SolidColorBrush _progressBrushCache = new SolidColorBrush(Colors.White);

        // ── 动态日期 ───────────────────────────────────────────
        private DateTime gaokaoDate;
        private DateTime startDate;

        // ── Win32 API ─────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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

        // 窗口扩展样式（点击穿透）
        private const int GWL_EXSTYLE      = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOMOVE       = 0x0002;
        private const uint SWP_NOSIZE       = 0x0001;
        private const uint SWP_NOACTIVATE   = 0x0010;

        // 基准尺寸
        private const int BaseFontSize     = 40;
        private const int BaseWindowWidth  = 850;
        private const int BaseWindowHeight = 175;

        // ── 上次 tick 的值（用于判断是否需要脉冲动画） ──
        private int _lastDays, _lastHours, _lastMinutes, _lastSeconds;

        // ── 最大化检测：记录上次隐藏状态，避免重复操作 ──
        private bool _hiddenByMaximize = false;
        private bool _hiddenByScheduleOrExam = false; // 因上课/考试而隐藏
        private DispatcherTimer? _maximizeCheckTimer;
        private bool _isPositioning = false;   // 程序化定位中，抑制 LocationChanged 回写
        private bool _clickThroughEnabled = false;  // 当前点击穿透状态

        // ── 设置窗口单例引用 ─────────────────────────────────
        private SettingWindow? _settingWindow;
        private bool _isOpeningSettings;  // 重入防护

        // ── 每日一言 ─────────────────────────────────────────
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        private DispatcherTimer? _quoteRefreshTimer;

        // ── 课表 & 提醒 & 考试模式 ───────────────────────────
        private ScheduleManager?   _scheduleManager;
        private ReminderService?   _reminderService;
        private ScheduleBarWindow? _scheduleBarWindow;
        private ExamModeWindow?    _examModeWindow;

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

        public int    PositionPreset
        {
            get => settings.PositionPreset;
            set {
                settings.PositionPreset = value;
                ApplyClickThrough();  // 预设模式 → 穿透；自定义 → 可交互
            }
        }
        public double CustomPositionX  { get => settings.CustomPositionX;  set => settings.CustomPositionX  = value; }
        public double CustomPositionY  { get => settings.CustomPositionY;  set => settings.CustomPositionY  = value; }
        public double PositionOffsetY  { get => settings.PositionOffsetY;  set => settings.PositionOffsetY  = value; }
        public bool   AlwaysOnTop      { get => settings.AlwaysOnTop;      set => settings.AlwaysOnTop      = value; }

        public string GaokaoDateStr    { get => settings.GaokaoDateStr;    set => settings.GaokaoDateStr    = value; }
        public string StartDateStr     { get => settings.StartDateStr;     set => settings.StartDateStr     = value; }
        public int    ProgressDecimalDigits { get => settings.ProgressDecimalDigits; set => settings.ProgressDecimalDigits = value; }
        public bool   EnableAnimations { get => settings.EnableAnimations; set => settings.EnableAnimations = value; }
        public bool   ShowDailyQuote            { get => settings.ShowDailyQuote;            set => settings.ShowDailyQuote            = value; }
        public double QuoteFontSize             { get => settings.QuoteFontSize;             set => settings.QuoteFontSize             = value; }
        public string QuoteForegroundHex        { get => settings.QuoteForegroundHex;        set => settings.QuoteForegroundHex        = value; }
        public bool   QuoteItalic               { get => settings.QuoteItalic;               set => settings.QuoteItalic               = value; }
        public string QuoteApiUrl               { get => settings.QuoteApiUrl;               set => settings.QuoteApiUrl               = value; }
        public int    QuoteAutoRefreshInterval   { get => settings.QuoteAutoRefreshInterval;   set => settings.QuoteAutoRefreshInterval   = value; }
        public string QuoteTextFieldName         { get => settings.QuoteTextFieldName;         set => settings.QuoteTextFieldName         = value; }

        public string WeatherCity            { get => settings.WeatherCity;            set => settings.WeatherCity            = value; }
        public string WeatherAdcode          { get => settings.WeatherAdcode;          set => settings.WeatherAdcode          = value; }
        public int    WeatherRefreshInterval { get => settings.WeatherRefreshInterval; set => settings.WeatherRefreshInterval = value; }
        public double WeatherFontSize        { get => settings.WeatherFontSize;        set => settings.WeatherFontSize        = value; }
        public string WeatherCityColor        { get => settings.WeatherCityColor;        set => settings.WeatherCityColor        = value; }
        public string WeatherInfoColor        { get => settings.WeatherInfoColor;        set => settings.WeatherInfoColor        = value; }
        public string WeatherTempColor        { get => settings.WeatherTempColor;        set => settings.WeatherTempColor        = value; }
        public string WeatherTimeColor        { get => settings.WeatherTimeColor;        set => settings.WeatherTimeColor        = value; }
        public string WeatherIconColor        { get => settings.WeatherIconColor;        set => settings.WeatherIconColor        = value; }
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
        public bool   HideDuringClass { get => settings.HideDuringClass; set => settings.HideDuringClass = value; }

        // ── 课表栏 & 提醒 & 考试模式代理属性 ─────────────────
        public bool   ShowScheduleBar         { get => settings.ShowScheduleBar;         set => settings.ShowScheduleBar         = value; }
        public double ScheduleBarOpacity      { get => settings.ScheduleBarOpacity;      set => settings.ScheduleBarOpacity      = value; }
        public bool   ScheduleBarAlwaysOnTop  { get => settings.ScheduleBarAlwaysOnTop;  set => settings.ScheduleBarAlwaysOnTop  = value; }
        public bool   ScheduleBarClickThrough { get => settings.ScheduleBarClickThrough; set => settings.ScheduleBarClickThrough = value; }
        public double ScheduleBarWidth        { get => settings.ScheduleBarWidth;        set => settings.ScheduleBarWidth        = value; }
        public bool   ScheduleBarAutoCollapse  { get => settings.ScheduleBarAutoCollapse;  set => settings.ScheduleBarAutoCollapse  = value; }
        public double ScheduleBarFontSize     { get => settings.ScheduleBarFontSize;     set => settings.ScheduleBarFontSize     = value; }
        public bool   EnableReminderSound     { get => settings.EnableReminderSound;     set => settings.EnableReminderSound     = value; }
        public string ReminderSoundPath       { get => settings.ReminderSoundPath;       set => settings.ReminderSoundPath       = value; }
        public bool   RemindClassStart        { get => settings.RemindClassStart;        set => settings.RemindClassStart        = value; }
        public bool   RemindClassMid          { get => settings.RemindClassMid;          set => settings.RemindClassMid          = value; }
        public bool   RemindClassEndSoon      { get => settings.RemindClassEndSoon;      set => settings.RemindClassEndSoon      = value; }
        public bool   RemindClassEnd          { get => settings.RemindClassEnd;          set => settings.RemindClassEnd          = value; }
        public bool   RemindNextClassSoon     { get => settings.RemindNextClassSoon;     set => settings.RemindNextClassSoon     = value; }
        public bool   RemindDayEnd            { get => settings.RemindDayEnd;            set => settings.RemindDayEnd            = value; }
        public bool   RemindSpecialPeriod     { get => settings.RemindSpecialPeriod;     set => settings.RemindSpecialPeriod     = value; }
        public bool   EnableExamMode          { get => settings.EnableExamMode;          set => settings.EnableExamMode          = value; }
        public bool   AutoEnterExamMode       { get => settings.AutoEnterExamMode;       set => settings.AutoEnterExamMode       = value; }
        public double ExamModeFontSize        { get => settings.ExamModeFontSize;        set => settings.ExamModeFontSize        = value; }
        // ── 考试模式样式代理 ──────────────────────────────
        public double ExamSubjectFontSize       { get => settings.ExamSubjectFontSize;       set => settings.ExamSubjectFontSize       = value; }
        public double ExamNameFontSize          { get => settings.ExamNameFontSize;          set => settings.ExamNameFontSize          = value; }
        public double ExamCountdownFontSize     { get => settings.ExamCountdownFontSize;     set => settings.ExamCountdownFontSize     = value; }
        public double ExamTimeInfoFontSize      { get => settings.ExamTimeInfoFontSize;      set => settings.ExamTimeInfoFontSize      = value; }
        public double ExamNextSubjectFontSize   { get => settings.ExamNextSubjectFontSize;   set => settings.ExamNextSubjectFontSize   = value; }
        public double ExamWarningFontSize       { get => settings.ExamWarningFontSize;       set => settings.ExamWarningFontSize       = value; }
        public double ExamEscHintFontSize       { get => settings.ExamEscHintFontSize;       set => settings.ExamEscHintFontSize       = value; }
        public double ExamProgressBarHeight     { get => settings.ExamProgressBarHeight;     set => settings.ExamProgressBarHeight     = value; }
        public string ExamSubjectColor          { get => settings.ExamSubjectColor;          set => settings.ExamSubjectColor          = value; }
        public string ExamNameColor             { get => settings.ExamNameColor;             set => settings.ExamNameColor             = value; }
        public string ExamCountdownNormalColor  { get => settings.ExamCountdownNormalColor;  set => settings.ExamCountdownNormalColor  = value; }
        public string ExamCountdownWarningColor { get => settings.ExamCountdownWarningColor; set => settings.ExamCountdownWarningColor = value; }
        public string ExamCountdownCriticalColor{ get => settings.ExamCountdownCriticalColor;set => settings.ExamCountdownCriticalColor= value; }
        public string ExamDistanceColor         { get => settings.ExamDistanceColor;         set => settings.ExamDistanceColor         = value; }
        public string ExamInfoColor             { get => settings.ExamInfoColor;             set => settings.ExamInfoColor             = value; }
        public string ExamProgressBarColor      { get => settings.ExamProgressBarColor;      set => settings.ExamProgressBarColor      = value; }
        public string ExamProgressBarBgColor    { get => settings.ExamProgressBarBgColor;    set => settings.ExamProgressBarBgColor    = value; }
        public string ExamBackgroundColor       { get => settings.ExamBackgroundColor;       set => settings.ExamBackgroundColor       = value; }
        public string ExamNextSubjectColor      { get => settings.ExamNextSubjectColor;      set => settings.ExamNextSubjectColor      = value; }
        public string ExamWarningColor          { get => settings.ExamWarningColor;          set => settings.ExamWarningColor          = value; }
        public string ExamProgressPctColor      { get => settings.ExamProgressPctColor;      set => settings.ExamProgressPctColor      = value; }
        public string ExamCountdownFontFamily   { get => settings.ExamCountdownFontFamily;   set => settings.ExamCountdownFontFamily   = value; }
        public string ExamInfoDimColor          { get => settings.ExamInfoDimColor;          set => settings.ExamInfoDimColor          = value; }

        /// <summary>应用考试模式窗口样式（若已打开）</summary>
        public void ApplyExamModeStyle()
        {
            _examModeWindow?.ApplyAllSettings(settings);
        }

        /// <summary>供设置窗口访问课表管理器</summary>
        public ScheduleManager? GetScheduleManager() => _scheduleManager;

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
            SetupScheduleServices();
            SetupTimer();
            UpdateCountdown();
            PositionWindow();
            UpdateCountdownDisplay();

            // 拖动窗口时实时同步坐标到 settings
            LocationChanged += Window_LocationChanged;
        }

        // ── 课表服务初始化 ─────────────────────────────────────
        private void SetupScheduleServices()
        {
            _scheduleManager = new ScheduleManager();
            _reminderService = new ReminderService(_scheduleManager, settings);

            // 订阅提醒事件
            _reminderService.Reminder += OnReminder;

            if (settings.EnableExamMode || settings.RemindClassStart || settings.ShowScheduleBar)
                _reminderService.Start();

            // 初始化课表栏
            if (settings.ShowScheduleBar)
                ShowScheduleBarWindow();

            // 当天有考试且开启自动进入时，延迟 2 秒进入
            if (settings.AutoEnterExamMode && settings.EnableExamMode)
            {
                var todayExams = _scheduleManager.GetTodayExams();
                if (todayExams.Count > 0)
                {
                    var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    delay.Tick += (s, e) => { delay.Stop(); EnterExamMode(); };
                    delay.Start();
                }
            }
        }

        private void OnReminder(object? sender, ReminderEventArgs e)
        {
            // 自定义提醒窗口（右下角滑入）
            ReminderWindow.Show(e.Title, e.Message, e.Type);

            // 触发课表栏临时展开（提醒时显示完整信息）
            _scheduleBarWindow?.ExpandOnReminder(e.Type);
        }

        // ── 课表栏窗口管理 ────────────────────────────────────
        private void ShowScheduleBarWindow()
        {
            if (_scheduleBarWindow != null) return;
            if (_scheduleManager == null || _reminderService == null) return;
            _scheduleBarWindow = new ScheduleBarWindow(settings, _scheduleManager, _reminderService);
            _scheduleBarWindow.Closed += (s, e) => { _scheduleBarWindow = null; SyncTrayMenu(); };
            _scheduleBarWindow.Show();
            SyncTrayMenu();
        }

        private void HideScheduleBarWindow()
        {
            _scheduleBarWindow?.Close();
            _scheduleBarWindow = null;
            SyncTrayMenu();
        }

        private void SyncTrayMenu()
        {
            if (_trayScheduleItem != null)
                _trayScheduleItem.Header = _scheduleBarWindow != null ? "课表栏 ✓" : "课表栏";
        }

        /// <summary>设置窗口应用设置后调用，刷新课表栏状态</summary>
        public void ApplyScheduleBarSettings()
        {
            // 重启提醒服务（开关可能变化）
            _reminderService?.Stop();
            if (settings.EnableExamMode || settings.RemindClassStart ||
                settings.ShowScheduleBar || settings.RemindClassEnd)
                _reminderService?.Start();

            if (settings.ShowScheduleBar)
            {
                if (_scheduleBarWindow == null)
                    ShowScheduleBarWindow();
                else
                {
                    _scheduleBarWindow.ApplySettings();
                    _scheduleBarWindow.ApplyFontSizes();
                }
            }
            else
            {
                HideScheduleBarWindow();
            }
        }

        // ── 考试模式 ──────────────────────────────────────────
        public void EnterExamMode()
        {
            if (_examModeWindow != null) { _examModeWindow.Activate(); return; }
            if (_scheduleManager == null) return;

            // 检查今天是否有考试
            var todayExams = _scheduleManager.GetTodayExams();
            if (todayExams.Count == 0)
            {
                System.Windows.MessageBox.Show("今天没有安排考试。", "考试模式",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // 检查当前是否在考试时段内（正在考或有下一场未考）
            var now = DateTime.Now;
            var cur  = _scheduleManager.GetCurrentExamSubject(now);
            var next = _scheduleManager.GetNextExamSubject(now);
            if (cur == null && next == null)
            {
                System.Windows.MessageBox.Show("今天的考试已全部结束。", "考试模式",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // 进入考试模式时隐藏课表栏
            if (settings.ShowScheduleBar)
                HideScheduleBarWindow();
            _examModeWindow = new ExamModeWindow(_scheduleManager, settings);
            _examModeWindow.Closed += (s, e) =>
            {
                _examModeWindow = null;
                // 考试窗口关闭时恢复课表栏
                if (settings.ShowScheduleBar && _scheduleBarWindow == null)
                    ShowScheduleBarWindow();
            };
            _examModeWindow.Show();
        }

        public void ExitExamMode()
        {
            _examModeWindow?.Close();
            _examModeWindow = null;
            // 退出考试模式时恢复课表栏
            if (settings.ShowScheduleBar)
                ShowScheduleBarWindow();
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
            var iconPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            notifyIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri(iconPath));
            notifyIcon.ToolTipText = "学程";
            var contextMenu = new ContextMenu();
            var showHideItem = new MenuItem { Header = "显示 / 隐藏" };
            showHideItem.Click += (s, e) => ToggleVisibility();
            var scheduleBarItem = new MenuItem { Header = "课表栏" };
            _trayScheduleItem = scheduleBarItem;
            scheduleBarItem.Click += (s, e) =>
            {
                if (_scheduleBarWindow != null) HideScheduleBarWindow();
                else ShowScheduleBarWindow();
            };
            var examModeItem = new MenuItem { Header = "考试模式" };
            examModeItem.Click += (s, e) => EnterExamMode();
            var settingsItem = new MenuItem { Header = "设置" };
            settingsItem.Click += (s, e) => OpenSettings();
            var exitItem = new MenuItem { Header = "退出" };
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(showHideItem);
            contextMenu.Items.Add(scheduleBarItem);
            contextMenu.Items.Add(examModeItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);
            notifyIcon.ContextMenu = contextMenu;
            notifyIcon.TrayMouseDoubleClick += (s, e) => ToggleVisibility();
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible) { Hide(); }
            else { Show(); Activate(); ApplyWindowLayer(); if (EnableAnimations) PlayIntroAnimation(); }
        }

        private void OpenSettings()
        {
            // 重入防护：若正在打开过程中，忽略重复点击
            if (_isOpeningSettings) return;
            _isOpeningSettings = true;

            try
            {
                // 若设置窗口已打开（或正在创建中），则激活而不重复创建
                // 注意：不能使用 IsLoaded 判断 — Show() 后到实际加载完成之间 IsLoaded=false，
                //       此时快速连续点击会突破守卫创建多个实例导致崩溃。
                if (_settingWindow != null)
                {
                    try
                    {
                        _settingWindow.Activate();
                        if (_settingWindow.WindowState == WindowState.Minimized)
                            _settingWindow.WindowState = WindowState.Normal;
                    }
                    catch
                    {
                        // 窗口可能正在关闭中，重置引用后重新创建
                        _settingWindow = null;
                    }
                    if (_settingWindow != null) return;
                }

                _settingWindow = new SettingWindow(this);
                _settingWindow.Owner = this;
                _settingWindow.Closed += (s, e) =>
                {
                    _settingWindow = null;
                    _isOpeningSettings = false;
                };
                _settingWindow.Closing += (s, e) =>
                {
                    // 窗口开始关闭时立即从主窗口引用中移除，
                    // 防止在关闭动画期间被重新激活（Activate 在关闭中会抛异常）
                    _settingWindow = null;
                };
                _settingWindow.Show();  // 非模态，允许与主窗口同时操作
            }
            finally
            {
                // 若异常导致窗口未创建，解除锁
                if (_settingWindow == null)
                    _isOpeningSettings = false;
            }
        }

        // ── 窗口层级 ───────────────────────────────────────────
        public void ApplyWindowLayer()
        {
            Topmost = AlwaysOnTop;
        }

        /// <summary>预设模式下启用点击穿透（WS_EX_TRANSPARENT），自定义模式下可正常交互</summary>
        private void ApplyClickThrough()
        {
            bool shouldEnable = PositionPreset != 5;  // 非自定义模式 → 穿透
            if (_clickThroughEnabled == shouldEnable) return;  // 状态未变，跳过

            _clickThroughEnabled = shouldEnable;

            if (!IsLoaded) return;  // 窗口句柄尚未创建

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (shouldEnable)
                exStyle |= WS_EX_TRANSPARENT;
            else
                exStyle &= ~WS_EX_TRANSPARENT;

            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            // 刷新窗口框架使扩展样式生效
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>让进度条宽度匹配中文倒计时文字的实际渲染宽度</summary>
        private void SyncProgressBarWidth()
        {
            ChinesePanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            ProgressBar.Width = ChinesePanel.DesiredSize.Width;
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
                Opacity = 0;
                Show();
                ApplyWindowLayer();
                var fadeIn = new DoubleAnimation(0, Math.Clamp(OverallOpacity, 0.1, 1.0),
                    TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeIn.Completed += (_, _) =>
                {
                    if (EnableAnimations) PlayIntroAnimation();
                };
                BeginAnimation(OpacityProperty, fadeIn);
            }
        }

        // ══════════════════════════════════════════════════════
        //  每秒触发：更新倒计时数字 + 动画
        // ══════════════════════════════════════════════════════
        private void UpdateCountdown()
        {
            // ── 上课期间隐藏主窗口（可设置）──
            bool isInClass   = settings.HideDuringClass && _scheduleManager?.GetCurrentEntry(DateTime.Now) != null;
            bool isInExam    = _examModeWindow != null;
            bool shouldHide  = isInClass || isInExam;

            if (shouldHide)
            {
                if (Visibility == Visibility.Visible)
                {
                    _hiddenByScheduleOrExam = true;
                    Hide();
                }
                return; // 不更新 UI，不请求 API
            }
            else if (_hiddenByScheduleOrExam)
            {
                _hiddenByScheduleOrExam = false;
                Opacity = 0;
                Show();
                var fadeIn = new DoubleAnimation(0, Math.Clamp(OverallOpacity, 0.1, 1.0),
                    TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeIn.Completed += (_, _) =>
                {
                    if (EnableAnimations) PlayIntroAnimation();
                };
                BeginAnimation(OpacityProperty, fadeIn);
            }

            // 如果是被最大化窗口压下去的，也不做 UI 更新
            if (_hiddenByMaximize) return;

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
            {
                // 平滑过渡（仅在启用动画时）
                if (EnableAnimations)
                {
                    var pbAnim = new DoubleAnimation(progress * 100, TimeSpan.FromMilliseconds(600))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    ProgressBar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, pbAnim);
                }
                else
                {
                    ProgressBar.Value = progress * 100;
                }
            }

            string fmt = "F" + ProgressDecimalDigits;
            double pct = progress * 100.0;
            ProgressText.Text   = $"高中生活已过去 {pct.ToString(fmt)}%";
            ProgressTextEn.Text = $"High school life has passed {pct.ToString(fmt)}%.";
            SyncProgressBarWidth();

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

            // ── 颜色刷（仅颜色变更时重建）─────────────────────────
            if (_textBrushCache.Color != TextColor)
                _textBrushCache = new SolidColorBrush(TextColor);
            if (_numberBrushCache.Color != NumberColor)
                _numberBrushCache = new SolidColorBrush(NumberColor);

            ChinesePrefixTb.Foreground  = _textBrushCache;
            ChineseDaysTb.Foreground    = _textBrushCache;
            ChineseHoursTb.Foreground   = _textBrushCache;
            ChineseMinutesTb.Foreground = _textBrushCache;
            ChineseSecondsTb.Foreground = _textBrushCache;

            EnglishPrefixTb.Foreground  = _textBrushCache;
            EnglishDaysTb.Foreground    = _textBrushCache;
            EnglishHoursTb.Foreground   = _textBrushCache;
            EnglishMinutesTb.Foreground = _textBrushCache;
            EnglishSecondsTb.Foreground = _textBrushCache;

            DaysTb.Foreground    = _numberBrushCache;
            HoursTb.Foreground   = _numberBrushCache;
            MinutesTb.Foreground = _numberBrushCache;
            SecondsTb.Foreground = _numberBrushCache;

            DaysEnTb.Foreground    = _numberBrushCache;
            HoursEnTb.Foreground   = _numberBrushCache;
            MinutesEnTb.Foreground = _numberBrushCache;
            SecondsEnTb.Foreground = _numberBrushCache;

            // 发光颜色同步
            if (DaysTb.Effect is DropShadowEffect g1)     g1.Color = NumberColor;
            if (HoursTb.Effect is DropShadowEffect g2)    g2.Color = NumberColor;
            if (MinutesTb.Effect is DropShadowEffect g3)  g3.Color = NumberColor;
            if (SecondsTb.Effect is DropShadowEffect g4)  g4.Color = NumberColor;

            ProgressText.Foreground   = _textBrushCache;
            ProgressTextEn.Foreground = _textBrushCache;

            // ── 进度条颜色 & 发光 ──────────────────────────────
            if (_progressBrushCache.Color != ProgressBarColor)
                _progressBrushCache = new SolidColorBrush(ProgressBarColor);
            ProgressBar.Foreground = _progressBrushCache;
            if (ProgressBar.Effect is DropShadowEffect pg)
                pg.Color = ProgressBarColor;

            // ── 字体族（统一设置）─────────────────────────────────
            ChinesePanel.Children.OfType<TextBlock>().ToList().ForEach(tb =>
                tb.FontFamily = CountdownFontFamily);
            EnglishPanel.Children.OfType<TextBlock>().ToList().ForEach(tb =>
                tb.FontFamily = CountdownFontFamily);
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

            SyncProgressBarWidth();

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

            _isPositioning = true;

            if (PositionPreset == 5 && CustomPositionX >= 0 && CustomPositionY >= 0)
            {
                Left = CustomPositionX;
                Top  = CustomPositionY;
                _isPositioning = false;
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

            _isPositioning = false;
        }

        // ══════════════════════════════════════════════════════
        //  事件
        // ══════════════════════════════════════════════════════
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWindowLayer();
            ApplyClickThrough();  // 根据当前预设模式设置点击穿透
            Activate();  // 确保启动时窗口可见（不被其他窗口遮挡）
            if (EnableAnimations)
                PlayIntroAnimation();
            // 异步加载每日一言（fire-and-forget）
            if (ShowDailyQuote)
            {
                _ = LoadDailyQuoteAsync();
                StartQuoteRefreshTimer();
            }
        }

        /// <summary>自定义模式下拖动窗口；预设模式下点击穿透，不响应拖动</summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 预设模式下不可拖动（点击穿透生效，此事件不应触发；但以防万一再次判断）
            if (PositionPreset != 5) return;

            DragMove();
        }

        /// <summary>拖动窗口时实时同步坐标到 settings，设置页中可实时看到</summary>
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_isPositioning) return;
            // 只在自定义模式（preset=5）时回写坐标
            if (PositionPreset != 5) return;
            CustomPositionX = Left;
            CustomPositionY = Top;
        }

        // ══════════════════════════════════════════════════════
        //  入场动画：数字 0→实际值滚动 + 进度条 0→当前值
        //  持续 1250ms，PowerEaseOut(Power=5) 先快后慢适中
        // ══════════════════════════════════════════════════════
        private void PlayIntroAnimation()
        {
            // 若已有动画在运行，先停止
            if (_introTimer != null)
            {
                _introTimer.Stop();
                _introTimer = null;
            }

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

            // ── 进度条动画：0 → 当前值，1.25s 缓出 ──────────
            ProgressBar.Value = 0;
            var pbAnim = new DoubleAnimation(0, _introProgress,
                new Duration(TimeSpan.FromMilliseconds(IntroDurationMs)))
            {
                EasingFunction = new PowerEase { Power = 5, EasingMode = EasingMode.EaseOut }
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

            // PowerEaseOut (Power=5): 1 - (1-t)^5，先快后慢适中
            double eased = 1.0 - Math.Pow(1.0 - t, 5);

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

        private bool _isExiting;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 应用退出时直接放行（ExitApplication 已处理）
            if (_isExiting) return;
            e.Cancel = true;

            // 淡出动画后隐藏
            var fadeOut = new DoubleAnimation(Math.Clamp(OverallOpacity, 0.1, 1.0), 0,
                TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => Hide();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _maximizeCheckTimer?.Stop();
            _maximizeCheckTimer = null;
            _quoteRefreshTimer?.Stop();
            _quoteRefreshTimer = null;
            _reminderService?.Dispose();
            HideScheduleBarWindow();
            ExitExamMode();
            notifyIcon?.Dispose();
            Application.Current.Shutdown();
        }

        // ══════════════════════════════════════════════════════
        //  每日一言 API
        // ══════════════════════════════════════════════════════

        /// <summary>从 SettingWindow 调用的公开刷新方法</summary>
        public async Task RefreshQuoteAsync()
        {
            await LoadDailyQuoteAsync();
        }

        /// <summary>调用 API 加载一言，并应用当前样式设置、淡入动画</summary>
        private async Task LoadDailyQuoteAsync()
        {
            // 窗口隐藏时（上课/考试中）不请求 API
            if (Visibility != Visibility.Visible || !ShowDailyQuote) return;
            try
            {
                string url = string.IsNullOrWhiteSpace(QuoteApiUrl)
                    ? "https://uapis.cn/api/v1/saying" : QuoteApiUrl;
                var json = await _httpClient.GetStringAsync(url);

                // 使用动态字段名解析 JSON（支持自定义 API）
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string fieldName = string.IsNullOrWhiteSpace(QuoteTextFieldName)
                    ? "text" : QuoteTextFieldName.Trim();
                string? quoteText = root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString() : null;
                if (string.IsNullOrWhiteSpace(quoteText)) return;

                string text = $"「{quoteText.Trim()}」";

                await Dispatcher.InvokeAsync(() =>
                {
                    // 应用当前样式设置
                    ApplyQuoteStyle();
                    DailyQuoteTb.Text = text;
                    // 淡入动画
                    var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8))
                    {
                        EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut }
                    };
                    DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, anim);
                    DailyQuoteTb.Visibility = Visibility.Visible;
                });
            }
            catch
            {
                // 网络异常时静默处理
            }
        }

        /// <summary>将当前设置中的字体大小、颜色、斜体应用到 DailyQuoteTb</summary>
        public void ApplyQuoteStyle()
        {
            DailyQuoteTb.FontSize = QuoteFontSize;
            DailyQuoteTb.FontStyle = QuoteItalic ? FontStyles.Italic : FontStyles.Normal;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(QuoteForegroundHex);
                DailyQuoteTb.Foreground = new SolidColorBrush(c);
            }
            catch { }
        }

        /// <summary>启动/重启每日一言自动切换定时器</summary>
        public void StartQuoteRefreshTimer()
        {
            _quoteRefreshTimer?.Stop();
            _quoteRefreshTimer = null;

            if (!ShowDailyQuote) return;
            int intervalSec = QuoteAutoRefreshInterval;
            if (intervalSec <= 0) return;

            _quoteRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(intervalSec)
            };
            _quoteRefreshTimer.Tick += async (_, _) =>
            {
                // 窗口隐藏时不刷新
                if (Visibility != Visibility.Visible) return;
                // 淡出 → 重新加载 → 淡入
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4))
                {
                    EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += async (_, _) => await LoadDailyQuoteAsync();
                DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            _quoteRefreshTimer.Start();
        }

        private async void DailyQuoteTb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 点击刷新：先淡出再重新加载
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += async (_, _) => await LoadDailyQuoteAsync();
            DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
