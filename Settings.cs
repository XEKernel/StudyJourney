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

        // ── 每日一言 ──────────────────────────────────────────
        public bool   ShowDailyQuote            { get; set; } = true;
        public double QuoteFontSize             { get; set; } = 12;
        public string QuoteForegroundHex        { get; set; } = "#AAAAAA";
        public bool   QuoteItalic               { get; set; } = true;
        public string QuoteApiUrl               { get; set; } = "https://uapis.cn/api/v1/saying";
        public int    QuoteAutoRefreshInterval   { get; set; } = 0;  // 秒，0=不自动切换
        public string QuoteTextFieldName         { get; set; } = "text";  // API 返回 JSON 中携带文本的字段名

        // ── 天气 ──────────────────────────────────────────
        public bool   ShowWeather            { get; set; } = false;
        public string WeatherCity            { get; set; } = "北京";
        public string WeatherAdcode          { get; set; } = "";
        public int    WeatherWindowMode      { get; set; } = 0;   // 0=文字模式, 1=窗口模式
        public double WeatherCustomX         { get; set; } = -1;
        public double WeatherCustomY         { get; set; } = -1;
        public double WeatherWindowWidth     { get; set; } = 300;
        public double WeatherWindowHeight    { get; set; } = 80;
        public int    WeatherRefreshInterval { get; set; } = 0;   // 分钟，0=不自动刷新
        public double WeatherFontSize        { get; set; } = 14;   // 文本字号
        public bool   WeatherAlwaysOnTop     { get; set; } = false;
        // 天气文字颜色
        public string WeatherCityColor        { get; set; } = "#FFFFFFFF";  // 城市名
        public string WeatherInfoColor        { get; set; } = "#FFCCCCDD";  // 天气描述+风+湿度
        public string WeatherTempColor        { get; set; } = "#FFFF8844";  // 温度
        public string WeatherTimeColor        { get; set; } = "#66AAAAAA";  // 更新时间
        public string WeatherIconColor        { get; set; } = "#FFFFAA00";  // 天气图标

        // ── 系统 ─────────────────────────────────────────────
        // 是否开机自启动（写注册表 HKCU\Run）
        public bool AutoStart { get; set; } = false;
        // 其他窗口最大化时自动隐藏倒计时
        public bool HideWhenMaximized { get; set; } = false;

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
