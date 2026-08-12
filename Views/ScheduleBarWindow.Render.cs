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
        // ── 定时刷新 ──────────────────────────────────────────
        private DispatcherTimer? _expandTimer;
        private bool _isCompact = false;
        private bool _countdownExpanded = false; // 防止每秒重复展开

        // ── "快上课"闪烁状态 ──────────────────────────────────
        private bool _flashVisible = true;
        private const int FLASH_THRESHOLD_SECONDS = 60;
        private bool _showTomorrowPreview = false;
        private bool _tomorrowChecked = false;   // 防止每天重复检查

        // ── 课节卡片缓存（避免每秒重建 UI）─────────────────────
        private DateTime _lastBuildDate = DateTime.MinValue;
        private string _lastStatusText = "";   // 状态文本变化检测（脉冲动画用）
        /// <summary>(entry, card, 节次Label, 课程Label, 时间Label)</summary>
        private readonly List<(ScheduleEntry Entry, Border Card, TextBlock PeriodLabel,
                               TextBlock SubjectLabel, TextBlock TimeLabel)> _periodCardRefs = new();

        // ── 缓存画刷（避免每秒 new SolidColorBrush；已 Freeze 提升性能）─────────────
        private static readonly SolidColorBrush BrOrange    = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44)));
        private static readonly SolidColorBrush BrGray      = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)));
        private static readonly SolidColorBrush BrLightGray = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
        private static readonly SolidColorBrush BrWhite     = FreezeBrush(new SolidColorBrush(Colors.White));
        private static readonly SolidColorBrush BrLtGreen   = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7)));
        private static readonly SolidColorBrush BrLtBlue    = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)));
        private static readonly SolidColorBrush BrFlashBg   = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x55, 0x15, 0x00)));
        private static readonly SolidColorBrush BrIndicator = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));

        private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

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

            // 数据计算（时间/状态/倒计时/进度）全部委托 ViewModel，值不变不触发通知
            ViewModel?.Refresh(now);

            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);

            // ── "快上课"闪烁翻转（IsFlashing 由 VM 计算）──
            if (ViewModel?.IsFlashing == true)
                _flashVisible = !_flashVisible;
            else
                _flashVisible = true;

            // ── 状态文本变化时脉冲动画（文本本身走绑定）──
            if (ViewModel != null && ViewModel.Status != _lastStatusText && !ViewModel.IsFlashing)
                PulseOpacity(StatusTb);
            _lastStatusText = ViewModel?.Status ?? "";

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
            // 如果之前有内容，先淡出再重建（实现平滑过渡）
            bool hadContent = PeriodPanel.Children.Count > 0;
            if (hadContent)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                PeriodPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                // 在下一帧重建
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(160);
                    PeriodPanel.Children.Clear();
                    _periodCardRefs.Clear();
                    BuildPanelContent(now);
                    PeriodPanel.Opacity = 1;
                }, System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            PeriodPanel.Children.Clear();
            _periodCardRefs.Clear();
            BuildPanelContent(now);
        }

        /// <summary>文本变化时快速脉冲动画（150ms 淡到 0.3 再弹回 1）</summary>
        private static void PulseOpacity(UIElement element)
        {
            element.Opacity = 1;
            var dim = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(80))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            dim.Completed += (_, _) =>
            {
                var up = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(150))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                element.BeginAnimation(UIElement.OpacityProperty, up);
            };
            element.BeginAnimation(UIElement.OpacityProperty, dim);
        }

        private void BuildPanelContent(DateTime now)
        {
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

    }
}
