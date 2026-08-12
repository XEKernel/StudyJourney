using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
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
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using GaokaoCountdown.Helpers;
using Hardcodet.Wpf.TaskbarNotification;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
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
        private string? _cachedHideSubjects; // 缓存 settings.HideSubjects 字符串
        private HashSet<string> _cachedHiddenSet = new(StringComparer.OrdinalIgnoreCase); // 缓存解析结果
        private string? _lastFontFamily; // 缓存字体族，避免每秒重复设置
        private List<TextBlock>? _cachedChineseTextBlocks; // 缓存中文面板 TextBlock 列表
        private List<TextBlock>? _cachedEnglishTextBlocks; // 缓存英文面板 TextBlock 列表
        private DispatcherTimer? _classEndRestoreTimer; // 下课后延迟恢复计时器
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

        /// <summary>MVVM 数据源（绑定主窗口数字/进度/自定义倒计时/一言）</summary>
        public ViewModels.MainWindowViewModel? ViewModel { get; private set; }

        public FontFamily CountdownFontFamily { get; set; }
        public int    PositionPreset
        {
            get => settings.PositionPreset;
            set {
                settings.PositionPreset = value;
                ApplyClickThrough();  // 预设模式 → 穿透；自定义 → 可交互
            }
        }
        public bool   AutoStart
        {
            get => settings.AutoStart;
            set
            {
                settings.AutoStart = value;
                ApplyAutoStart(value);
            }
        }

        /// <summary>应用考试模式窗口样式（若已打开）</summary>
        public void ApplyExamModeStyle()
        {
            _examModeWindow?.ApplyAllSettings(settings);
        }

        /// <summary>供设置窗口访问课表管理器</summary>
        public ScheduleManager? GetScheduleManager() => _scheduleManager;

        /// <summary>供设置窗口直接读写设置模型（替代大量代理属性）</summary>
        public AppSettings GetSettings() => settings;

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
                    string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
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
            // 加载配置（损坏时备份并提示用户）
            try { settings = AppSettings.Load(); }
            catch (Exception ex)
            {
                settings = new AppSettings();
                Dispatcher.BeginInvoke(new Action(() =>
                    MessageBox.Show(ex.Message, "学程 — 配置恢复",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
            }

            CountdownFontFamily = new FontFamily(settings.FontFamily);
            RefreshDateFields();

            // 启动时以注册表实际状态同步设置（防止手动删除注册表后不一致）
            settings.AutoStart = GetAutoStartFromRegistry();

            InitializeComponent();
            // MVVM：数据绑定到 ViewModel（倒计时/进度/自定义倒计时/一言）
            ViewModel = new ViewModels.MainWindowViewModel(settings);
            DataContext = ViewModel;
            SetupTrayIcon();
            SetupScheduleServices();
            SetupTimer();
            UpdateCountdown();
            PositionWindow();
            UpdateCountdownDisplay();

            // 拖动窗口时实时同步坐标到 settings
            LocationChanged += Window_LocationChanged;
        }
        // ── 保存 ───────────────────────────────────────────────
        public void SaveSettings()
        {
            settings.FontFamily         = CountdownFontFamily.Source;
            settings.NumberColorHex    = settings.NumberColor.ToString();
            settings.TextColorHex      = settings.TextColor.ToString();
            settings.ProgressBarColorHex = settings.ProgressBarColor.ToString();
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

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible) { Hide(); }
            else { Show(); Activate(); ApplyWindowLayer(); if (settings.EnableAnimations) PlayIntroAnimation(); }
        }

        /// <summary>快捷键切换课表栏（对 public，供 App 调用）</summary>
        public void ToggleScheduleBarViaHotkey()
        {
            if (_scheduleBarWindow != null) HideScheduleBarWindow();
            else ShowScheduleBarWindow();
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
            Topmost = settings.AlwaysOnTop;
        }

        /// <summary>预设模式下启用点击穿透（WS_EX_TRANSPARENT），自定义模式下可正常交互</summary>
        private void ApplyClickThrough()
        {
            bool shouldEnable = PositionPreset != PositionPresetValues.Custom;  // 非自定义模式 → 穿透
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
            ProgressBar.Width = ChinesePanel.ActualWidth;
        }
        //  窗口定位
        // ══════════════════════════════════════════════════════
        public void PositionWindow()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            double x, y;

            _isPositioning = true;

            if (PositionPreset == PositionPresetValues.Custom && settings.CustomPositionX >= 0 && settings.CustomPositionY >= 0)
            {
                Left = settings.CustomPositionX;
                Top  = settings.CustomPositionY;
                _isPositioning = false;
                return;
            }

            x = (sw - Width) / 2;
            switch (PositionPreset)
            {
                case PositionPresetValues.Top:         y = 10; break;
                case PositionPresetValues.UpperCenter: y = sh / 25.0; break;
                case PositionPresetValues.Center:      y = (sh - Height) / 2; break;
                case PositionPresetValues.LowerCenter: y = sh * 0.65; break;
                case PositionPresetValues.Bottom:      y = sh - Height - 40; break;
                default: y = sh / 25.0; break;
            }
            Left = x;
            Top  = y + settings.PositionOffsetY;

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
            if (settings.EnableAnimations)
                PlayIntroAnimation();
            // 渲染完成后刷新一次静态显示（进度条宽度依赖 ActualWidth，构造时不可用）
            UpdateCountdownDisplay();
            // 异步加载每日一言（fire-and-forget）
            if (settings.ShowDailyQuote)
            {
                _ = LoadDailyQuoteAsync();
                StartQuoteRefreshTimer();
            }
        }

        /// <summary>自定义模式下拖动窗口；预设模式下点击穿透，不响应拖动</summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 预设模式下不可拖动（点击穿透生效，此事件不应触发；但以防万一再次判断）
            if (PositionPreset != PositionPresetValues.Custom) return;

            DragMove();
        }

        /// <summary>拖动窗口时实时同步坐标到 settings，设置页中可实时看到</summary>
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            if (_isPositioning) return;
            // 只在自定义模式（preset=5）时回写坐标
            if (PositionPreset != PositionPresetValues.Custom) return;
            settings.CustomPositionX = Left;
            settings.CustomPositionY = Top;
        }

        // ══════════════════════════════════════════════════════
        //  入场动画：数字 0→实际值滚动 + 进度条 0→当前值
        //  持续 1250ms，PowerEaseOut(Power=5) 先快后慢适中
        // ══════════════════════════════════════════════════════
        private bool _isExiting;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 应用退出时直接放行（ExitApplication 已处理）
            if (_isExiting) return;
            e.Cancel = true;

            // 淡出动画后隐藏（FadeHelper 在动画完成后会移除动画持有，避免 Opacity 残留）
            FadeHelper.FadeOut(this, Math.Clamp(settings.OverallOpacity, 0.1, 1.0), 0, 300, Hide);
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

    }
}
