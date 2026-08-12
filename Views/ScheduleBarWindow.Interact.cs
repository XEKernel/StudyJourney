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
using GaokaoCountdown.Helpers;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class ScheduleBarWindow : Window
    {
        // ── 60 秒倒计时回调 ───────────────────────────────────
        private void OnCountdown60Tick(object? sender, int remaining)
        {
            Dispatcher.Invoke(() =>
            {
                if (remaining > 0)
                {
                    if (Countdown60Panel.Visibility != Visibility.Visible)
                    {
                        _countdownExpanded = false; // 新倒计时周期，重置标志
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

                    // 文本走绑定（VM），View 只负责显隐/展开/提示音
                    int expandAt = _settings.CountdownExpandSeconds;
                    if (expandAt <= 0 || expandAt > 60) expandAt = 30;
                    if (remaining <= expandAt && _isCompact && !_countdownExpanded)
                    {
                        _countdownExpanded = true;
                        SetExpanded();
                        _expandTimer?.Stop();
                        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                        _expandTimer.Tick += AutoCompact;
                        _expandTimer.Start();
                        Countdown60Tb.Foreground = BrOrange;

                        // 提示音（可开关）
                        if (_settings.EnableCountdownSound)
                        {
                            try { System.Media.SystemSounds.Asterisk.Play(); }
                            catch { }
                        }
                    }

                    if (ViewModel != null)
                        ViewModel.Countdown60Text = remaining <= expandAt
                            ? $"⏰ 还有 {remaining}s 下课！"
                            : $"下课倒计时 {remaining}s";
                }
                else
                {
                    _countdownExpanded = false; // 倒计时结束，重置标志
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

        private void AutoCompact(object? sender, EventArgs e)
        {
            try
            {
                _expandTimer?.Stop();
                _expandTimer = null;
                var cur = _manager.GetCurrentEntry(DateTime.Now);
                if (cur != null) SetCompact();
            }
            catch { /* 窗口已关闭，忽略 */ }
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

        /// <summary>提醒时临时展开（ClassEnd 延迟 2 分钟后展开，给老师留操作窗口）</summary>
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

            // 下课延迟 2 分钟再展开（老师需要操作 PPT/关窗口）
            if (type == ReminderType.ClassEnd)
            {
                _expandTimer?.Stop();
                _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
                _expandTimer.Tick += (_, _) =>
                {
                    try
                    {
                        _expandTimer?.Stop();
                        _expandTimer = null;
                        SetExpanded();
                        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                        _expandTimer.Tick += AutoCompact;
                        _expandTimer.Start();
                    }
                    catch { }
                };
                _expandTimer.Start();
                return;
            }

            SetExpanded();
            _expandTimer?.Stop();
            _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _expandTimer.Tick += AutoCompact;
            _expandTimer.Start();
        }

        // ── 天气加载（复用 WeatherWindow 逻辑）───────────────
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
                    WeatherIconTb.FontSize = weatherFs * 0.86;
                    WeatherCityTb.FontSize = weatherFs * 0.72;
                    WeatherTb.FontSize = weatherFs * 0.72;
                    WeatherTempTb.FontSize = weatherFs * 0.8;
                    WeatherWindTb.FontSize = weatherFs * 0.65;
                    WeatherHumidityTb.FontSize = weatherFs * 0.65;

                    WeatherCityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherCityColor, "#FFFFFFFF");
                    WeatherTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherWindTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherHumidityTb.Foreground = ColorUtils.ParseColor(_settings.WeatherInfoColor, "#FFCCCCDD");
                    WeatherTempTb.Foreground = ColorUtils.ParseColor(_settings.WeatherTempColor, "#FFFF8844");
                    WeatherIconTb.Foreground = ColorUtils.ParseColor(_settings.WeatherIconColor, "#FFFFAA00");

                    WeatherIconTb.Text = ColorUtils.GetWeatherEmoji(result.WeatherIcon);
                    WeatherCityTb.Text = result.Location;
                    WeatherTb.Text = result.Weather;
                    WeatherTempTb.Text = $"{result.Temperature}°";
                    WeatherWindTb.Text = !string.IsNullOrWhiteSpace(result.WindDirection)
                        ? $"{result.WindDirection} {result.WindPower}".Trim() : "--";
                    WeatherHumidityTb.Text = result.Humidity > 0 ? $"{result.Humidity}%" : "--";
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
