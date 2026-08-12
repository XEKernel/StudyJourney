using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MessageBox = GaokaoCountdown.Views.DialogHelper;

using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
using GaokaoCountdown.Helpers;
namespace GaokaoCountdown.Views
{
    /// <summary>考试模式全屏倒计时窗口。按 ESC 或托盘菜单退出。</summary>
    public partial class ExamModeWindow : Window
    {
        private readonly ScheduleManager _manager;
        private readonly AppSettings _settings;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _weatherTimer;

        // ── 当前显示状态 ──────────────────────────────────────
        private string _currentSubjectName = string.Empty;
        private bool   _warnShown          = false;
        private bool   _autoExited         = false;  // 防止重复自动退出
        private int    _lastBeepSecond     = -1;     // 防止重复蜂鸣

        public ExamModeWindow(ScheduleManager manager, AppSettings settings)
        {
            _manager  = manager;
            _settings = settings;
            InitializeComponent();

            // MVVM：倒计时/进度/科目信息绑定 ViewModel
            ViewModel = new ViewModels.ExamModeViewModel(manager, settings);
            DataContext = ViewModel;
        }

        /// <summary>考试模式 ViewModel（倒计时/进度/科目信息）</summary>
        public ViewModels.ExamModeViewModel? ViewModel { get; private set; }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStaticStyles();
            StartTimer();
            Refresh();
            ApplyFontSizes();
            _ = LoadWeatherAsync();
            StartWeatherTimer();

            // 入场动画：缩放弹入
            MainGrid.RenderTransform = new ScaleTransform(0.9, 0.9);
            MainGrid.RenderTransformOrigin = new Point(0.5, 0.5);
            MainGrid.Opacity = 0;
            var scaleAnim = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fadeAnim.Completed += (_, _) =>
            {
                MainGrid.BeginAnimation(UIElement.OpacityProperty, null);
                MainGrid.Opacity = 1;
            };
            scaleAnim.Completed += (_, _) => MainGrid.RenderTransform = Transform.Identity;
            MainGrid.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            MainGrid.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            MainGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        /// <summary>应用考试模式所有样式设置</summary>
        public void ApplyAllSettings(AppSettings s)
        {
            _settings.ExamSubjectFontSize        = s.ExamSubjectFontSize;
            _settings.ExamNameFontSize           = s.ExamNameFontSize;
            _settings.ExamCountdownFontSize      = s.ExamCountdownFontSize;
            _settings.ExamTimeInfoFontSize       = s.ExamTimeInfoFontSize;
            _settings.ExamNextSubjectFontSize    = s.ExamNextSubjectFontSize;
            _settings.ExamWarningFontSize        = s.ExamWarningFontSize;
            _settings.ExamEscHintFontSize        = s.ExamEscHintFontSize;
            _settings.ExamProgressBarHeight      = s.ExamProgressBarHeight;
            _settings.ExamSubjectColor           = s.ExamSubjectColor;
            _settings.ExamNameColor              = s.ExamNameColor;
            _settings.ExamCountdownNormalColor   = s.ExamCountdownNormalColor;
            _settings.ExamCountdownWarningColor  = s.ExamCountdownWarningColor;
            _settings.ExamCountdownCriticalColor = s.ExamCountdownCriticalColor;
            _settings.ExamDistanceColor          = s.ExamDistanceColor;
            _settings.ExamInfoColor              = s.ExamInfoColor;
            _settings.ExamProgressBarColor       = s.ExamProgressBarColor;
            _settings.ExamProgressBarBgColor     = s.ExamProgressBarBgColor;
            _settings.ExamBackgroundColor        = s.ExamBackgroundColor;
            _settings.ExamNextSubjectColor       = s.ExamNextSubjectColor;
            _settings.ExamWarningColor           = s.ExamWarningColor;
            _settings.ExamProgressPctColor       = s.ExamProgressPctColor;
            _settings.ExamCountdownFontFamily    = s.ExamCountdownFontFamily;
            _settings.ExamInfoDimColor           = s.ExamInfoDimColor;

            ApplyStaticStyles();
            Refresh();  // 立即刷新，使颜色即时生效
        }

