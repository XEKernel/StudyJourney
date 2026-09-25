using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 「恢复默认设置」的**影响范围清单** + **重置前自动备份**（2026-09-25，规划 2.7 ①）。
///
/// 起因：原来按下去只弹一句"将恢复默认：外观 / 位置 / 提醒 / 考试模式等偏好"，
/// 老师不知道具体会丢什么 —— 而「恢复默认」恰恰会清掉老师账号、自定义倒计时、
/// 选科、远程控制台配置这些**真正的业务数据**，属数据安全问题。
///
/// 现在：① 清单按"会丢什么 / 会恢复什么 / 不受影响"三段给出，带**真实数量**；
/// ② 动手前先自动备份 settings.json（老师还能反悔）。
///
/// ⚠ 本类刻意写成**纯逻辑**（不碰 App.Settings 全局、不引 UI）→ 便于 SJ_SELFTEST=settings 断言。
/// </summary>
public static class SettingsReset
{
    /// <summary>备份根目录名（与课表编辑器的「备份数据」同一约定）</summary>
    public const string BackupFolderName = "backups";

    public static string BackupRoot(string baseDir) => Path.Combine(baseDir, BackupFolderName);

    /// <summary>目标考试日期 / 起算日期的兜底显示（空值说明白，别显示成空白）</summary>
    private static string Show(string? v, string empty = "（未设置）")
        => string.IsNullOrWhiteSpace(v) ? empty : v.Trim();

