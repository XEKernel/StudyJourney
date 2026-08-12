using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GaokaoCountdown.Helpers;
using GaokaoCountdown.Models;

namespace GaokaoCountdown.ViewModels
{
    /// <summary>
    /// 考试模式 ViewModel：负责倒计时 / 进度 / 科目信息等展示数据的计算。
    /// 蜂鸣提醒、自动退出、警告显隐、全屏切换等副作用留在 code-behind（View 职责）。
    /// </summary>
    public partial class ExamModeViewModel : ObservableObject
    {
        private readonly ScheduleManager _manager;
        private readonly AppSettings _settings;

        // ── 颜色画刷（随剩余时间切换，设置变更时 RefreshColors 重建）──
        private Brush _brNormal   = Freeze(0xFF, 0xFF, 0xFF);
        private Brush _brWarning  = Freeze(0xFF, 0xCC, 0x88);
        private Brush _brCritical = Freeze(0xFF, 0x44, 0x44);
        private Brush _brDistance = Freeze(0xAA, 0xFF, 0xFF, 0xFF);
        private static readonly Brush BrGray = Freeze(0x66, 0x66, 0x66);

        private static Brush Freeze(byte r, byte g, byte b, byte a = 0xFF)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public ExamModeViewModel(ScheduleManager manager, AppSettings settings)
        {
            _manager = manager;
            _settings = settings;
            Weather = new WeatherViewModel(settings);
            RefreshColors();
        }

        /// <summary>共享天气（文本走绑定；样式由 View 应用）</summary>
        public WeatherViewModel Weather { get; }

        /// <summary>设置变更后调用：重建倒计时颜色画刷</summary>
        public void RefreshColors()
        {
            _brNormal   = ColorUtils.ParseColor(_settings.ExamCountdownNormalColor,   "#FFFFFFFF");
            _brWarning  = ColorUtils.ParseColor(_settings.ExamCountdownWarningColor,  "#FFCC8800");
            _brCritical = ColorUtils.ParseColor(_settings.ExamCountdownCriticalColor, "#FFFF4444");
            _brDistance = ColorUtils.ParseColor(_settings.ExamDistanceColor,          "#AAFFFFFF");
        }

        // ── 展示数据 ──────────────────────────────────────────
        [ObservableProperty]
        private string currentTime = "";

        [ObservableProperty]
        private string examName = "";

        [ObservableProperty]
        private string subject = "";

        [ObservableProperty]
        private string countdownText = "";

        [ObservableProperty]
        private Brush countdownBrush = BrGray;

        [ObservableProperty]
        private double progressValue;   // 0~100

        [ObservableProperty]
        private string progressPctText = "";

        [ObservableProperty]
        private string startTime = "";

        [ObservableProperty]
        private string endTime = "";

        [ObservableProperty]
        private string duration = "";

        [ObservableProperty]
        private string nextSubject = "";

        // ── 供 View 做副作用判断 ──────────────────────────────
        /// <summary>当前科目剩余秒数（>0 表示考试中），View 用于蜂鸣/警告</summary>
        [ObservableProperty]
        private double remainingSeconds;

        /// <summary>今日考试已全部结束（View 用于 3 秒后自动退出）</summary>
        [ObservableProperty]
        private bool isExamOver;

        /// <summary>每秒刷新（500ms 定时器），重算全部展示数据</summary>
        public void Refresh(DateTime now)
        {
            CurrentTime = now.ToString("HH:mm:ss");

            var cur = _manager.GetCurrentExamSubject(now);
            if (cur.HasValue)
            {
                var (exam, subject) = cur.Value;
                ExamName = exam.Name;
                Subject = subject.Name;
                StartTime = subject.StartTimeStr;
                EndTime = subject.EndTimeStr;
                Duration = $"共 {subject.Duration.TotalMinutes:F0} 分钟";

                var endDt = now.Date + subject.EndTime;
                var remaining = endDt - now;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                RemainingSeconds = remaining.TotalSeconds;
                CountdownText = remaining.ToString(@"hh\:mm\:ss");
                CountdownBrush = remaining.TotalMinutes <= 5
                    ? _brCritical
                    : remaining.TotalMinutes <= 15
                        ? _brWarning
                        : _brNormal;

                var elapsed = now - (now.Date + subject.StartTime);
                double pct = subject.Duration.TotalSeconds > 0
                             ? Math.Clamp(elapsed.TotalSeconds / subject.Duration.TotalSeconds, 0, 1)
                             : 0;
                ProgressValue = pct * 100;
                ProgressPctText = $"{pct * 100:F1}% 已完成";

                var next = _manager.GetNextExamSubject(now);
                NextSubject = next.HasValue
                    ? $"下一场：{next.Value.Item2.Name}  {next.Value.Item2.StartTimeStr}"
                    : "";
                IsExamOver = false;
            }
            else
            {
                var next = _manager.GetNextExamSubject(now);
                if (next.HasValue)
                {
                    var (exam, subject) = next.Value;
                    ExamName = exam.Name;
                    Subject = subject.Name;
                    var startDt = now.Date + subject.StartTime;
                    var remaining = startDt - now;
                    CountdownText = remaining > TimeSpan.Zero
                        ? $"距开考 {remaining:hh\\:mm\\:ss}"
                        : "--:--";
                    CountdownBrush = _brDistance;
                    RemainingSeconds = 0;
                    ProgressValue = 0;
                    ProgressPctText = string.Empty;
                    StartTime = subject.StartTimeStr;
                    EndTime = subject.EndTimeStr;
                    Duration = $"共 {subject.Duration.TotalMinutes:F0} 分钟";
                    NextSubject = string.Empty;
                    IsExamOver = false;
                }
                else
                {
                    ExamName = "今日考试";
                    Subject = "考试已结束";
                    CountdownText = "00:00";
                    CountdownBrush = BrGray;
                    RemainingSeconds = 0;
                    ProgressValue = 100;
                    ProgressPctText = "100%";
                    StartTime = string.Empty;
                    EndTime = string.Empty;
                    Duration = string.Empty;
                    NextSubject = string.Empty;
                    IsExamOver = true;
                }
            }
        }
    }
}