        private static Brush SP(string hex) => ColorUtils.ParseColor(hex, "#FFFFFFFF");
        private static Brush Sd(string hex, string fallback) => ColorUtils.ParseColor(hex, fallback);

        /// <summary>应用静态样式（字体大小、颜色、进度条等不随计时变化的属性）</summary>
        private void ApplyStaticStyles()
        {
            // 同步 VM 中随设置变化的倒计时颜色画刷
            ViewModel?.RefreshColors();

            // 字体大小
            SubjectTb.FontSize        = _settings.ExamSubjectFontSize;
            ExamNameTb.FontSize       = _settings.ExamNameFontSize;
            CountdownTb.FontSize      = _settings.ExamCountdownFontSize;
            StartTimeTb.FontSize      = _settings.ExamTimeInfoFontSize;
            EndTimeTb.FontSize        = _settings.ExamTimeInfoFontSize;
            DurationTb.FontSize       = _settings.ExamTimeInfoFontSize;
            NextSubjectTb.FontSize    = _settings.ExamNextSubjectFontSize;
            WarningTb.FontSize        = _settings.ExamWarningFontSize;
            ProgressPctTb.FontSize    = _settings.ExamTimeInfoFontSize * 0.81;
            CurrentTimeTb.FontSize    = _settings.ExamModeFontSize;

            // ESC 提示
            EscHintTb.FontSize = _settings.ExamEscHintFontSize;

            // 颜色
            SubjectTb.Foreground     = SP(_settings.ExamSubjectColor);
            ExamNameTb.Foreground    = Sd(_settings.ExamNameColor, "#AAFFFFFF");
            NextSubjectTb.Foreground = Sd(_settings.ExamNextSubjectColor, "#88FFFFFF");
            WarningTb.Foreground     = Sd(_settings.ExamWarningColor, "#FFCC8800");
            ProgressPctTb.Foreground = Sd(_settings.ExamProgressPctColor, "#66FFFFFF");
            StartTimeTb.Foreground   = Sd(_settings.ExamInfoColor, "#88FFFFFF");
            EndTimeTb.Foreground     = Sd(_settings.ExamInfoColor, "#88FFFFFF");
            DurationTb.Foreground    = Sd(_settings.ExamInfoDimColor, "#66FFFFFF");
            CurrentTimeTb.Foreground = Sd(_settings.ExamInfoDimColor, "#66FFFFFF");
            EscHintTb.Foreground     = Sd(_settings.ExamInfoDimColor, "#88FFFFFF");

            // 倒计时字体族
            if (!string.IsNullOrWhiteSpace(_settings.ExamCountdownFontFamily))
            {
                try { CountdownTb.FontFamily = new FontFamily(_settings.ExamCountdownFontFamily); }
                catch { }
            }

            // 进度条
            ExamProgress.Height    = _settings.ExamProgressBarHeight;
            ExamProgress.Foreground = SP(_settings.ExamProgressBarColor);
            ExamProgress.Background = SP(_settings.ExamProgressBarBgColor);

            // 窗口背景
            try
            {
                var bgColor = (Color)ColorConverter.ConvertFromString(_settings.ExamBackgroundColor);
                Background = new SolidColorBrush(bgColor);
            }
            catch { }
        }

