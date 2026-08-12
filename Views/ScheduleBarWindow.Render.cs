using System;
using System.Windows;
using System.Windows.Controls;
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
        // ── 紧凑模式状态 ──────────────────────────────────────
        private DispatcherTimer? _expandTimer;
        private bool _isCompact = false;
        private bool _countdownExpanded = false; // 防止每秒重复展开

        // ── 状态文本变化检测（脉冲动画用）──────────────────────
        private string _lastStatusText = "";

        // ── 缓存画刷（Interact.cs 的 60s 倒计时用；卡片颜色已移到 XAML DataTemplate）──
        private static readonly SolidColorBrush BrOrange = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44)));

        private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }

        // ── 定时刷新 ──────────────────────────────────────────
        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();
        }

        // ── 核心刷新方法（数据计算 + 卡片重建全在 ViewModel）───
        private void Refresh()
        {
            var now = DateTime.Now;

            // 时间/状态/倒计时/进度/课节卡片（含闪烁）全部由 ViewModel 计算并通知绑定
            ViewModel?.Refresh(now);

            var cur = _manager.GetCurrentEntry(now);

            // ── 状态文本变化时脉冲动画（文本本身走绑定）──
            if (ViewModel != null && ViewModel.Status != _lastStatusText && !ViewModel.IsFlashing)
                PulseOpacity(StatusTb);
            _lastStatusText = ViewModel?.Status ?? "";

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
    }
}