    /// <summary>
    /// 会被**清掉或需要重新设置**的东西（老师最关心的那段，放最前）。
    /// 只列出"当前确实有值"的项，避免清单里塞一堆"本来就空"的噪音。
    /// </summary>
    public static List<string> LossItems(AppSettings s)
    {
        var list = new List<string>();
        // 与出厂默认一致的值不算"会丢" —— 否则一份全新的设置里会冒出
        // "会清除 班级名称「高三（2）班 智慧黑板」"这种让人以为有自定义数据的假警报。
        var d = new AppSettings();

        int teachers = s.Teachers?.Count ?? 0;
        if (teachers > 0)
            list.Add($"老师账号：清空 {teachers} 个账号  →  重置后远程登录会回落到内置账号");

        int countdowns = s.CustomCountdowns?.Count ?? 0;
        if (countdowns > 0)
            list.Add($"自定义倒计时：删除 {countdowns} 条（含名称与日期）");

        if (!string.IsNullOrWhiteSpace(s.CustomUploadDirectory))
            list.Add($"远程控制台：自定义上传目录  {Show(s.CustomUploadDirectory)}");

        if (!string.IsNullOrWhiteSpace(s.ClassName) &&
            !string.Equals(s.ClassName.Trim(), d.ClassName, StringComparison.Ordinal))
            list.Add($"远程控制台：班级名称「{s.ClassName}」");

        bool cityChanged = !string.IsNullOrWhiteSpace(s.WeatherCity) &&
                           !string.Equals(s.WeatherCity.Trim(), d.WeatherCity, StringComparison.Ordinal);
        bool adcodeSet = !string.IsNullOrWhiteSpace(s.WeatherAdcode);
        if (cityChanged || adcodeSet)
            list.Add("天气：" + string.Join("、", new List<string>
            {
                cityChanged ? $"城市「{Show(s.WeatherCity)}」" : "",
                adcodeSet   ? $"行政区划代码 {s.WeatherAdcode}" : "",
            }.Where(x => x.Length > 0)));

        var extraDirs = (s.DiagExtraDirs ?? "")
            .Split(new[] { ';', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (extraDirs.Count > 0)
            list.Add($"诊断包：额外目录 {extraDirs.Count} 项  →  恢复为不带额外目录");

        if (s.RecordActivity)
            list.Add("上课活动记录：会被关闭（已记录的 records/ 文件保留，不再新增）");

        if (s.AutoStart)
            list.Add("开机自启动：会被关闭，并同时删除注册表启动项");

        return list;
    }

    /// <summary>会被**恢复成出厂默认**的偏好（列成大类，不逐个枚举 40 个字段）</summary>
    public static List<string> ResetItems(AppSettings s)
    {
        var defaults = new AppSettings();
        var list = new List<string>
        {
            "外观：字体 / 字号 / 透明度 / 胶囊样式与圆角 / 时间单位与进度条显示",
            "位置：屏幕位置预设、自定义坐标、水平垂直偏移、置顶、点击穿透",
            "提醒：8 个提醒开关、提示音路径、提醒方式（胶囊弹窗 / 系统通知）",
            $"倒计时：目标日期 → {Show(defaults.GaokaoDateStr)}，起算日期 → {Show(defaults.StartDateStr)}，文字与强调色",
            "考试模式：开关、开考自动进入、8 项字号、14 项颜色、倒计时字体",
            "每日一言：开关、字号斜体、颜色、API 地址、文本字段名、自动切换间隔",
            "天气：开关状态、字号与颜色、刷新间隔、详细度",
            "更新：自动检查开关、加速镜像开关与前缀",
            "远程控制台：开机自动启动服务、内置 PDF 阅读器开关",
            $"选科：恢复为默认 {defaults.Subjects.Count} 项（当前 {s.Subjects?.Count ?? 0} 项）",
        };

        if (s.DiagDesktopTreeDepth != defaults.DiagDesktopTreeDepth)
            list.Add($"诊断包：目录树深度 → {defaults.DiagDesktopTreeDepth} 层（当前 {s.DiagDesktopTreeDepth} 层）");

        return list;
    }

    /// <summary>**不受影响**的东西（存在独立文件里）—— 明确写出来，老师才敢按</summary>
    public static List<string> UntouchedItems() => new()
    {
        "课表 schedule.json（含作息时段模板、调休补课日）",
        "自动化任务规则 automations.json",
        "远程登录状态 tokens.json、PDF 续读进度 pdf-state.json",
        "课件打开顺序 open-state.json、已记录的上课活动 records/",
    };

    /// <summary>组装成确认框正文。备份路径为空时不提备份那一段。</summary>
    public static string DescribeImpact(AppSettings s, string? backupPath)
    {
        var sb = new StringBuilder();

        void Section(string title, IEnumerable<string> items, string fallback)
        {
            sb.AppendLine(title);
            bool any = false;
            foreach (var it in items) { sb.AppendLine("  · " + it); any = true; }
            if (!any) sb.AppendLine("  " + fallback);
            sb.AppendLine();
        }

        Section("■ 会被清除（丢数据，需重新设置）", LossItems(s), "（当前没有这类自定义数据）");
        Section("■ 会被恢复成出厂默认", ResetItems(s), "（无）");
        Section("■ 不受影响（存在独立文件里）", UntouchedItems(), "（无）");

        sb.Append(string.IsNullOrEmpty(backupPath)
            ? "⚠ 重置不可撤销。"
            : $"⚠ 重置不可撤销 —— 但会自动留一份备份：\n   {backupPath}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 重置前把当前 settings.json 复制到 `backups/reset-yyyyMMdd_HHmmss/settings.json`。
    /// 用与课表编辑器「备份数据」相同的目录约定 → 出问题了可以用「恢复数据」直接选回去。
    /// </summary>
    /// <param name="settingsPath">被备份的文件（通常是程序目录 settings.json）</param>
    /// <param name="baseDir">程序目录（备份写到其下 backups/）</param>
    /// <returns>备份出的文件全路径；失败返回 null，并把原因写进 error</returns>
    public static string? Backup(string settingsPath, string baseDir, DateTime now, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(settingsPath))
            {
                error = "settings.json 不存在（没有可备份的内容）";
                return null;
            }

            string stamp = now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.Combine(BackupRoot(baseDir), "reset-" + stamp);
            Directory.CreateDirectory(dir);

            string dest = Path.Combine(dir, Path.GetFileName(settingsPath));
            File.Copy(settingsPath, dest, overwrite: true);

            // 备份要能一眼认出来 —— 同目录放一个说明，别让老师在一堆 stamp 目录里猜
            try
            {
                File.WriteAllText(Path.Combine(dir, "说明.txt"),
                    $"这是「恢复默认设置」前的自动备份。\r\n" +
                    $"时间：{now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"内容：{Path.GetFileName(settingsPath)}（重置前的全部设置，含老师账号 / 选科 / 自定义倒计时）\r\n" +
                    $"恢复方法：设置 → 课表 → 「恢复数据」，选中本目录下的 {Path.GetFileName(settingsPath)}。\r\n");
            }
            catch { /* 说明文件写不出不影响备份本身 */ }

            TrimBackups(baseDir);
            return dest;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>重置前的自动备份只保留最近 5 份（按目录名倒序 = 时间倒序），避免长期堆积</summary>
    public static void TrimBackups(string baseDir, int keep = 5)
    {
        try
        {
            var root = BackupRoot(baseDir);
            if (!Directory.Exists(root)) return;
            var dirs = Directory.GetDirectories(root, "reset-*")
                .OrderByDescending(d => d)   // 目录名含时间戳，字典序即时间序
                .Skip(keep);
            foreach (var d in dirs)
            {
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
        catch { }
    }
}
