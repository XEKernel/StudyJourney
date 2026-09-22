using Avalonia.Media;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudyJourney.Avalonia.Helpers;

// 学程 (Study Journey) — 学生桌面伴侣
// 应用设置数据模型，JSON 持久化到 settings.json

namespace StudyJourney.Avalonia.Models
{
    public class CustomCountdown
    {
        public string Name { get; set; } = "";
        public string DateStr { get; set; } = "";
    }

    /// <summary>老师账号（远程管理登录用）：用户名 / 密码哈希 / 显示名 / 任教科目。
    /// #4-阶段2：PasswordHash（PBKDF2-SHA256）为唯一存储形式；Password 仅保留给旧版明文数据
    /// 做一次性迁移（登录命中明文后自动升级为哈希并清空），新数据一律 Password="" + PasswordHash。</summary>
    public class TeacherAccount
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Subject { get; set; } = "";

        /// <summary>列表显示：李老师（语文）· teacher01</summary>
        public override string ToString() => $"{DisplayName}（{Subject}）· {Username}";

        /// <summary>设置新密码：明文 → PBKDF2 哈希，立即清空明文（内存态；落盘由调用方 SaveSettings）</summary>
        public void SetPassword(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return;
            PasswordHash = Helpers.PasswordHasher.Hash(plain);
            Password = "";
        }

        /// <summary>是否仍存旧版明文（等待首次登录迁移）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool NeedsPlaintextMigration =>
            string.IsNullOrEmpty(PasswordHash) && !string.IsNullOrEmpty(Password);

        /// <summary>本次校验是否触发明文→哈希自动升级（登录成功路径据此立即落盘；internal 供同程序集清除标记）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool JustUpgradedDuringVerify { get; internal set; }

