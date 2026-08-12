using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GaokaoCountdown.Models;

namespace GaokaoCountdown.ViewModels
{
    /// <summary>
    /// 主窗口倒计时 ViewModel：负责所有"数据计算"逻辑。
    /// 动画 / 窗口定位 / Win32 / 托盘等 UI 专属逻辑仍留在 MainWindow code-behind（View 职责）。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly AppSettings _settings;
        private readonly HttpClient _httpClient;
        private DateTime _gaokaoDate;
        private DateTime _startDate;

        public MainWindowViewModel(AppSettings settings, HttpClient httpClient)
        {
            _settings = settings;
            _httpClient = httpClient;
            RefreshDates();
        }

        /// <summary>从设置刷新目标/起算日期</summary>
        public void RefreshDates()
        {
            if (!DateTime.TryParse(_settings.GaokaoDateStr, out _gaokaoDate))
                _gaokaoDate = new DateTime(2027, 6, 7, 9, 0, 0);
            if (!DateTime.TryParse(_settings.StartDateStr, out _startDate))
                _startDate = new DateTime(2024, 8, 24);
        }

        // ── 倒计时数值（绑定主窗口数字）────────────────────────
        [ObservableProperty]
        private int days;

        [ObservableProperty]
        private int hours;

        [ObservableProperty]
        private int minutes;

        [ObservableProperty]
        private int seconds;

        // ── 进度（绑定进度文本；进度条动画仍在 View 层）────────
        [ObservableProperty]
        private double progressValue;   // 0~100

        [ObservableProperty]
        private string progressText = "";

        [ObservableProperty]
        private string progressTextEn = "";

        // ── 自定义倒计时（显示最近一个）────────────────────────
        [ObservableProperty]
        private string customCountdownText = "";

        // ── 每日一言（HTTP 获取在 VM；View 拿到文本后做淡入动画）──
        [ObservableProperty]
        private string quoteText = "";

        /// <summary>
        /// 从一言 API 拉取文本（支持自定义 URL / 字段名）。
        /// 返回原始文本（未加「」包装）；失败或格式异常返回 null。
        /// </summary>
        public async Task<string?> FetchQuoteAsync()
        {
            try
            {
                string url = string.IsNullOrWhiteSpace(_settings.QuoteApiUrl)
                    ? "https://uapis.cn/api/v1/saying" : _settings.QuoteApiUrl;
                string json = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string fieldName = string.IsNullOrWhiteSpace(_settings.QuoteTextFieldName)
                    ? "text" : _settings.QuoteTextFieldName.Trim();
                string? text = root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString() : null;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch
            {
                // 网络异常/JSON 解析失败：静默返回 null
                return null;
            }
        }

        /// <summary>每秒 tick：重算倒计时与进度</summary>
        public void Tick()
        {
            var now = DateTime.Now;
            var timeLeft = _gaokaoDate - now;

            bool positive = timeLeft.TotalSeconds > 0;
            Days = positive ? timeLeft.Days : 0;
            Hours = positive ? timeLeft.Hours : 0;
            Minutes = positive ? timeLeft.Minutes : 0;
            Seconds = positive ? timeLeft.Seconds : 0;

            double totalDays = (_gaokaoDate - _startDate).TotalDays;
            double daysPassed = (now - _startDate).TotalDays;
            double progress = Math.Min(1, Math.Max(0, daysPassed / totalDays));
            ProgressValue = progress * 100.0;

            string fmt = "F" + _settings.ProgressDecimalDigits;
            double pct = progress * 100.0;
            ProgressText = $"高中生活已过去 {pct.ToString(fmt)}%";
            ProgressTextEn = $"High school life has passed {pct.ToString(fmt)}%.";
        }

        /// <summary>重算自定义倒计时文本（内部缓存最近目标，仅文本变化时触发通知）</summary>
        public void UpdateCustomCountdown()
        {
            var list = _settings.CustomCountdowns;
            if (list == null || list.Count == 0)
            {
                CustomCountdownText = string.Empty;
                return;
            }

            var now = DateTime.Now;
            DateTime? nearest = null;
            string? name = null;
            foreach (var cc in list)
            {
                if (DateTime.TryParse(cc.DateStr, out var dt) && dt > now &&
                    (nearest == null || dt < nearest))
                {
                    nearest = dt;
                    name = cc.Name;
                }
            }

            if (nearest == null)
            {
                CustomCountdownText = string.Empty;
                return;
            }

            var ts = nearest.Value - now;
            CustomCountdownText = $"📅 {name} 还剩 {ts.Days} 天 {ts.Hours:D2}时{ts.Minutes:D2}分";
        }
    }
}
