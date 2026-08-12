using System;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GaokaoCountdown.Models;

namespace GaokaoCountdown.ViewModels
{
    /// <summary>
    /// 课表悬浮栏 ViewModel：负责状态文本 / 倒计时 / 进度等"每秒变化的数据"计算。
    /// 课节卡片渲染、闪烁动画、Win32 定位、天气、紧凑/展开动画等 UI 职责留在 code-behind。
    /// </summary>
    public partial class ScheduleBarViewModel : ObservableObject
    {
        private const int FLASH_THRESHOLD_SECONDS = 60;

        private readonly ScheduleManager _manager;

        // ── 状态颜色（与 View 中卡片画刷同一色系）──────────────
        private static readonly Brush BrOrange = Freeze(0xFF, 0x88, 0x44);
        private static readonly Brush BrRed    = Freeze(0xFF, 0x44, 0x44);
        private static readonly Brush BrGreen  = Freeze(0x4C, 0xAF, 0x50);
        private static readonly Brush BrGray   = Freeze(0xAA, 0xAA, 0xAA);

        private static Brush Freeze(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public ScheduleBarViewModel(ScheduleManager manager, AppSettings settings)
        {
            _manager = manager;
            Weather = new WeatherViewModel(settings);
        }

        /// <summary>共享天气（文本走绑定；样式由 View 应用）</summary>
        public WeatherViewModel Weather { get; }

        // ── 时间/日期 ─────────────────────────────────────────
        [ObservableProperty]
        private string currentTime = "";

        [ObservableProperty]
        private string date = "";

        // ── 状态文本 ──────────────────────────────────────────
        [ObservableProperty]
        private string status = "";

        [ObservableProperty]
        private Brush statusBrush = BrOrange;

        [ObservableProperty]
        private string nextCountdown = "";

        [ObservableProperty]
        private Brush nextCountdownBrush = BrOrange;

        [ObservableProperty]
        private Visibility nextCountdownVisibility = Visibility.Collapsed;

        /// <summary>"快上课"闪烁中（View 用它驱动课节卡片闪烁）</summary>
        [ObservableProperty]
        private bool isFlashing;

        // ── 当前课进度（完整模式 + 紧凑模式共用）──────────────
        [ObservableProperty]
        private Visibility progressVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private string progressLabel = "";

        [ObservableProperty]
        private string progressPct = "";

        [ObservableProperty]
        private double progress;   // 0~100，绑定进度条 Value

        [ObservableProperty]
        private string compactRemaining = "";

        // ── 60 秒下课倒计时文本（显隐/展开动画仍在 View）──────
        [ObservableProperty]
        private string countdown60Text = "";

        /// <summary>每秒刷新：重算全部展示数据（值不变时不触发通知）</summary>
        public void Refresh(DateTime now)
        {
            CurrentTime = now.ToString("HH:mm:ss");
            Date = now.ToString("MM月dd日 ddd");

            var cur = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);
            var timeToNext = _manager.GetTimeToNextEntry(now);

            bool flashing = timeToNext.HasValue
                && timeToNext.Value.TotalSeconds <= FLASH_THRESHOLD_SECONDS
                && timeToNext.Value.TotalSeconds > 0;
            IsFlashing = flashing;

            if (flashing && next != null)
            {
                int remainSec = (int)timeToNext!.Value.TotalSeconds;
                Status = $"即将上课：{next.Subject}";
                StatusBrush = BrOrange;
                NextCountdown = $"还有 {remainSec}s";
                NextCountdownBrush = BrRed;
                NextCountdownVisibility = Visibility.Visible;
            }
            else if (cur != null)
            {
                Status = $"正在上课：{cur.Subject}";
                StatusBrush = BrGreen;
                NextCountdownVisibility = Visibility.Collapsed;
            }
            else if (next != null)
            {
                Status = "课间休息";
                StatusBrush = BrOrange;
                if (timeToNext.HasValue)
                {
                    var ts = timeToNext.Value;
                    NextCountdown = $"距下节课 {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                    NextCountdownBrush = BrOrange;
                    NextCountdownVisibility = Visibility.Visible;
                }
                else
                {
                    NextCountdownVisibility = Visibility.Collapsed;
                }
            }
            else
            {
                Status = "今日课程已结束";
                StatusBrush = BrGray;
                NextCountdownVisibility = Visibility.Collapsed;
            }

            // 当前课进度
            var pct = _manager.GetCurrentProgress(now);
            if (pct.HasValue && cur != null)
            {
                ProgressVisibility = Visibility.Visible;
                ProgressLabel = cur.Subject;
                Progress = pct.Value * 100;
                ProgressPct = $"{pct.Value * 100:F0}%";

                var remaining = _manager.GetTimeToEndOfCurrent(now);
                CompactRemaining = remaining.HasValue
                    ? $"剩余 {remaining.Value.Hours:D2}:{remaining.Value.Minutes:D2}:{remaining.Value.Seconds:D2}"
                    : "";
            }
            else
            {
                ProgressVisibility = Visibility.Collapsed;
                CompactRemaining = "";
            }
        }
    }
}
