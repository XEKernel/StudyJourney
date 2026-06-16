using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    /// <summary>考试模式全屏倒计时窗口。按 ESC 或托盘菜单退出。</summary>
    public partial class ExamModeWindow : Window
    {
        private readonly ScheduleManager _manager;
        private readonly AppSettings _settings;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _weatherTimer;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        // ── 当前显示状态 ──────────────────────────────────────
        private string _currentSubjectName = string.Empty;
        private bool   _warnShown          = false;
        private bool   _autoExited         = false;  // 防止重复自动退出

        public ExamModeWindow(ScheduleManager manager, AppSettings settings)
        {
            _manager  = manager;
            _settings = settings;
            InitializeComponent();
        }

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
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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
            DurationTb.Foreground    = Sd(_settings.ExamInfoColor, "#66FFFFFF");
            CurrentTimeTb.Foreground = Sd(_settings.ExamInfoColor, "#66FFFFFF");

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
                    var r = System.Windows.MessageBox.Show(
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
                var r = System.Windows.MessageBox.Show(
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
            CurrentTimeTb.Text = now.ToString("HH:mm:ss");

            var cur = _manager.GetCurrentExamSubject(now);
            if (cur.HasValue)
            {
                var (exam, subject) = cur.Value;
                ShowCurrentSubject(exam, subject, now);
            }
            else
            {
                // 不在考试中，显示等待或结束
                var next = _manager.GetNextExamSubject(now);
                if (next.HasValue)
                {
                    var (exam, subject) = next.Value;
                    ExamNameTb.Text     = exam.Name;
                    SubjectTb.Text      = subject.Name;
                    var startDt         = now.Date + subject.StartTime;
                    var remaining       = startDt - now;
                    CountdownTb.Text    = remaining > TimeSpan.Zero
                                          ? $"距开考 {remaining:hh\\:mm\\:ss}"
                                          : "--:--";
                    CountdownTb.Foreground = SP(_settings.ExamDistanceColor);
                    ExamProgress.Value  = 0;
                    ProgressPctTb.Text  = string.Empty;
                    StartTimeTb.Text    = subject.StartTimeStr;
                    EndTimeTb.Text      = subject.EndTimeStr;
                    DurationTb.Text     = $"共 {subject.Duration.TotalMinutes:F0} 分钟";
                    NextSubjectTb.Text  = string.Empty;
                    WarningTb.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ExamNameTb.Text     = "今日考试";
                    SubjectTb.Text      = "考试已结束";
                    CountdownTb.Text    = "00:00";
                    CountdownTb.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    ExamProgress.Value  = 100;
                    ProgressPctTb.Text  = "100%";
                    NextSubjectTb.Text  = string.Empty;
                    WarningTb.Visibility = Visibility.Collapsed;

                    // 最后一场考试结束 → 3 秒后自动退出，恢复正常上课状态
                    if (!_autoExited)
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
            }
        }

        private void ShowCurrentSubject(ExamEntry exam, ExamSubject subject, DateTime now)
        {
            ExamNameTb.Text = exam.Name;
            SubjectTb.Text  = subject.Name;
            StartTimeTb.Text = subject.StartTimeStr;
            EndTimeTb.Text   = subject.EndTimeStr;
            DurationTb.Text  = $"共 {subject.Duration.TotalMinutes:F0} 分钟";

            // 剩余时间
            var endDt     = now.Date + subject.EndTime;
            var remaining = endDt - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            CountdownTb.Text = remaining.ToString(@"hh\:mm\:ss");

            // 颜色随时间变化（可配置）
            CountdownTb.Foreground = remaining.TotalMinutes <= 5
                ? SP(_settings.ExamCountdownCriticalColor)
                : remaining.TotalMinutes <= 15
                    ? SP(_settings.ExamCountdownWarningColor)
                    : SP(_settings.ExamCountdownNormalColor);

            // 进度条
            var elapsed = now - (now.Date + subject.StartTime);
            double pct  = subject.Duration.TotalSeconds > 0
                          ? Math.Clamp(elapsed.TotalSeconds / subject.Duration.TotalSeconds, 0, 1)
                          : 0;
            ExamProgress.Value = pct * 100;
            ProgressPctTb.Text = $"{pct * 100:F1}% 已完成";

            // 下一场
            var next = _manager.GetNextExamSubject(now);
            if (next.HasValue)
            {
                var (_, ns) = next.Value;
                NextSubjectTb.Text = $"下一场：{ns.Name}  {ns.StartTimeStr}";
            }
            else
            {
                NextSubjectTb.Text = string.Empty;
            }

            // 15 分钟警告
            if (remaining.TotalMinutes <= 15 && !_warnShown)
            {
                _warnShown = true;
                WarningTb.Visibility = Visibility.Visible;
                System.Media.SystemSounds.Beep.Play();
            }
            // 5 分钟临界提醒（每秒蜂鸣）
            if (remaining.TotalMinutes <= 5 && remaining.TotalSeconds % 2 == 0)
            {
                System.Media.SystemSounds.Beep.Play();
            }
            // 科目切换后重置警告
            if (subject.Name != _currentSubjectName)
            {
                _currentSubjectName = subject.Name;
                _warnShown = false;
                WarningTb.Visibility = Visibility.Collapsed;
            }
        }

        // ── 天气加载 ──────────────────────────────────────────
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

                string rCity    = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                string district = root.TryGetProperty("district", out var d) ? d.GetString() ?? "" : "";
                string weather  = root.TryGetProperty("weather", out var w) ? w.GetString() ?? "" : "";
                string wIcon    = root.TryGetProperty("weather_icon", out var wi) ? wi.GetString() ?? "" : "";
                int temperature = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number
                    ? (int)t.GetDouble() : 0;

                string location = !string.IsNullOrWhiteSpace(district) ? district
                    : !string.IsNullOrWhiteSpace(rCity) ? rCity : city;

                await Dispatcher.InvokeAsync(() =>
                {
                    // 应用天气字体大小
                    double weatherFs = _settings.WeatherFontSize;
                    if (weatherFs <= 0) weatherFs = 14;
                    W2IconTb.FontSize = weatherFs * 1.0;
                    W2CityTb.FontSize = weatherFs * 0.86;
                    W2WeatherTb.FontSize = weatherFs * 0.86;
                    W2TempTb.FontSize = weatherFs * 0.93;

                    // 应用天气颜色
                    W2CityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherCityColor, "#FFFFFFFF");
                    W2WeatherTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    W2TempTb.Foreground = ColorUtils.ParseColor(_settings.WeatherTempColor, "#FFFF8844");
                    W2IconTb.Foreground = ColorUtils.ParseColor(_settings.WeatherIconColor, "#FFFFAA00");

                    W2IconTb.Text = ColorUtils.GetWeatherEmoji(wIcon);
                    W2CityTb.Text = location;
                    W2WeatherTb.Text = weather;
                    W2TempTb.Text = $"{temperature}°";
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
