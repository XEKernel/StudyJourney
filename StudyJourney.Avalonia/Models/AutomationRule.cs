using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StudyJourney.Avalonia.Models
{
    // ── 自动化任务：拼图式规则（触发拼块 + 动作拼块）────────────────

    /// <summary>触发拼块类型。
    /// ⚠ 同样以整数落盘 automations.json，只能末尾追加，不能重排。</summary>
    public enum AutomationTriggerKind
    {
        FixedTime,        // 0 固定时间（每天/指定星期）
        BeforeClassStart, // 1 上课前 N 分钟（可选科目；空=每一节普通课）
        AtClassEnd,       // 2 下课时（可选科目；空=每节课）
        AtDayEnd,         // 3 放学时（当天最后一节下课）
        Idle,             // 4 闲置 N 分钟无操作
        AppStarted,       // 5 软件启动后 N 分钟（本次运行一次）
        AtMorningDayEnd,  // 6 上午放学（上午最后一节下课，后接长间隔；2.8，追加于末尾）
    }

    /// <summary>动作拼块类型。
    /// ⚠ 枚举以整数落盘 automations.json，**只能往末尾追加新值，不能重排/删除**，
    /// 否则老配置文件的动作会被解析成别的类型（2.5.7 追加 CloseApp 时的硬约束）。</summary>
    public enum AutomationActionKind
    {
        OpenFile,        // 0 打开文件（系统默认程序）
        OpenCourseware,  // 1 打开「上传目录\课件\<科目>」里的课件（按顺序记忆，见 AutomationRule.RememberLast）
        PlayAudio,       // 2 播放音频（默认播放器）
        ScreenOff,       // 3 熄屏（只关显示器，不动系统；触屏/鼠标即唤醒）
        Shutdown,        // 4 关机（倒计时可取消：shutdown /a）
        Restart,         // 5 重启
        ShowMessage,     // 6 弹出提醒
        CloseApp,        // 7 关闭软件（按进程名结束；2.5.7，追加于末尾）
        OpenWhiteboard,  // 8 打开白板（板书；2026-09-15，追加于末尾）
    }

    /// <summary>
    /// 一条自动化规则 = [触发拼块] + [动作拼块]，扁平字段方便 JSON 持久化与拼图式 UI 直接编辑。
    /// </summary>
    public class AutomationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // ── 触发拼块 ──
        public AutomationTriggerKind TriggerKind { get; set; } = AutomationTriggerKind.FixedTime;
        /// <summary>固定时间触发用："HH:mm"</summary>
        public string TriggerTime { get; set; } = "08:00";
        /// <summary>BeforeClassStart=提前分钟；AtClassEnd=延后分钟；Idle=闲置分钟；AppStarted=启动后延迟分钟</summary>
        public int TriggerMinutes { get; set; } = 5;
        /// <summary>科目过滤（空=全部）：BeforeClassStart/AtClassEnd 用。真实课程名从课表取，不受此限制</summary>
        public string TriggerSubject { get; set; } = "";
        /// <summary>星期集合（1=周一..7=周日），仅 FixedTime 用；空=每天</summary>
        public List<int> TriggerDays { get; set; } = new();

        // ── 动作拼块 ──
        public AutomationActionKind ActionKind { get; set; } = AutomationActionKind.OpenFile;
        /// <summary>OpenFile / PlayAudio 用的文件路径</summary>
        public string ActionPath { get; set; } = "";
        /// <summary>Shutdown / Restart 倒计时秒数（默认 60，期间 shutdown /a 可取消）</summary>
        public int ActionDelaySeconds { get; set; } = 60;
        /// <summary>ShowMessage 提醒内容（空=用规则名）</summary>
        public string ActionMessage { get; set; } = "";
        /// <summary>CloseApp 要关闭的进程名（如 POWERPNT.exe / wps.exe）。不带 .exe 也可，执行时自动补</summary>
        public string CloseTarget { get; set; } = "";

        // ── 打开类动作的智能行为（2.5.8 / 2.5.9）──────────────
        /// <summary>顺序记忆：自动打开"上次用到的文件"，老师手动打开别的文件即跟随推进（默认开）</summary>
        public bool RememberLast { get; set; } = true;
        /// <summary>纯自动顺次：每次触发自动指向序列下一份（听力场景；默认关 = 由老师手动推进）</summary>
        public bool AutoAdvance { get; set; } = false;
        /// <summary>目标已打开时：true = 激活到前台，false = 直接跳过（连堂防重复打开，默认跳过）</summary>
        public bool ActivateIfOpen { get; set; } = false;

        // ── 人类可读摘要（列表/日志用）──────────────────
        public string TriggerText => DescribeTrigger();
        public string ActionText => DescribeAction();
        public string Summary => $"{TriggerText} → {ActionText}";

        private static readonly string[] WeekNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

        private string DayText()
        {
            if (TriggerDays == null || TriggerDays.Count == 0) return "每天";
            if (TriggerDays.Count == 7) return "每天";
            // 周一到周五全选 = 工作日
            if (TriggerDays.Count == 5 && Enumerable.Range(1, 5).All(TriggerDays.Contains)) return "工作日";
            var set = TriggerDays.OrderBy(d => d).ToList();
            return string.Join("、", set.Where(d => d is >= 1 and <= 7).Select(d => WeekNames[d - 1]));
        }

        private string SubjectText()
            => string.IsNullOrWhiteSpace(TriggerSubject) ? "" : $"《{TriggerSubject}》";

        private string DescribeTrigger()
        {
            switch (TriggerKind)
            {
                case AutomationTriggerKind.FixedTime:
                    return $"{DayText()} {TriggerTime}";
                case AutomationTriggerKind.BeforeClassStart:
                    return $"{SubjectText()}{ (string.IsNullOrWhiteSpace(TriggerSubject) ? "每节课" : "课") }上课前 {Math.Max(TriggerMinutes, 0)} 分钟";
                case AutomationTriggerKind.AtClassEnd:
                    return SubjectText() + (string.IsNullOrWhiteSpace(TriggerSubject) ? "每节课" : "课") +
                           (TriggerMinutes > 0 ? $"下课后 {TriggerMinutes} 分钟" : "下课时");
                case AutomationTriggerKind.AtDayEnd:
                    return "放学时（当天最后一节下课）";
                case AutomationTriggerKind.AtMorningDayEnd:
                    return "上午放学时（上午最后一节下课）";
                case AutomationTriggerKind.Idle:
                    return $"闲置 {Math.Max(TriggerMinutes, 0)} 分钟无操作";
                case AutomationTriggerKind.AppStarted:
                    return TriggerMinutes > 0 ? $"软件启动 {TriggerMinutes} 分钟后" : "软件启动时";
                default:
                    return "";
            }
        }

        private string DescribeAction()
        {
            switch (ActionKind)
            {
                case AutomationActionKind.OpenFile:
                    if (string.IsNullOrWhiteSpace(ActionPath)) return "打开文件（未选文件）";
                    return $"打开文件 {Path.GetFileName(ActionPath)}{MemoryHint()}";
                case AutomationActionKind.OpenCourseware:
                    return "打开当堂科目课件" + (AutoAdvance ? "（自动顺次下一份）" : RememberLast ? "（续用上次那份）" : "（编号第一份）");
                case AutomationActionKind.PlayAudio:
                    if (string.IsNullOrWhiteSpace(ActionPath)) return "播放音频（未选文件）";
                    return $"播放音频 {Path.GetFileName(ActionPath)}{MemoryHint()}";
                case AutomationActionKind.ScreenOff:
                    return "关闭屏幕（熄屏）";
                case AutomationActionKind.Shutdown:
                    return $"关机（{Math.Max(ActionDelaySeconds, 30)} 秒倒计时）";
                case AutomationActionKind.Restart:
                    return $"重启（{Math.Max(ActionDelaySeconds, 30)} 秒倒计时）";
                case AutomationActionKind.ShowMessage:
                    return "弹出提醒：" + (string.IsNullOrWhiteSpace(ActionMessage) ? Name : ActionMessage);
                case AutomationActionKind.CloseApp:
                    return string.IsNullOrWhiteSpace(CloseTarget) ? "关闭软件（未选目标）" : $"关闭软件 {CloseTarget}";
                case AutomationActionKind.OpenWhiteboard:
                    return "打开白板（板书）";
                default:
                    return "";
            }
        }

        /// <summary>打开类动作的统一记忆提示（顺序记忆开着时的轻标记）</summary>
        private string MemoryHint()
            => !RememberLast ? "" : AutoAdvance ? "（自动顺次下一份）" : "（续用上次那份）";
    }

    /// <summary>自动化任务容器：全局总开关 + 规则列表，独立存 automations.json（不混入 settings.json，
    /// 「恢复默认设置」不会误删老师的规则；随软件文件夹分发，与 schedule.json 一致）</summary>
    public class AutomationSettings
    {
        /// <summary>全局总开关（默认关，用户在设置页显式开启才生效，避免新功能吓到老师）</summary>
        public bool Enabled { get; set; } = false;
        public List<AutomationRule> Rules { get; set; } = new();
    }

    public static class AutomationStore
    {
        private static readonly string StorePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "automations.json");

        public static string FilePath => StorePath;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static AutomationSettings Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    return JsonSerializer.Deserialize<AutomationSettings>(json, _jsonOpts)
                           ?? new AutomationSettings();
                }
            }
            catch (Exception ex)
            {
                // 备份损坏文件后重建（保留最近 3 份）
                try
                {
                    var bak = StorePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(StorePath, bak, overwrite: true);
                    TrimCorruptedBackups();
                }
                catch { }
                Helpers.AppLogger.Warn($"automations.json 加载失败，使用默认: {ex.Message}");
            }
            return new AutomationSettings();
        }

        public static void Save(AutomationSettings data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, _jsonOpts);
                Helpers.FileAtomic.WriteAllText(StorePath, json);   // 原子写，防半截 JSON
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Error("保存 automations.json 失败", ex);
            }
        }

        private static void TrimCorruptedBackups(int maxCount = 3)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (string.IsNullOrEmpty(dir)) return;
                Directory.GetFiles(dir, Path.GetFileName(StorePath) + ".corrupted.*")
                    .OrderByDescending(f => f)
                    .Skip(maxCount)
                    .ToList()
                    .ForEach(f => { try { File.Delete(f); } catch { } });
            }
            catch { }
        }
    }
}