        /// <summary>应用考试模式字体大小设置</summary>
        public void ApplyFontSizes()
        {
            double baseFont = _settings.ExamModeFontSize;
            if (baseFont <= 0) baseFont = 32;
            CurrentTimeTb.FontSize = baseFont;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
            _weatherTimer?.Stop();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(_currentSubjectName))
                {
                    var r = MessageBox.Show(
                        "确定要退出考试模式吗？\n当前科目计时将被中断。",
                        "退出考试", System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);
                    if (r != System.Windows.MessageBoxResult.Yes) return;
                }
                CloseWindow();
            }
            else if (e.Key == Key.F11)
                ToggleFullScreen();
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleFullScreen();
        }

        private void ToggleFullScreen()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                Width = SystemParameters.WorkArea.Width * 0.8;
                Height = SystemParameters.WorkArea.Height * 0.8;
                Left = (SystemParameters.WorkArea.Width - Width) / 2;
                Top = (SystemParameters.WorkArea.Height - Height) / 2;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentSubjectName))
            {
                var r = MessageBox.Show(
                    "确定要退出考试模式吗？\n当前科目计时将被中断。",
                    "退出考试", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                if (r != System.Windows.MessageBoxResult.Yes) return;
            }
            CloseWindow();
        }

        private void CloseWindow()
        {
            _timer?.Stop();
            _weatherTimer?.Stop();
            Close();
        }

        // ── 定时刷新 ──────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            var now = DateTime.Now;

            // 展示数据（倒计时/进度/科目/时间）由 ViewModel 计算，绑定自动更新
            ViewModel?.Refresh(now);

            // ── 以下为 View 副作用：蜂鸣/警告/自动退出 ──
            if (ViewModel == null) return;

            // 15 分钟警告（仅一次）
            if (ViewModel.RemainingSeconds > 0 && ViewModel.RemainingSeconds <= 15 * 60 && !_warnShown)
            {
                _warnShown = true;
                WarningTb.Visibility = Visibility.Visible;
                System.Media.SystemSounds.Beep.Play();
            }
            // 5 分钟临界提醒（每秒蜂鸣，避免重复）
            if (ViewModel.RemainingSeconds > 0 && ViewModel.RemainingSeconds <= 5 * 60)
            {
                int currentSecond = (int)ViewModel.RemainingSeconds;
                if (currentSecond != _lastBeepSecond)
                {
                    _lastBeepSecond = currentSecond;
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            // 科目切换后重置警告
            if (ViewModel.Subject != _currentSubjectName)
            {
                _currentSubjectName = ViewModel.Subject;
                _warnShown = false;
                _lastBeepSecond = -1;   // 重置蜂鸣去重，防止新科目漏蜂鸣
                WarningTb.Visibility = Visibility.Collapsed;
            }

            // 最后一场考试结束 → 3 秒后自动退出，恢复正常上课状态
            if (ViewModel.IsExamOver && !_autoExited)
            {
                _autoExited = true;
                var autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                autoClose.Tick += (s, args) =>
                {
                    autoClose.Stop();
                    if (IsLoaded) CloseWindow();
                };
                autoClose.Start();
            }
        }

        // ── 天气加载 ──────────────────────────────────────────
        public async System.Threading.Tasks.Task LoadWeatherAsync()
        {
            try
            {
                var result = await WeatherService.FetchAsync(_settings.WeatherCity, _settings.WeatherAdcode);
                if (result == null) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    double weatherFs = _settings.WeatherFontSize;
                    if (weatherFs <= 0) weatherFs = 14;
                    W2IconTb.FontSize = weatherFs * 1.0;
                    W2CityTb.FontSize = weatherFs * 0.86;
                    W2WeatherTb.FontSize = weatherFs * 0.86;
                    W2TempTb.FontSize = weatherFs * 0.93;

                    W2CityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherCityColor, "#FFFFFFFF");
                    W2WeatherTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    W2TempTb.Foreground = ColorUtils.ParseColor(_settings.WeatherTempColor, "#FFFF8844");
                    W2IconTb.Foreground = ColorUtils.ParseColor(_settings.WeatherIconColor, "#FFFFAA00");

                    W2IconTb.Text = ColorUtils.GetWeatherEmoji(result.WeatherIcon);
                    W2CityTb.Text = result.Location;
                    W2WeatherTb.Text = result.Weather;
                    W2TempTb.Text = $"{result.Temperature}°";
                    WeatherRow2.Visibility = Visibility.Visible;
                });
            }
            catch { }
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
