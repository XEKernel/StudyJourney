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

        // ── 课节卡片列表（ItemsControl 绑定）──────────────────
        [ObservableProperty]
        private System.Collections.ObjectModel.ObservableCollection<PeriodCardItem> cards = new();

        /// <summary>列表头文本（"明天课程"），空则不显示</summary>
        [ObservableProperty]
        private string headerText = "";

        /// <summary>空态文本（"今日无课"/"明日无课"），空则不显示</summary>
        [ObservableProperty]
        private string emptyText = "";

        /// <summary>列表头字号（"明天课程"）</summary>
        [ObservableProperty]
        private double headerFontSize = 11;

        /// <summary>空态字号</summary>
        [ObservableProperty]
        private double emptyFontSize = 10;

        // ── 卡片重建内部状态 ──────────────────────────────────
        private DateTime _lastBuildDate = DateTime.MinValue;
        private bool _tomorrowChecked;
        private bool _flashToggle = true;

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

            // ── 课节卡片：日期变更才重建，其余每秒更新状态 ──
            if (_lastBuildDate != now.Date)
            {
                _lastBuildDate = now.Date;
                RebuildCards(now);
            }
            else
            {
                UpdateCardStates(now);
            }
        }

        // ── 课节卡片构建（替代 code-behind 的 UI 构建代码）────
        private void RebuildCards(DateTime now)
        {
            Cards.Clear();
            HeaderText = "";
            EmptyText = "";
            double labelSize = BaseFontSize * 0.65 * 1.2;
            HeaderFontSize = labelSize;
            EmptyFontSize = labelSize;

            var entries = _manager.GetTodayEntries(now.Date);
            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);

            // 放学后直接展示明天课程
            if (cur == null && next == null && !_tomorrowChecked)
            {
                _tomorrowChecked = true;
                var tomorrow = _manager.GetTodayEntries(now.Date.AddDays(1));
                if (tomorrow.Count > 0)
                {
                    HeaderText = "明天课程";
                    foreach (var e in tomorrow)
                        Cards.Add(new PeriodCardItem(e, false, false, BaseFontSize));
                    return;
                }
                EmptyText = "明日无课";
                return;
            }
            _tomorrowChecked = false;

            if (entries.Count == 0)
            {
                EmptyText = "今日无课";
                return;
            }

            foreach (var entry in entries)
            {
                Cards.Add(new PeriodCardItem(entry, cur == entry, next == entry, BaseFontSize));
            }
        }

        /// <summary>同一天内仅更新卡片状态（不重建集合）</summary>
        private void UpdateCardStates(DateTime now)
        {
            if (Cards.Count == 0) return;
            var cur  = _manager.GetCurrentEntry(now);
            var next = _manager.GetNextEntry(now);

            // 闪烁交替：仅当处于"快上课"窗口时翻转
            _flashToggle = IsFlashing ? !_flashToggle : true;

            foreach (var card in Cards)
            {
                card.IsCurrent = card.Entry == cur;
                card.IsNext = card.Entry == next;
                card.IsFlashing = card.IsNext && IsFlashing && !_flashToggle;
            }
        }

        private double _baseFontSize;

        private double BaseFontSize => _baseFontSize > 0 ? _baseFontSize : 14;

        /// <summary>View 应用字体设置后同步基准字号；变更会强制重建卡片</summary>
        public void SetBaseFontSize(double size)
        {
            if (size <= 0) size = 14;
            if (Math.Abs(_baseFontSize - size) < 0.01) return;
            _baseFontSize = size;
            _lastBuildDate = DateTime.MinValue;   // 下次 Refresh 重建卡片
        }
    }
}
