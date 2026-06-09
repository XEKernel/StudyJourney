using System.Windows.Media;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaokaoCountdown
{
    public class AppSettings
    {
        // ── 中文文本 ─────────────────────────────────────────
        public string ChinesePrefix { get; set; } = "距离高考还有 ";
        public string ChineseDaysText { get; set; } = "天 ";
        public string ChineseHoursText { get; set; } = "小时 ";
        public string ChineseMinutesText { get; set; } = "分 ";
        public string ChineseSecondsText { get; set; } = "秒";

        // ── 英文文本 ─────────────────────────────────────────
        public string EnglishPrefix { get; set; } = "There are ";
        public string EnglishDaysText { get; set; } = " days, ";
        public string EnglishHoursText { get; set; } = " hours, ";
        public string EnglishMinutesText { get; set; } = " minutes, and ";
        public string EnglishSecondsText { get; set; } = " seconds until the college entrance examination.";

        // ── 字体 ─────────────────────────────────────────────
        public string FontFamily { get; set; } = "Arial";
        public int FontSize { get; set; } = 40;

        // ── 颜色 ─────────────────────────────────────────────
        [JsonIgnore]
        public Color NumberColor { get; set; } = Colors.Red;

        [JsonIgnore]
        public Color TextColor { get; set; } = Colors.White;

        [JsonIgnore]
        public Color ProgressBarColor { get; set; } = Colors.White;

        // 颜色的 JSON 序列化代理属性
        public string NumberColorHex
        {
            get => NumberColor.ToString();
            set => NumberColor = (Color)ColorConverter.ConvertFromString(value);
        }

        public string TextColorHex
        {
            get => TextColor.ToString();
            set => TextColor = (Color)ColorConverter.ConvertFromString(value);
        }

        public string ProgressBarColorHex
        {
            get => ProgressBarColor.ToString();
            set => ProgressBarColor = (Color)ColorConverter.ConvertFromString(value);
        }

        // ── 显示选项 ─────────────────────────────────────────
        public bool ShowEnglishLine { get; set; } = true;
        public bool ShowProgressBar { get; set; } = true;
        public bool ShowProgressText { get; set; } = true;

        // ── 时间精度（各部分开关）──────────────────────────
        public bool ShowDays    { get; set; } = true;
        public bool ShowHours   { get; set; } = true;
        public bool ShowMinutes { get; set; } = true;
        public bool ShowSeconds { get; set; } = true;

        // 整体透明度 0.1 ~ 1.0
        public double OverallOpacity { get; set; } = 1.0;

        // ── 窗口位置 ─────────────────────────────────────────
        // 0=顶部, 1=中上, 2=居中, 3=中下, 4=底部, 5=自定义
        public int PositionPreset { get; set; } = 1;
        public double CustomPositionX { get; set; } = -1;   // -1 表示居中
        public double CustomPositionY { get; set; } = -1;   // -1 表示自动
        public double PositionOffsetY { get; set; } = 0;    // 垂直偏移（像素）
        public bool AlwaysOnTop { get; set; } = false;

        // ── 日期设置 ─────────────────────────────────────────
        // 目标考试日期
        public string GaokaoDateStr { get; set; } = "2027-06-07 09:00:00";
        // 进度条起算日期
        public string StartDateStr { get; set; } = "2024-08-24";

        // ── 进度条样式 ────────────────────────────────────────
        // 进度文本精度（小数位数）
        public int ProgressDecimalDigits { get; set; } = 7;

        // ── 动画 ─────────────────────────────────────────────
        // 是否启用主窗口数字脉冲 & 进度条平滑动画
        public bool EnableAnimations { get; set; } = true;

        // ── 持久化 ────────────────────────────────────────────
        private static readonly string SettingsPath = "settings.json";

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 保存失败静默处理
            }
        }
    }
}