        /// <summary>校验密码：优先哈希；命中旧明文则就地升级为哈希并置 JustUpgradedDuringVerify</summary>
        public bool VerifyPassword(string plain)
        {
            if (!string.IsNullOrEmpty(PasswordHash))
                return Helpers.PasswordHasher.Verify(plain, PasswordHash);

            if (!string.IsNullOrEmpty(Password))
            {
                // 旧版明文（仅迁移期存在）：验证通过即升级，下次登录起走哈希
                bool ok = string.Equals(plain, Password, StringComparison.Ordinal);
                if (ok)
                {
                    SetPassword(plain);
                    JustUpgradedDuringVerify = true;
                }
                return ok;
            }
            return false;
        }
    }

    public class AppSettings
    {
        // ── 字体 ─────────────────────────────────────────────
        public string FontFamily { get; set; } = "Microsoft YaHei UI";
        public int FontSize { get; set; } = 12;

        // ── 颜色 ─────────────────────────────────────────────
        [JsonIgnore]
        public Color TextColor { get; set; } = Colors.White;

        /// <summary>强调色（校园蓝 #2B6CB0）：统一控制进度条/圆环/课表高亮</summary>
        [JsonIgnore]
        public Color AccentColor { get; set; } = Color.FromRgb(0x2B, 0x6C, 0xB0);

        // 颜色的 JSON 序列化代理属性
        public string TextColorHex
        {
            get => TextColor.ToString();
            set { try { TextColor = Color.Parse(value); } catch { } }
        }

        public string AccentColorHex
        {
            get => AccentColor.ToString();
            set { try { AccentColor = Color.Parse(value); } catch { } }
        }

        // ── 显示选项 ─────────────────────────────────────────
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
        /// <summary>位置预设：0=顶部, 1=中上, 2=居中, 3=中下, 4=底部, 5=自定义（JSON 持久化保持 int）</summary>
        public int PositionPreset { get; set; } = PositionPresetValues.UpperCenter;
        public double CustomPositionX { get; set; } = -1;   // -1 表示居中
        public double CustomPositionY { get; set; } = -1;   // -1 表示自动
        public double PositionOffsetX { get; set; } = 0;    // 水平偏移（像素，负=左移，正=右移）
        public double PositionOffsetY { get; set; } = 0;    // 垂直偏移（像素，负=上移，正=下移）
        public bool AlwaysOnTop { get; set; } = false;
        /// <summary>上课收起为进度条时是否置顶（不影响完整视图的 AlwaysOnTop）</summary>
        public bool CompactProgressTopmost { get; set; } = true;
        /// <summary>点击穿透：鼠标点击穿过窗口（自定义坐标模式始终可交互）</summary>
        public bool ClickThrough { get; set; } = true;

        // ── 灵动岛外观 ──────────────────────────────────────
        /// <summary>胶囊圆角（px，0=直角，20=完全胶囊）</summary>
        public double MainWindowCornerRadius { get; set; } = 12;
        /// <summary>胶囊分离显示：true=多块胶囊，false=单条大胶囊</summary>
        public bool IslandSeparated { get; set; } = true;

        // ── 日期设置 ─────────────────────────────────────────
        // 目标考试日期
        public string GaokaoDateStr { get; set; } = "2027-06-07 09:00:00";
        // 进度条起算日期
        public string StartDateStr { get; set; } = "2024-08-24";

        // ── 倒计时显示 ────────────────────────────────────────
        /// <summary>倒计时进度条样式：false=环形（进度数字在环旁），true=条形</summary>
        public bool CountdownProgressBarStyle { get; set; } = false;
        // （时间单位天/时/分/秒 的显示开关见「时间精度」ShowDays 等；进度条/百分比见 ShowProgressBar/ShowProgressText）

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
        public string WeatherIconColor        { get; set; } = "#FFFFAA00";  // 天气图标
        /// <summary>天气详细度：0=简洁（emoji+温度），1=标准（+描述），2=详细（+城市/湿度/风力）</summary>
        public int WeatherDetailLevel        { get; set; } = 1;

        // ── 系统 ─────────────────────────────────────────────
        // 是否开机自启动（写注册表 HKCU\Run）
        public bool AutoStart { get; set; } = false;
        // 有其他窗口（非桌面）时自动隐藏倒计时（桌面同一层，默认开启）
        public bool HideWhenMaximized { get; set; } = true;
        // 上课时收起为进度条（只留进度条+上课进度；false 则上课时保持完整显示）
        public bool HideDuringClass { get; set; } = true;

        // ── 提醒开关 ──────────────────────────────────────────
        public bool EnableReminderSound  { get; set; } = true;
        public string ReminderSoundPath  { get; set; } = string.Empty;  // 空=系统提示音
        /// <summary>提醒方式：0=胶囊弹窗（默认），1=Windows 通知</summary>
        public int ReminderStyle         { get; set; } = 0;
        public bool RemindClassStart     { get; set; } = true;
        public bool RemindClassMid       { get; set; } = false;
        // 下课提前提醒：距下课 10 分钟 / 1 分钟各提醒一次（下课那一刻仍静默，避免拖堂时打扰）
        public bool RemindClassEndSoon10 { get; set; } = true;
        public bool RemindClassEndSoon   { get; set; } = true;
        public bool RemindClassEnd       { get; set; } = false;
        public bool RemindNextClassSoon  { get; set; } = true;
        public bool RemindDayEnd         { get; set; } = false;
        public bool RemindSpecialPeriod  { get; set; } = true;

        // ── 更新检查 ──────────────────────────────────────────
        public bool AutoCheckUpdate      { get; set; } = true;
        /// <summary>下载更新时走国内加速镜像（2026-09-15）。
        /// 实测：本机 github.com 的 release 资产**直连 20 秒 0 字节**（基本不通），
        /// api.github.com 直连反而可用 → 所以资产下载必须走镜像。</summary>
        public bool UpdateUseProxy       { get; set; } = true;
        /// <summary>加速镜像前缀（形如 https://gh-proxy.com/ ，会把原始 GitHub 链接拼在后面）。
        /// 默认值实测可用；留空等价于直连 GitHub。</summary>
        public string UpdateProxyPrefix  { get; set; } = "https://gh-proxy.com/";

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
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>清理过期的 .corrupted 备份，只保留最近 maxCount 份</summary>
        private static void TrimCorruptedBackups(string basePath, int maxCount = 3)
        {
            try
            {
                var dir = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(dir)) return;
                var files = Directory.GetFiles(dir, Path.GetFileName(basePath) + ".corrupted.*")
                    .OrderByDescending(f => f)   // 文件名含时间戳，字典序即时间序
                    .Skip(maxCount);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                // 首次运行：给一份带默认老师账号的设置。
                // ⚠ 默认账号的 PBKDF2 计算只在这里发生（约 470ms / 7 个账号）。
                //
                // ⚠⚠ **必须落盘**（2026-09-22 修）：原来只造对象不保存，于是文件永远不会被创建 →
                //   **每次启动都重新走这个分支**，那 470ms 变成每次启动都白付（与我上一版
                //   "只付一次"的说法相反），日志里还会每次都出现"未找到 settings.json"。
                //   落盘后 Updater 的用户数据保护名单也从第一次启动起就有效。
                Helpers.AppLogger.Info("[AppSettings] 未找到 settings.json，按首次运行创建默认设置（含默认老师账号）");
                var fresh = new AppSettings { Teachers = CreateDefaultTeachers() };
                fresh.Save();
                return fresh;
            }

            try
            {
                // 分段计时（2026-09-18）：启动耗时里"设置加载"这一段一直占 ~470ms，
                // 先入为主以为是 STJ 反射建元数据，但换成源生成器后**没变** → 判断错了。
                // 真因是 AppSettings 属性初始化器里的 DefaultTeachers()（7×PBKDF2），已修。
                // 保留这段埋点：以后再出现异常值能立刻定位。
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string json = File.ReadAllText(SettingsPath);
                long tRead = sw.ElapsedMilliseconds;

                var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)
                             ?? new AppSettings();
                long tParse = sw.ElapsedMilliseconds;

                Helpers.AppLogger.Info(
                    $"[启动耗时]   └ settings 明细：读文件 {tRead} ms / 反序列化 {tParse - tRead} ms / 共 {tParse} ms");
                return result;
            }
            catch (JsonException jex)
            {
                // 内容确实不是合法 JSON（真损坏）：备份一份供恢复，再把原文件移走，
                // 让程序能生成一份干净的设置。
                Helpers.AppLogger.Error("settings.json 内容损坏，已备份并重建（可从 .corrupted.* 恢复）", jex);
                BackupAndRemoveCorrupted();
                return new AppSettings();
            }
            catch (Exception ex)
            {
                // ⚠ 修复（2026-09-16）：IO/权限等**偶发**失败绝不碰原文件。
                //
                // 原来这里把「任何异常」都当成文件损坏 → 复制备份后 `File.Delete` 原文件，
                // 而且只用 `Debug.WriteLine` 记录（Release 下完全看不见）。后果：
                //   · 文件被另一个实例的原子写短暂占用、杀软扫描、磁盘抖动 → **静默清空老师的全部设置**，
                //     下次保存再把默认值写回 → 不可逆、无提示；
                //   · NativeAOT 下 System.Text.Json 反射被禁用（抛 NotSupportedException），
                //     跑一次就把 settings.json 删一次（2026-09-16 实测确认）。
                // 现在：保留原文件 + 用 AppLogger 明确记录，最坏也只是这一次用默认值。
                Helpers.AppLogger.Error(
                    $"settings.json 读取失败，本次使用默认设置（原文件已保留，不会丢失）: {ex.Message}", ex);
                return new AppSettings();
            }
        }

        /// <summary>备份损坏的设置文件后移除原文件（保留最近 3 份备份）</summary>
        private static void BackupAndRemoveCorrupted()
        {
            try
            {
                var bak = SettingsPath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(SettingsPath, bak, overwrite: true);
                File.Delete(SettingsPath);
                TrimCorruptedBackups(SettingsPath);
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Warn($"备份损坏的设置文件失败（已跳过，不影响使用）: {ex.Message}");
            }
        }

        // ── 自定义倒计时 ──────────────────────────────────────
        public List<CustomCountdown> CustomCountdowns { get; set; } = new();

        // ── 远程管理 ─────────────────────────────────────────
        /// <summary>自定义课件上传目录（空 = 默认 Documents\StudyJourney\Uploads）</summary>
        public string CustomUploadDirectory { get; set; } = "";
        /// <summary>启动软件时自动开启远程 HTTP 服务（默认开启，局域网老师可访问）</summary>
        public bool AutoStartHttpServer { get; set; } = true;
        /// <summary>班级名称（教师端控制台顶部显示）</summary>
        public string ClassName { get; set; } = "高三（2）班 智慧黑板";
        /// <summary>默认老师显示名（老师账号列表为空时的兜底）</summary>
        public string TeacherName { get; set; } = "老师";
        /// <summary>老师账号列表（语数英物化生 6 位 + 管理员），登录与显示名来源。
        /// <summary>
        /// 老师账号列表。⚠ 默认值刻意留**空表**，不是 `DefaultTeachers()`。
        ///
        /// 2026-09-18 修（启动性能）：原来这里是 `= DefaultTeachers()`，而每个默认账号都要算一次
        /// PBKDF2（10 万次迭代）→ 7 个账号 ≈ **470ms**。更糟的是它不是"首次运行才付一次"：
        /// `JsonSerializer.Deserialize` 也要先构造对象、跑一遍属性初始化器，所以**每次启动都白跑**，
        /// 算出来的哈希马上又被文件里的值覆盖掉。实测这 470ms 正是启动里除运行时自举外最大的一块。
        /// （我一开始误判成"STJ 反射建元数据"，换源生成器后数字纹丝不动，才定位到这里。）
        ///
        /// 现在默认账号只由 <see cref="Load"/> 在"首次运行（无 settings.json）"时赋一次。
        /// </summary>
        public List<TeacherAccount> Teachers { get; set; } = new();

        /// <summary>
        /// 生成默认老师账号（语数英物化生 6 位 + 管理员 Teacher01）。
        /// #4-阶段2：默认账号存 PBKDF2 哈希（SetPassword），settings.json 不再出现明文密码。
        ///
        /// ⚠ **只允许在"首次运行"时调用一次**（见 <see cref="Load"/>）。
        /// 每个账号算一次 PBKDF2(100_000) ≈ 67ms，7 个 ≈ 470ms ——
        /// 放回属性初始化器会让每次启动（含每次反序列化）都白跑一遍。
        /// </summary>
        public static List<TeacherAccount> CreateDefaultTeachers() => new()
        {
            MakeAccount("Teacher01", "Study@2026", "老师", "管理员"),
            MakeAccount("teacher01", "123456", "李老师", "语文"),
            MakeAccount("teacher02", "123456", "张老师", "数学"),
            MakeAccount("teacher03", "123456", "王老师", "英语"),
            MakeAccount("teacher04", "123456", "赵老师", "物理"),
            MakeAccount("teacher05", "123456", "孙老师", "化学"),
            MakeAccount("teacher06", "123456", "周老师", "生物"),
        };

        /// <summary>构造预置哈希的默认账号（仅在文件缺失/恢复默认时运行一次，成本 ~0.5s 可接受）</summary>
        private static TeacherAccount MakeAccount(string username, string plainPassword,
            string displayName, string subject)
        {
            var acc = new TeacherAccount
            {
                Username = username,
                DisplayName = displayName,
                Subject = subject,
            };
            acc.SetPassword(plainPassword);
            return acc;
        }
        /// <summary>
        /// 可选科目（选科）：课表编辑只在范围内选。默认物化生组合（语数英+物化生），不含政史地。
        /// 可在设置页「服务器 → 可选科目」增删。
        /// </summary>
        public List<string> Subjects { get; set; } = new()
        {
            "语文", "数学", "英语", "物理", "化学", "生物",
            "体育", "信息技术", "班会", "自习",
        };

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, AppJsonContext.Default.AppSettings);
                Helpers.FileAtomic.WriteAllText(SettingsPath, json);   // #6：原子写，防半截 JSON
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Error("保存设置失败", ex);
            }
        }
    }

    /// <summary>窗口位置预设常量（消除魔法数字；JSON 中保持 int 存储以兼容旧配置）</summary>
    public static class PositionPresetValues
    {
        public const int Top          = 0;   // 顶部
        public const int UpperCenter  = 1;   // 中上（默认）
        public const int Center       = 2;   // 居中
        public const int LowerCenter  = 3;   // 中下
        public const int Bottom       = 4;   // 底部
        public const int Custom       = 5;   // 自定义坐标
    }
}
