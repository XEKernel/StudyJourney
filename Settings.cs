using System.Windows.Media;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// 学程 (Study Journey) — 学生桌面伴侣
// 应用设置数据模型，JSON 持久化到 settings.json

namespace GaokaoCountdown
{
    public class CustomCountdown
    {
        public string Name { get; set; } = "";
        public string DateStr { get; set; } = "";
    }

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
        public string WeatherCity            { get; set; } = "北京";
        public string WeatherAdcode          { get; set; } = "";
        public int    WeatherRefreshInterval { get; set; } = 0;   // 分钟，0=不自动刷新
        public double WeatherFontSize        { get; set; } = 14;   // 文本字号
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
        // 上课期间隐藏高考倒计时主窗口
        public bool HideDuringClass { get; set; } = true;

        // ── 课表栏 ────────────────────────────────────────────
        public bool   ShowScheduleBar          { get; set; } = false;
        public double ScheduleBarOpacity       { get; set; } = 0.92;
        public bool   ScheduleBarAlwaysOnTop   { get; set; } = true;
        public bool   ScheduleBarClickThrough  { get; set; } = false;
        /// <summary>0 = 全屏宽度</summary>
        public double ScheduleBarWidth         { get; set; } = 0;
        /// <summary>课表栏基础字体大小（默认 14）</summary>
        public double ScheduleBarFontSize      { get; set; } = 14;
        /// <summary>上课时课表栏自动收缩为进度条</summary>
        public bool   ScheduleBarAutoCollapse   { get; set; } = true;

        // ── 提醒开关 ──────────────────────────────────────────
        public bool EnableReminderSound  { get; set; } = true;
        public string ReminderSoundPath  { get; set; } = string.Empty;  // 空=系统提示音
        public bool RemindClassStart     { get; set; } = true;
        public bool RemindClassMid       { get; set; } = false;
        public bool RemindClassEndSoon   { get; set; } = true;
        public bool RemindClassEnd       { get; set; } = true;
        public bool RemindNextClassSoon  { get; set; } = true;
        public bool RemindDayEnd         { get; set; } = true;
        public bool RemindSpecialPeriod  { get; set; } = true;

        // ── 考试模式 ──────────────────────────────────────────
        public bool EnableExamMode       { get; set; } = false;
        /// <summary>当天有考试时自动进入考试模式</summary>
        public bool AutoEnterExamMode    { get; set; } = false;
        /// <summary>考试模式当前时间字体大小（默认 32）</summary>
        public double ExamModeFontSize    { get; set; } = 32;

        // ── 考试模式样式 ──────────────────────────────────────
        /// <summary>科目名称字体大小（默认 64）</summary>
        public double ExamSubjectFontSize       { get; set; } = 64;
        /// <summary>考试名称字体大小（默认 28）</summary>
        public double ExamNameFontSize          { get; set; } = 28;
        /// <summary>倒计时字体大小（默认 120）</summary>
        public double ExamCountdownFontSize     { get; set; } = 120;
        /// <summary>时间信息行字体大小（默认 16）</summary>
        public double ExamTimeInfoFontSize      { get; set; } = 16;
        /// <summary>下一场文字字体大小（默认 22）</summary>
        public double ExamNextSubjectFontSize   { get; set; } = 22;
        /// <summary>警告文字字体大小（默认 20）</summary>
        public double ExamWarningFontSize       { get; set; } = 20;
        /// <summary>ESC 提示字体大小（默认 12）</summary>
        public double ExamEscHintFontSize       { get; set; } = 12;

        /// <summary>倒计时正常颜色（剩余 > 15 分钟）</summary>
        public string ExamCountdownNormalColor   { get; set; } = "#FFFFFFFF";
        /// <summary>倒计时警告颜色（剩余 5-15 分钟）</summary>
        public string ExamCountdownWarningColor  { get; set; } = "#FFCC8800";
        /// <summary>倒计时紧迫颜色（剩余 < 5 分钟）</summary>
        public string ExamCountdownCriticalColor { get; set; } = "#FFCC4400";
        /// <summary>距开考倒计时颜色</summary>
        public string ExamDistanceColor          { get; set; } = "#FF8899CC";
        /// <summary>信息文字颜色（时间/时长等）</summary>
        public string ExamInfoColor              { get; set; } = "#88FFFFFF";
        /// <summary>标签信息半透明颜色</summary>
        public string ExamInfoDimColor           { get; set; } = "#44FFFFFF";
        /// <summary>进度条颜色</summary>
        public string ExamProgressBarColor       { get; set; } = "#FF5B9BD5";
        /// <summary>进度条高度</summary>
        public double ExamProgressBarHeight       { get; set; } = 12;
        /// <summary>进度条背景颜色</summary>
        public string ExamProgressBarBgColor     { get; set; } = "#22FFFFFF";
        /// <summary>主窗口背景颜色</summary>
        public string ExamBackgroundColor        { get; set; } = "#FF060B14";
        /// <summary>科目文字颜色</summary>
        public string ExamSubjectColor           { get; set; } = "#FFFFFFFF";
        /// <summary>考试名称文字颜色</summary>
        public string ExamNameColor              { get; set; } = "#AAFFFFFF";
        /// <summary>下一场文字颜色</summary>
        public string ExamNextSubjectColor       { get; set; } = "#88FFFFFF";
        /// <summary>警告文字颜色</summary>
        public string ExamWarningColor           { get; set; } = "#FFCC8800";
        /// <summary>进度百分比文字颜色</summary>
        public string ExamProgressPctColor       { get; set; } = "#66FFFFFF";
        /// <summary>倒计时字体族</summary>
        public string ExamCountdownFontFamily    { get; set; } = "Consolas";

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

        // ── 自定义倒计时 ──────────────────────────────────────
        public List<CustomCountdown> CustomCountdowns { get; set; } = new();

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
