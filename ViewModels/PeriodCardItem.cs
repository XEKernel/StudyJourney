using CommunityToolkit.Mvvm.ComponentModel;
using GaokaoCountdown.Models;

namespace GaokaoCountdown.ViewModels
{
    /// <summary>
    /// 课节卡片数据模型：由 ScheduleBarViewModel 构建，ItemsControl + DataTemplate 渲染。
    /// 替代原 code-behind 中 BuildCard / UpdatePeriodCardStates 的 UI 构建代码。
    /// </summary>
    public partial class PeriodCardItem : ObservableObject
    {
        public ScheduleEntry Entry { get; }

        public string PeriodText { get; }
        public string Subject { get; }
        public string TimeText { get; }

        // 字体大小（由 ScheduleBarFontSize 按比例算好，避免 XAML 里做乘法）
        public double PeriodFontSize { get; }
        public double SubjectFontSize { get; }
        public double TimeFontSize { get; }

        // ── 状态（DataTrigger 驱动卡片样式切换）──────────────
        [ObservableProperty]
        private bool isCurrent;

        [ObservableProperty]
        private bool isNext;

        [ObservableProperty]
        private bool isFlashing;

        public PeriodCardItem(ScheduleEntry entry, bool isCurrent, bool isNext, double baseFont)
        {
            Entry = entry;
            IsCurrent = isCurrent;
            IsNext = isNext;

            double periodSize = baseFont * 0.65;
            PeriodFontSize = periodSize;
            SubjectFontSize = baseFont * 0.8;
            TimeFontSize = periodSize;

            PeriodText = entry.Type switch
            {
                PeriodType.Morning => "早自习",
                PeriodType.Evening => "晚自习",
                PeriodType.Reading => "晚读",
                PeriodType.Noon    => "午自习",
                _                  => $"第 {entry.Period} 节"
            };
            Subject = entry.Subject;
            TimeText = $"{entry.StartTimeStr}-{entry.EndTimeStr}";
        }
    }
}
