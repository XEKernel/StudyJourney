using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Services;

/// <summary>
/// 自动化任务服务：把「到点/状态 → 执行动作」做成通用拼图式规则引擎。
/// 每条规则 = 触发拼块 + 动作拼块（AutomationRule），落盘 automations.json。
///
/// 调度模型（复用 ReminderService 的周期轮询 + #11 触发窗口）：
///  - DispatcherTimer 每 1 秒轮询所有启用规则
///  - 固定时间 / 课表事件（上课前·下课·放学）按 IsInTriggerWindow（前 0.5s~后 90s 补发）命中
///  - 闲置（GetLastInputInfo）按"本轮无操作时长 ≥ N 分钟"命中，用户一操作即重新武装
///  - 软件启动后（AppStarted）本次运行仅触发一次
///  - 幂等：每天零点清空当日已触发集合；同一天同一规则不同课节互不冲突
///
/// 屏蔽规则：
///  - 考试模式激活中（ExamModeWindow.IsExamModeActive，A1 修复：运行时状态而非持久化开关
///    EnableExamMode——后者班级常年开启，误用会导致课表/闲置规则全年静默失效）：
///    跳过课表事件与闲置触发（考试进行时不按课表自动开课件/熄屏）
///  - 闲置触发：**2.5.7c 起上课时段也生效**（老师上课不用电脑但没关 → 到点照样熄屏），
///    仅保留考试模式屏蔽；闲置→熄屏会先弹可取消的倒计时提示（Views/ScreenOffPromptWindow）
///  - 上课前自动开课件：跳过"自习/班会"（非授课课节不打扰）
///  - 固定时间触发不屏蔽（放学关机在考试日依然有意义）
///
/// 打开类动作的智能行为（2.5.8 / 2.5.9）：
///  - 顺序记忆：目标 = 教师端指定（一次性）&gt; 上次用到的文件（AutoAdvance 时取序列下一份）&gt; 初始候选
///  - 连堂幂等：目标已打开（进程存活 / 窗口标题命中）→ 跳过，避免连堂课重复弹窗
///  - 运行期状态（指针 / 已开进程 / 待打开指定）存 open-state.json，与规则定义分离——
///    设置页是"克隆→编辑→整表写回"模式，混在一起会被保存操作覆盖掉指针
/// </summary>
public class AutomationService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    // 熄屏：向所有窗口广播 SC_MONITORPOWER=2 关闭显示器（只关屏不睡系统，鼠标/触屏即唤醒）。
    // A5 修复：SendMessageTimeout + SMTO_ABORTIFHUNG —— 原 SendMessage(HWND_BROADCAST) 同步等待
    // 系统内所有顶层窗口处理完毕，任一窗口挂起（老 WPS/浏览器假死）会卡死 UI 线程；
    // 带超时且自动跳过挂起窗口，100ms 足够（熄屏是即发即弃指令）。
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private readonly ScheduleManager _manager;
    private AutomationSettings _data;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedAt = DateTime.Now;

    // 幂等：当日已触发 key（每天零点清空）；Idle 每段连续闲置独立 key；AppStarted 本次运行只触发一次
    private readonly HashSet<string> _firedKeys = new();
    private readonly HashSet<string> _appStartedDone = new();

    /// <summary>S6：上次关机/重启执行时刻 —— 同一时刻多条规则命中时避免连续调用（第二次会重置倒计时）</summary>
    private DateTime _lastShutdownAt = DateTime.MinValue;
    private DateTime _lastClearDay = DateTime.Today;

    /// <summary>2.5.8：上次扫描"老师手动打开了哪个文件"的时刻（窗口标题枚举较重，5 秒一次即可）</summary>
    private DateTime _lastManualScan = DateTime.MinValue;
    private const int ManualScanSeconds = 5;

    /// <summary>「上午放学」的时间上界：上一节结束时间早于此才算中午放学（不是傍晚的长间隔）。
    /// 与 ReminderService.NoonCutoff 保持同一语义。</summary>
    private static readonly TimeSpan NoonCutoff = TimeSpan.FromHours(13);

    /// <summary>规则容器变更后触发（设置页保存后调用 Reload 刷新内存即可，无需重启服务）</summary>
    public event Action? DataChanged;

    public AutomationSettings Data => _data;

    public AutomationService(ScheduleManager manager)
    {
        _manager = manager;
        _data = AutomationStore.Load();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public bool GlobalEnabled => _data.Enabled;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    /// <summary>从磁盘重载规则（设置页保存后调用；总开关/规则立即生效）</summary>
    public void Reload()
    {
        _data = AutomationStore.Load();
        DataChanged?.Invoke();
        Helpers.AppLogger.Info($"自动化规则已重载：总开关={_data.Enabled}，规则 {_data.Rules.Count} 条");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;

        // 每天零点清空"当日触发"集合（Idle 无日期概念，但 key 含 lastInput 时间戳天然分段）
        if (now.Date != _lastClearDay)
        {
            _firedKeys.Clear();
            _lastClearDay = now.Date;
        }

        if (!_data.Enabled) return;
        if (_data.Rules.Count == 0) return;

        // 2.5.8：定期扫描顶层窗口标题，捕获"老师自己打开/切换了哪份文件" → 更新顺序记忆指针。
        // 与 2.5.9 连堂幂等共用同一套信号（窗口标题含文件名）。
        if ((now - _lastManualScan).TotalSeconds >= ManualScanSeconds)
        {
            _lastManualScan = now;
            try { CaptureManualOpens(); }
            catch (Exception ex) { Helpers.AppLogger.Warn($"顺序记忆扫描失败: {ex.Message}"); }
        }

        foreach (var rule in _data.Rules)
        {
            if (!rule.Enabled) continue;
            try { TryTrigger(rule, now); }
            catch (Exception ex)
            {
                Helpers.AppLogger.Error($"自动化规则「{rule.Name}」执行异常: {ex.Message}", ex);
            }
        }
    }

    // ── 触发判定 ──────────────────────────────────────────────

    private void TryTrigger(AutomationRule rule, DateTime now)
    {
        var dayKey = now.ToString("yyyyMMdd");

        switch (rule.TriggerKind)
        {
            case AutomationTriggerKind.FixedTime:
            {
                if (!DayMatches(rule, now)) return;
                if (!TimeSpan.TryParse(rule.TriggerTime, out var t)) return;
                var trigger = now.Date + t;
                // A6 修复：key 含触发时间 —— 当天已触发后改时间保存，新时刻应能再次触发
                //（原 key 与时间无关，改时间会被旧 key 拦下，规则静默失效）
                if (RuleFired(rule, $"{dayKey}_fixed_{rule.TriggerTime}")) return;
                if (ReminderService.IsInTriggerWindow(now, trigger))
                    Fire(rule, $"{dayKey}_fixed_{rule.TriggerTime}", null);
                break;
            }

            case AutomationTriggerKind.BeforeClassStart:
            case AutomationTriggerKind.AtClassEnd:
            case AutomationTriggerKind.AtDayEnd:
            case AutomationTriggerKind.AtMorningDayEnd:
            {
                // 考试模式激活中：课表被顶替，不按课表触发（固定时间类任务不受影响）。
                // A1 修复：用 ExamModeWindow.IsExamModeActive（运行时状态）而非 App.Settings.EnableExamMode（持久化开关）
                if (Views.ExamModeWindow.IsExamModeActive) return;
                TryTriggerScheduleEvent(rule, now, dayKey);
                break;
            }

            case AutomationTriggerKind.Idle:
            {
                // 2.5.7c：闲置熄屏改为**上课时段也生效**（老师上课不用电脑但没关 → 闲置到点照样熄屏）。
                // 仅保留考试模式屏蔽（考试大屏在放倒计时，熄屏会打断）。
                if (Views.ExamModeWindow.IsExamModeActive) return;
                // 防御：0 分钟闲置无意义（用户一直有输入也恒满足 → 每次输入都触发），最低 1 分钟
                if (rule.TriggerMinutes < 1) return;

                var idle = GetIdleSeconds();
                if (idle < rule.TriggerMinutes * 60L) return;
                // 每段"连续闲置"只触发一次：以该段起点（lastInput 时刻）做 key
                long bucket = GetLastInputTicks() / 1000;
                string key = $"{dayKey}_idle_{bucket}";
                if (RuleFired(rule, key)) return;
                Fire(rule, key, null);
                break;
            }

            case AutomationTriggerKind.AppStarted:
            {
                if (_appStartedDone.Contains(rule.Id)) return;
                var due = _startedAt.AddMinutes(Math.Max(rule.TriggerMinutes, 0));
                if (now < due) return;
                _appStartedDone.Add(rule.Id);
                Fire(rule, $"appstart_{rule.Id}", null);
                break;
            }
        }
    }

    private void TryTriggerScheduleEvent(AutomationRule rule, DateTime now, string dayKey)
    {
        var entries = _manager.GetTodayEntries(now.Date);
        if (entries.Count == 0) return;   // 今天没课（周末/节假日）→ 课表事件不触发

        var matching = new List<ScheduleEntry>();
        if (rule.TriggerKind == AutomationTriggerKind.AtDayEnd)
        {
            // 放学 = 当天最后一节（跨天课用真实结束时刻）
            var last = entries[entries.Count - 1];
            matching.Add(last);
        }
        else if (rule.TriggerKind == AutomationTriggerKind.AtMorningDayEnd)
        {
            // 2.8 上午放学 = 上午最后一节（其后是 ≥60 分钟长间隔）且**结束时间早于 13:00**。
            // 时间条件是必须的：傍晚的长间隔（下午最后一节 → 晚自习）不是"中午放学"，
            // 否则「上午放学」触发块会在傍晚误触发（与 ReminderService 的判定保持一致）。
            for (int i = 0; i < entries.Count - 1; i++)
            {
                var endActual = entries[i].GetEndDateTimeActual(now.Date);
                var gap = entries[i + 1].GetStartDateTime(now.Date) - endActual;
                if (gap >= TimeSpan.FromMinutes(60) && endActual.TimeOfDay < NoonCutoff)
                {
                    matching.Add(entries[i]);
                    break;
                }
            }
        }
        else
        {
            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(rule.TriggerSubject) &&
                    !string.Equals(e.Subject, rule.TriggerSubject, StringComparison.Ordinal))
                    continue;
                // 上课前自动开课件：自习/班会不算授课，跳过（下课/放学的铃不受限）
                if (rule.TriggerKind == AutomationTriggerKind.BeforeClassStart &&
                    (e.Subject == "自习" || e.Subject == "班会"))
                    continue;
                matching.Add(e);
            }
        }

        foreach (var entry in matching)
        {
            DateTime trigger;
            string evKey;
            if (rule.TriggerKind == AutomationTriggerKind.BeforeClassStart)
            {
                trigger = entry.GetStartDateTime(now.Date)
                          .AddMinutes(-Math.Max(rule.TriggerMinutes, 0));
                // A6：key 含提前量（与 FixedTime 同理，当天改提前量应能重触发）
                evKey = $"{dayKey}_s{entry.Period}_{entry.StartTimeStr.Replace(":", "")}_{rule.TriggerMinutes}";
            }
            else if (rule.TriggerKind == AutomationTriggerKind.AtClassEnd)
            {
                trigger = entry.GetEndDateTimeActual(now.Date)
                          .AddMinutes(Math.Max(rule.TriggerMinutes, 0));
                // A6：key 含延后分钟（同上）
                evKey = $"{dayKey}_e{entry.Period}_{entry.EndTimeStr.Replace(":", "")}_{rule.TriggerMinutes}";
            }
            else if (rule.TriggerKind == AutomationTriggerKind.AtDayEnd)
            {
                trigger = entry.GetEndDateTimeActual(now.Date);
                evKey = $"{dayKey}_dayend";
            }
            else // AtMorningDayEnd
            {
                trigger = entry.GetEndDateTimeActual(now.Date);
                evKey = $"{dayKey}_amdayend";
            }

            if (RuleFired(rule, evKey)) continue;
            if (!ReminderService.IsInTriggerWindow(now, trigger)) continue;
            Fire(rule, evKey, entry.Subject);
        }
    }

    /// <summary>命中后执行：记录 fired key + 分发动作（ctx.Subject = 当堂科目，供课件目录动作使用）</summary>
    private void Fire(AutomationRule rule, string key, string? subject)
    {
        _firedKeys.Add($"{rule.Id}:{key}");
        Helpers.AppLogger.Info($"自动化触发「{rule.Name}」：{rule.Summary}");
        Dispatcher.UIThread.Post(() => Execute(rule, subject));
    }

    private bool RuleFired(AutomationRule rule, string key) => _firedKeys.Contains($"{rule.Id}:{key}");

    /// <summary>
    /// 规则配的星期几是否命中"今天"。
    ///
    /// ⚠ 2026-09-21：改成走 <see cref="ScheduleManager.GetEffectiveDayOfWeek"/> 而不是
    /// 直接的 `now.DayOfWeek` —— 调休补课时（如周日补周五的课），周五中午的听力规则
    /// （TriggerDays=[5]）必须照常触发，否则调休日就"什么都没发生"（用户实际反馈）。
    /// 该映射同时负责周日 0→7 的归一化。
    /// </summary>
    private bool DayMatches(AutomationRule rule, DateTime now)
    {
        if (rule.TriggerDays == null || rule.TriggerDays.Count == 0) return true;
        return rule.TriggerDays.Contains(_manager.GetEffectiveDayOfWeek(now));
    }

    // ── 动作执行 ──────────────────────────────────────────────

    /// <summary>
    /// 执行动作。subject = 触发时刻的当堂科目（课表事件触发才有；"打开课件目录"用它定位科目文件夹）。
    /// 全部动作失败兜底：写日志 + 弹提示，不崩溃。
    /// </summary>
    private void Execute(AutomationRule rule, string? subject)
    {
        try
        {
            switch (rule.ActionKind)
            {
                // 2.5.8：三类打开动作统一走"顺序记忆"解析（教师指定 > 记忆指针 > 初始候选）
                case AutomationActionKind.OpenFile:
                case AutomationActionKind.PlayAudio:
                case AutomationActionKind.OpenCourseware:
                    OpenResolved(rule, subject);
                    break;

                case AutomationActionKind.ScreenOff:
                    // 2.5.7c：闲置触发的熄屏先弹可取消的倒计时提示（防放 PPT 时被突然黑屏）；
                    // 其它触发（固定时间/课表事件）保持立即熄屏，语义清晰
                    if (rule.TriggerKind == AutomationTriggerKind.Idle)
                        PromptThenScreenOff(rule);
                    else
                    {
                        ScreenOff();
                        Helpers.AppLogger.Info($"自动化「{rule.Name}」：已熄屏");
                    }
                    break;

                case AutomationActionKind.Shutdown:
                case AutomationActionKind.Restart:
                    // S6 修复：10 秒内已执行过关机/重启则跳过（多规则同时命中时重复调用会重置系统倒计时）
                    if ((DateTime.Now - _lastShutdownAt).TotalSeconds < 10)
                    {
                        Helpers.AppLogger.Warn($"自动化「{rule.Name}」：10 秒内已有一次关机/重启，跳过重复执行");
                        break;
                    }
                    _lastShutdownAt = DateTime.Now;
                    SystemShutdown(rule);
                    break;

                case AutomationActionKind.ShowMessage:
                    _ = App.ShowMessageAsync(rule.Name,
                        string.IsNullOrWhiteSpace(rule.ActionMessage) ? rule.Name : rule.ActionMessage);
                    break;

                case AutomationActionKind.CloseApp:
                    CloseApp(rule);
                    break;

                case AutomationActionKind.OpenWhiteboard:
                    // 打开白板板书（2026-09-15 新增）。开窗必须回 UI 线程 ——
                    // 本服务是 DispatcherTimer（已在 UI 线程），但显式封送更稳，也便于以后换调度方式
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            App.OpenWhiteboardGlobal();
                            Helpers.AppLogger.Info($"自动化「{rule.Name}」：已打开白板");
                        }
                        catch (Exception ex)
                        {
                            Helpers.AppLogger.Error($"自动化「{rule.Name}」打开白板失败", ex);
                        }
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error($"自动化动作「{rule.Name}」执行失败: {ex.Message}", ex);
        }
    }

    // ── 打开类动作：顺序记忆（2.5.8）+ 连堂幂等（2.5.9A）────────────

    /// <summary>该规则的"候选文件目录"：科目课件取「上传目录\课件\科目」；文件/音频取所填路径的所在目录</summary>
    private static string ResolveDirectory(AutomationRule rule, string? subject)
    {
        if (rule.ActionKind == AutomationActionKind.OpenCourseware)
        {
            string subj = string.IsNullOrWhiteSpace(subject)
                ? rule.TriggerSubject?.Trim() ?? ""
                : subject.Trim();
            var root = Path.Combine(Services.HttpServerService.UploadRootPath, "课件");
            return string.IsNullOrEmpty(subj) ? root : Path.Combine(root, subj);
        }
        var p = rule.ActionPath?.Trim() ?? "";
        return p.Length == 0 ? "" : (Path.GetDirectoryName(p) ?? "");
    }

    /// <summary>
    /// 解析并打开目标文件：
    ///   目标优先级 = 教师端指定（一次性） &gt; 顺序记忆指针（AutoAdvance 时取序列下一份） &gt; 初始候选
    ///   初始候选   = 科目课件 → 目录内编号第一份；文件/音频 → 老师填的路径（不存在再退编号第一份）
    ///   连堂幂等   = 目标已开着 → 跳过（或激活到前台，看规则开关 ActivateIfOpen）
    /// 打开成功后记入跟踪表（进程 Id，供幂等判断）并更新记忆指针。
    /// </summary>
    private void OpenResolved(AutomationRule rule, string? subject)
    {
        // ① 教师端网页指定优先（一次性消费）：软件 → 直接启动；文件 → 作为本次目标
        var pending = OpenStateStore.ConsumePending(subject);
        string? target = null;
        if (pending != null && string.Equals(pending.Kind, "app", StringComparison.OrdinalIgnoreCase))
        {
            LaunchPendingApp(rule, pending);
            return;
        }
        if (pending != null && File.Exists(pending.Path))
        {
            target = pending.Path;
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：采用教师端指定文件 {target}");
        }

        var candidates = Helpers.FileSequence.ListCandidates(ResolveDirectory(rule, subject));

        // ② 顺序记忆指针（默认续用"上次那份"；开了自动顺次则推下一份）
        if (target == null && rule.RememberLast)
        {
            var ptr = OpenStateStore.GetPointer(rule.Id);
            if (!string.IsNullOrWhiteSpace(ptr) && File.Exists(ptr))
                target = rule.AutoAdvance ? (Helpers.FileSequence.Next(ptr, candidates) ?? ptr) : ptr;
        }

        // ③ 初始候选
        if (target == null)
        {
            var configured = rule.ActionPath?.Trim() ?? "";
            target = rule.ActionKind == AutomationActionKind.OpenCourseware
                ? Helpers.FileSequence.First(candidates)
                : (File.Exists(configured) ? configured : Helpers.FileSequence.First(candidates));
        }

        if (target == null)
        {
            var dir = ResolveDirectory(rule, subject);
            Helpers.AppLogger.Warn($"自动化「{rule.Name}」：没有可打开的文件（目录 {dir}）");
            _ = App.ShowMessageAsync("自动化任务",
                $"「{rule.Name}」没有可打开的文件。\n目录：{dir}\n\n请先把课件投递到该文件夹。");
            return;
        }

        // ④ 连堂幂等：已经开着就不再打开（老师下节课继续用同一份课件，不该弹第二个窗口）
        if (IsAlreadyOpen(target))
        {
            if (rule.ActivateIfOpen)
            {
                bool ok = Helpers.WindowEnumerator.TryActivateWindow(target);
                Helpers.AppLogger.Info($"自动化「{rule.Name}」：{Path.GetFileName(target)} 已打开，已尝试激活窗口（{ok}）");
            }
            else
            {
                Helpers.AppLogger.Info($"自动化「{rule.Name}」：{Path.GetFileName(target)} 已打开，跳过重复打开");
            }
            OpenStateStore.SetPointer(rule.Id, target);
            return;
        }

        // ⑤ 真正打开
        try
        {
            var proc = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            if (proc != null)
            {
                try { OpenStateStore.TrackOpen(target, proc.Id); } catch { }
            }
            OpenStateStore.SetPointer(rule.Id, target);   // 顺序记忆：记下"这次用的是哪份"
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：打开 {target}");
        }
        catch (Exception ex)
        {
            // A9：打开失败（无关联程序/关联损坏等）弹提示，不只写日志
            Helpers.AppLogger.Error($"自动化「{rule.Name}」：打开失败 {target}: {ex.Message}", ex);
            _ = App.ShowMessageAsync("自动化任务",
                $"「{rule.Name}」无法打开文件：\n{target}\n\n{ex.Message}\n\n请检查系统里是否有能打开此类型文件的程序。");
        }
    }

    /// <summary>目标是否已经打开：先看"我们开的那个进程还活着"，再兜底按窗口标题匹配（覆盖老师自己双击打开的）</summary>
    private static bool IsAlreadyOpen(string path)
    {
        if (OpenStateStore.TryGetAlivePid(path, out _)) return true;
        return Helpers.WindowEnumerator.IsFileOpenInWindow(path, out _);
    }

    /// <summary>
    /// 2.5.8：扫描顶层窗口，找出"老师此刻实际开着"的那份文件 → 更新该规则的记忆指针。
    /// 这是"手动管推进"的落地方式：老师开哪份，下次自动触发就续用哪份。
    ///
    /// 性能：窗口枚举只做一次，全部标题拼成一个串（BuildTitleBlob），
    /// 之后每条规则/每个候选文件都只是一次 string.Contains —— 原实现是"候选文件 × 窗口"双层循环。
    /// 目录列表按"最后写入时间"做 10 秒缓存，避免每 5 秒对每个目录做一次文件系统遍历。
    /// </summary>
    private void CaptureManualOpens()
    {
        var rules = _data.Rules
            .Where(r => r.Enabled && r.RememberLast &&
                        r.ActionKind is AutomationActionKind.OpenFile
                                     or AutomationActionKind.OpenCourseware
                                     or AutomationActionKind.PlayAudio)
            .ToList();
        if (rules.Count == 0) return;

        var windows = Helpers.WindowEnumerator.TopLevelWindows();
        if (windows.Count == 0) return;

        var titleBlob = Helpers.WindowEnumerator.BuildTitleBlob(windows);

        foreach (var rule in rules)
        {
            var candidates = GetCachedCandidates(ResolveDirectory(rule, null));
            if (candidates.Count == 0) continue;

            var hit = Helpers.WindowEnumerator.FindOpenFileInBlob(titleBlob, candidates);
            if (hit != null)
                OpenStateStore.SetPointer(rule.Id, hit);
        }
    }

    // ── 目录候选缓存（M2：把每 5 秒一次的磁盘遍历压到 10 秒 + 按目录去重）────────

    private readonly Dictionary<string, (List<string> Files, DateTime Stamp)> _candidateCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int CandidateCacheSeconds = 10;

    /// <summary>取目录候选文件（带短时缓存）。目录不存在/读取失败返回空列表。</summary>
    private List<string> GetCachedCandidates(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return new List<string>();

        var now = DateTime.Now;
        if (_candidateCache.TryGetValue(directory, out var cached) &&
            (now - cached.Stamp).TotalSeconds < CandidateCacheSeconds)
            return cached.Files;

        var files = Helpers.FileSequence.ListCandidates(directory);
        _candidateCache[directory] = (files, now);

        // 目录数有上限（规则数级），但仍做一次清理防止怪异场景下无界增长
        if (_candidateCache.Count > 64) _candidateCache.Clear();
        return files;
    }

    /// <summary>按教师端指定启动软件（2.5.9B 的"软件"分支）：直接拉起，不参与顺序记忆</summary>
    private static void LaunchPendingApp(AutomationRule rule, PendingOpen pending)
    {
        var path = pending.Path?.Trim() ?? "";
        if (path.Length == 0)
        {
            Helpers.AppLogger.Warn($"自动化「{rule.Name}」：教师指定的软件路径为空");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            OpenStateStore.AddKnownApp(path, Path.GetFileNameWithoutExtension(path));
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：按教师端指定启动软件 {path}");
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error($"自动化「{rule.Name}」：启动指定软件失败 {path}: {ex.Message}", ex);
            _ = App.ShowMessageAsync("自动化任务",
                $"「{rule.Name}」无法启动老师指定的软件：\n{path}\n\n{ex.Message}");
        }
    }

    // ── 熄屏前提示（2.5.7c）──────────────────────────────────

    /// <summary>熄屏提示倒计时秒数（老师可点「取消」保持亮屏）</summary>
    private const int ScreenOffPromptSeconds = 10;

    private bool _screenOffPromptShowing;

    /// <summary>闲置触发的熄屏：先弹可取消的倒计时提示，超时无人理会才真正熄屏。
    /// 同一时刻只允许一个提示（多条闲置规则同时命中时不叠窗）。</summary>
    private void PromptThenScreenOff(AutomationRule rule)
    {
        if (_screenOffPromptShowing)
        {
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：熄屏提示已在显示，忽略重复触发");
            return;
        }
        _screenOffPromptShowing = true;
        _ = RunScreenOffPromptAsync(rule);
    }

    private async Task RunScreenOffPromptAsync(AutomationRule rule)
    {
        bool confirmed;
        try
        {
            confirmed = await Views.ScreenOffPromptWindow.AskAsync(ScreenOffPromptSeconds);
        }
        catch (Exception ex)
        {
            // 提示窗异常不该阻断熄屏本身，按"继续熄屏"处理并留痕
            Helpers.AppLogger.Warn($"熄屏提示窗异常，直接熄屏: {ex.Message}");
            confirmed = true;
        }
        finally { _screenOffPromptShowing = false; }

        if (!confirmed)
        {
            // 取消后本次闲置段不再重复提示（该段 fired key 已记录），鼠标一动即进入新的一段
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：老师取消了本次熄屏");
            return;
        }
        ScreenOff();
        Helpers.AppLogger.Info($"自动化「{rule.Name}」：已熄屏");
    }

    /// <summary>关闭软件（2.5.7）：按进程名 taskkill。**不带 /F** —— 走 WM_CLOSE 让程序自己弹"是否保存"，
    /// 避免强杀导致 Office 未保存内容丢失；程序自身与资源管理器拒绝作为目标。</summary>
    private static void CloseApp(AutomationRule rule)
    {
        var name = (rule.CloseTarget ?? "").Trim();
        if (name.Length == 0)
        {
            Helpers.AppLogger.Warn($"自动化「{rule.Name}」：未指定要关闭的软件");
            _ = App.ShowMessageAsync("自动化任务", $"「{rule.Name}」还没选择要关闭的软件。");
            return;
        }
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (name.Equals("StudyJourney.Avalonia.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        {
            Helpers.AppLogger.Warn($"自动化「{rule.Name}」：拒绝关闭受保护进程 {name}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            Arguments = $"/IM \"{name}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            Process.Start(psi);
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：已请求关闭 {name}");
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error($"自动化「{rule.Name}」关闭 {name} 失败: {ex.Message}", ex);
            _ = App.ShowMessageAsync("自动化任务", $"「{rule.Name}」关闭 {name} 失败：{ex.Message}");
        }
    }

    private static void ScreenOff()
    {
        // A5：超时广播（SMTO_ABORTIFHUNG 跳过挂起窗口），不再阻塞
        SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SYSCOMMAND,
            new IntPtr(SC_MONITORPOWER), new IntPtr(2), SMTO_ABORTIFHUNG, 100, out _);
    }

    private static void SystemShutdown(AutomationRule rule)
    {
        bool restart = rule.ActionKind == AutomationActionKind.Restart;
        // 倒计时下限 30 秒（复核裁决：5 秒没有取消窗口，等同误关机；服务端与编辑器/摘要三处统一）
        int secs = Math.Max(rule.ActionDelaySeconds, 30);
        // A8 修复：规则名拼进 shutdown /c 参数，含双引号会破坏参数解析 → 剥离
        string comment = $"StudyJourney auto {(restart ? "restart" : "shutdown")}: {rule.Name}"
            .Replace("\"", "");
        var psi = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = $"{(restart ? "/r" : "/s")} /t {secs} /c \"{comment}\"",
            // A3 修复：CreateNoWindow 仅在 UseShellExecute=false 时生效；原 true+true 组合
            // 无效，关机/重启时投影上会闪黑色控制台窗口。shutdown.exe 无需 shell，普通权限可调度
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            Process.Start(psi);
            Helpers.AppLogger.Info($"自动化「{rule.Name}」：{(restart ? "重启" : "关机")}倒计时 {secs} 秒已启动");
        }
        catch (Exception ex)
        {
            // S6：失败给出可见提示（与文件类动作一致），便于老师发现策略/权限问题
            Helpers.AppLogger.Error($"自动化「{rule.Name}」{(restart ? "重启" : "关机")}失败: {ex.Message}", ex);
            _ = App.ShowMessageAsync("自动化任务", $"「{rule.Name}」{(restart ? "重启" : "关机")}执行失败：{ex.Message}");
        }
        // 倒计时期间可在命令行执行 shutdown /a 取消（系统也会弹出可关闭的"即将关机"通知）
    }

    // ── 闲置检测 ──────────────────────────────────────────────

    private static long GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        uint ticks = (uint)Environment.TickCount;
        uint idle = ticks >= info.dwTime ? ticks - info.dwTime : ticks + (uint.MaxValue - info.dwTime);
        return idle / 1000L;
    }

    private static long GetLastInputTicks()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return info.dwTime;
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Stop();
    }
}
