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
                FadeHelper.FadeIn(this, 0, Math.Clamp(OverallOpacity, 0.1, 1.0), 350,
                    () => { if (EnableAnimations) PlayIntroAnimation(); });
            }
        }

        // ══════════════════════════════════════════════════════
        //  每秒触发：更新倒计时数字 + 动画
        // ══════════════════════════════════════════════════════
        private void UpdateCountdown()
        {
            // ── 上课期间隐藏主窗口（可设置科目白名单）──
            var curEntry = _scheduleManager?.GetCurrentEntry(DateTime.Now);
            bool isInClass = settings.HideDuringClass && curEntry != null;
            // HideSubjects 非空时只隐藏匹配科目，为空则所有科目都隐藏
            if (isInClass && !string.IsNullOrWhiteSpace(settings.HideSubjects))
            {
                if (settings.HideSubjects != _cachedHideSubjects)
                {
                    _cachedHideSubjects = settings.HideSubjects;
                    _cachedHiddenSet = settings.HideSubjects
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
                isInClass = curEntry != null && _cachedHiddenSet.Contains(curEntry.Subject);
            }
            bool isInExam    = _examModeWindow != null;
            bool shouldHide  = isInClass || isInExam;

            if (shouldHide)
            {
                // 进入隐藏模式
                _classEndRestoreTimer?.Stop();
                _classEndRestoreTimer = null;

                if (Visibility == Visibility.Visible)
                {
                    _hiddenByScheduleOrExam = true;
                    Hide();
                }
                // 隐藏科目时连课表栏进度条也不显示
                if (isInClass && !string.IsNullOrWhiteSpace(settings.HideSubjects))
                    _scheduleBarWindow?.Hide();
                return; // 不更新 UI，不请求 API
            }
            else if (_hiddenByScheduleOrExam)
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
                        FadeHelper.FadeIn(this, 0, Math.Clamp(OverallOpacity, 0.1, 1.0), 400,
                            () => { if (EnableAnimations) PlayIntroAnimation(); });
                        _scheduleBarWindow?.Show();
                        UpdateCountdownDisplay(); // 立即刷新倒计时显示
                    };
                    _classEndRestoreTimer.Start();
                }
                return;
            }
            else
            {
                // 正常显示模式，取消延迟
                _classEndRestoreTimer?.Stop();
                _classEndRestoreTimer = null;
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

            // 自定义倒计时（内部有缓存+文本变更守卫，每秒开销极低）
            UpdateCustomCountdown();
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

            // ── 字体族（仅在变更时设置）─────────────────────
            if (_lastFontFamily != CountdownFontFamily.Source)
            {
                _lastFontFamily = CountdownFontFamily.Source;
                _cachedChineseTextBlocks ??= ChinesePanel.Children.OfType<TextBlock>().ToList();
                _cachedEnglishTextBlocks ??= EnglishPanel.Children.OfType<TextBlock>().ToList();
                _cachedChineseTextBlocks.ForEach(tb => tb.FontFamily = CountdownFontFamily);
                _cachedEnglishTextBlocks.ForEach(tb => tb.FontFamily = CountdownFontFamily);
            }
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

            // ── 自定义倒计时（显示最近的一个）───────────────────
            UpdateCustomCountdown();
        }

        private void UpdateCustomCountdown()
        {
            var list = settings.CustomCountdowns;
            if (list == null || list.Count == 0)
            {
                if (CustomCountdownTb.Visibility != Visibility.Collapsed)
                {
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    fade.Completed += (_, _) => CustomCountdownTb.Visibility = Visibility.Collapsed;
                    CustomCountdownTb.BeginAnimation(UIElement.OpacityProperty, fade);
                }
                return;
            }

            var now = DateTime.Now;
            var todayDate = now.Date;
            DateTime? nearestDate;
            string nearestName;

            // 缓存：仅当日期变化或缓存无效时重新计算最近倒计时
            if (_cachedNearestCountdown != null && _lastCountdownComputeDay == todayDate)
            {
                var (cachedDate, cachedName) = _cachedNearestCountdown.Value;
                nearestDate = cachedDate;
                nearestName = cachedName;
            }
            else
            {
                nearestDate = null;
                nearestName = "";
                foreach (var cc in list)
                {
                    if (DateTime.TryParse(cc.DateStr, out var dt))
                    {
                        if (dt > now && (nearestDate == null || dt < nearestDate))
                        {
                            nearestDate = dt;
                            nearestName = cc.Name;
                        }
                    }
                }
                _lastCountdownComputeDay = todayDate;
                _cachedNearestCountdown = nearestDate != null ? (nearestDate.Value, nearestName) : null;
            }

            if (nearestDate == null)
            {
                if (CustomCountdownTb.Visibility != Visibility.Collapsed)
                {
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    fade.Completed += (_, _) => CustomCountdownTb.Visibility = Visibility.Collapsed;
                    CustomCountdownTb.BeginAnimation(UIElement.OpacityProperty, fade);
                }
                return;
            }

            var ts = nearestDate.Value - now;
            string text = $"📅 {nearestName} 还剩 {ts.Days} 天 {ts.Hours:D2}时{ts.Minutes:D2}分";

            if (CustomCountdownTb.Text != text)
            {
                CustomCountdownTb.Text = text;
                if (CustomCountdownTb.Visibility != Visibility.Visible)
                {
                    CustomCountdownTb.Visibility = Visibility.Visible;
                    CustomCountdownTb.Opacity = 0;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    CustomCountdownTb.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
            }
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
    }
}
