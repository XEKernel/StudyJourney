using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Services;

/// <summary>提醒事件参数</summary>
public class ReminderEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public ReminderType Type { get; }
    public ReminderEventArgs(ReminderType type, string title, string message)
    {
        Type = type; Title = title; Message = message;
    }
}

public enum ReminderType
{
    ClassStart,          // 上课时间到
    ClassMid,            // 上课后 20 分钟
    ClassEndSoon,        // 距下课还有 1 分钟（并触发 60s 倒计时）
    ClassEnd,            // 下课
    NextClassSoon,       // 距下节课还有 5 分钟
    DayEnd,              // 放学（最后一节下课）
    MorningStart,        // 早自习开始
    MorningEnd,          // 早自习结束
    EveningStart,        // 晚自习开始
    EveningEnd,          // 晚自习结束
    ReadingStart,        // 晚读开始
    ReadingEnd,          // 晚读结束
    ExamEndSoon,         // 考试还有 15 分钟结束
    MorningDayEnd,       // 2.8：中午放学（上午最后一节下课，后接长间隔）
}

/// <summary>
/// 提醒服务（Avalonia 版，逻辑对齐 WPF ReminderService）：
/// 每秒轮询课表，在关键时刻触发事件；声音用 user32 MessageBeep（Avalonia 无 SoundPlayer）。
/// </summary>
public class ReminderService : IDisposable
{
    // 系统蜂鸣（WPF SystemSounds.Asterisk 底层即 user32.MessageBeep）
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
    private const uint MB_ICONASTERISK = 0x40;

    // 自定义 wav 播放（winmm，替代 WPF SoundPlayer）
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundW(string? pszSound, IntPtr hmod, uint fdwSound);
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x0001;

    private readonly ScheduleManager _manager;
    // #3 修复：不再缓存 AppSettings 实例引用（重置设置/恢复备份会替换 App.Settings 实例，
    // 旧缓存将永远读不到新值）。一律经 App.Settings 属性动态读取。
    // 编码规约：任何长期存活组件不得缓存 App.Settings 实例引用。
    private readonly DispatcherTimer _timer;

    private readonly HashSet<string> _firedKeys = new();
    private DateTime _lastClearDay = DateTime.Today;

    private DateTime _cachedDay = DateTime.MinValue;
    private List<ScheduleEntry> _cachedEntries = new();
    private readonly Action _onDataChanged;

    public event EventHandler<ReminderEventArgs>? Reminder;

    public ReminderService(ScheduleManager manager)
    {
        _manager = manager;
        _onDataChanged = () => _cachedDay = DateTime.MinValue;
        _manager.DataChanged += _onDataChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;

        if (now.Date != _lastClearDay)
        {
            _firedKeys.Clear();
            _lastClearDay = now.Date;
        }

        if (_cachedDay != now.Date)
        {
            _cachedDay = now.Date;
            _cachedEntries = _manager.GetTodayEntries(now.Date);
        }
        if (_cachedEntries.Count == 0) return;

        foreach (var entry in _cachedEntries)
            CheckClassReminders(entry, now, _cachedEntries);

        if (App.Settings.EnableExamMode)
            CheckExamReminders(now);
    }

    /// <summary>相邻两节之间的间隔类型（2.8 课间语义）。internal 供自检断言用。</summary>
    internal enum GapKind
    {
        Normal,              // 普通课间（1 ~ 60 分钟）
        Consecutive,         // 连堂：几乎没有课间（≤1 分钟，两节其实是一节大课）
        Dismissal,           // 放学级：长间隔（≥60 分钟，如中午放学 / 傍晚放学）
        SelfStudyBoundary,   // 自习类连堂（2026-09-18 新增，见 ClassifyGap 注释）
    }

    private static readonly TimeSpan ConsecutiveGapMax = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DismissalGapMin = TimeSpan.FromMinutes(60);

    /// <summary>
    /// 是不是"自习类"节次（早自习 / 晚读 / 午休·午自习 / 晚自习）。
    /// 用于判断"自习类连堂"边界 —— 见 <see cref="ClassifyGap"/>。
    /// </summary>
    private static bool IsSelfStudy(PeriodType type)
        => type is PeriodType.Morning or PeriodType.Reading or PeriodType.Noon or PeriodType.Evening;

    /// <summary>「上午放学」的时间上界：结束时间早于此视为中午放学（而不是傍晚的长间隔）</summary>
    private static readonly TimeSpan NoonCutoff = TimeSpan.FromHours(13);

    /// <summary>判定 current → next 的间隔类型；任一为空或时间异常（跨天课）→ Normal（不压制）。
    /// 注：current/next 都可为 null（首节无前、末节无后），调用方无需预先判空。
    /// internal 供自检断言用（课间静音语义容易"改错也不报错"，必须钉住）。</summary>
    internal static GapKind ClassifyGap(ScheduleEntry? current, ScheduleEntry? next, DateTime date)
    {
        // 首节没有上一节、末节没有下一节 —— 都不是"间隔"概念，按普通处理（不压制任何提醒）
        if (current == null || next == null) return GapKind.Normal;
        try
        {
            var gap = next.GetStartDateTime(date) - current.GetEndDateTimeActual(date);
            if (gap < TimeSpan.Zero) return GapKind.Normal;   // 跨天课/时间异常 → 不压制
            if (gap >= DismissalGapMin) return GapKind.Dismissal;

            // 2026-09-18（用户反馈"连堂中间的通知太吵"）：
            // 上一节是**自习类**（早自习 / 晚读 / 午休 / 晚自习）时，它与紧随其后的节次
            // 其实是**一段连着的时间**（晚读→晚自习、中午听力→下午第一节、早自习→第一节），
            // 中间报"下课 / 晚读结束"和"快上课了 / 晚自习开始"都只是噪音。
            // → 归为 SelfStudyBoundary，调用方据此把**这个边界上的提醒全部压掉**。
            //
            // 放在 Dismissal 之后判断：长间隔仍走 Dismissal，保住「上午放学」那条语义
            // （午休结束 → 下午第一节若间隔很长，仍会被认成中午放学）。
            if (IsSelfStudy(current.Type)) return GapKind.SelfStudyBoundary;

            if (gap <= ConsecutiveGapMax) return GapKind.Consecutive;
        }
        catch { /* 时间串非法等异常 → 按普通课间处理 */ }
        return GapKind.Normal;
    }

    /// <summary>
    /// 课节边界上的提醒压制决策（internal 供自检断言）。
    /// 独立成"纯函数"是因为这套语义已经按用户反馈调整过两次
    /// （9-18 加自习类连堂静音、9-20 又要求保留后面那节的「上课了」），
    /// 散在触发代码里改错了不会报错、只会默默变吵/变哑。
    /// </summary>
    internal readonly record struct ReminderGates(
        bool SuppressNextClassSoon,   // 本节的「快上课了」
        bool SuppressStart,           // 本节的「上课了」
        bool SuppressEnd,             // 本节的「下课」/「即将下课」
        bool SuppressSpecialStart,    // 本节的自习类专属「XX开始」
        bool SuppressSpecialEnd)      // 本节的自习类专属「XX结束」
    {
        public static readonly ReminderGates None = new(false, false, false, false, false);
    }

    /// <summary>
    /// 根据"上一节 / 本节 / 下一节"算出本节要压掉哪些提醒。
    /// 规则（按用户两次反馈定稿）：
    ///   · 连堂（≤1 分钟）或放学级长间隔（≥60 分钟）→ 压掉本节的「快上课了」（2.8 原有）
    ///   · **自习类连堂**（上一节是自习类且间隔 &lt;60 分钟）→ 边界整段静音：
    ///     上一节的下课类 + 本节的自习专属/快上课了；
    ///     **但本节的「上课了」要保留 —— 仅当本节是普通课**（2026-09-20 用户要求
    ///     "下午第一节课还是该提醒一声"）。晚读→晚自习 这种两头都是自习的边界仍然全静音。
    /// </summary>
    internal static ReminderGates DecideGates(ScheduleEntry? prev, ScheduleEntry entry,
        ScheduleEntry? next, DateTime date)
    {
        var gapFromPrev = ClassifyGap(prev, entry, date);
        var gapKindToNext = ClassifyGap(entry, next, date);

        bool quietStart = gapFromPrev == GapKind.SelfStudyBoundary;   // 本节紧跟在自习类之后
        bool quietEnd = gapKindToNext == GapKind.SelfStudyBoundary;   // 本节是自习类且后面还接一节

        return new ReminderGates(
            // 「快上课了」：连堂 / 放学级 / 自习类连堂 都压
            SuppressNextClassSoon: gapFromPrev is GapKind.Consecutive or GapKind.Dismissal
                                                  or GapKind.SelfStudyBoundary,
            // 「上课了」：只在"本节也是自习类"时跟着静音
            SuppressStart: quietStart && IsSelfStudy(entry.Type),
            SuppressEnd: quietEnd,
            SuppressSpecialStart: quietStart,
            SuppressSpecialEnd: quietEnd);
    }

    private void CheckClassReminders(ScheduleEntry entry, DateTime now, List<ScheduleEntry> allEntries)
    {
        var startDt = entry.GetStartDateTime(now.Date);
        // 跨天课（EndTime < StartTime）的真实结束时刻在次日，否则下课/放学提醒永不触发
        var endDt = entry.GetEndDateTimeActual(now.Date);
        string prefix = $"{now:yyyyMMdd}_{entry.DayOfWeek}_{entry.Period}";

        // 边界提醒的压制决策统一由 DecideGates 给出（见其注释：规则经用户两次反馈定稿）
        int idx = allEntries.IndexOf(entry);
        var prev = idx > 0 ? allEntries[idx - 1] : null;
        var next = idx >= 0 && idx + 1 < allEntries.Count ? allEntries[idx + 1] : null;
        var gates = DecideGates(prev, entry, next, now.Date);

        if (App.Settings.RemindClassStart && !gates.SuppressStart)
            TryFire($"{prefix}_start", now, startDt, TimeSpan.Zero,
                ReminderType.ClassStart, "上课了", $"{entry.Subject} 开始上课");

        if (App.Settings.RemindClassMid)
            TryFire($"{prefix}_mid", now, startDt, TimeSpan.FromMinutes(20),
                ReminderType.ClassMid, "上课提醒", $"{entry.Subject} 已上课 20 分钟");

        if (App.Settings.RemindClassEndSoon10 && !gates.SuppressEnd)
        {
            TryFire($"{prefix}_endsoon10", now, endDt, TimeSpan.FromMinutes(-10),
                ReminderType.ClassEndSoon, "即将下课", $"{entry.Subject} 还有 10 分钟下课");
        }

        if (App.Settings.RemindClassEndSoon && !gates.SuppressEnd)
        {
            TryFire($"{prefix}_endsoon", now, endDt, TimeSpan.FromMinutes(-1),
                ReminderType.ClassEndSoon, "即将下课", $"{entry.Subject} 还有 1 分钟下课");
        }

        if (App.Settings.RemindClassEnd && !gates.SuppressEnd)
            TryFire($"{prefix}_end", now, endDt, TimeSpan.Zero,
                ReminderType.ClassEnd, "下课", $"{entry.Subject} 下课了");

        if (App.Settings.RemindNextClassSoon && !gates.SuppressNextClassSoon)
            TryFire($"{prefix}_nextclass", now, startDt, TimeSpan.FromMinutes(-5),
                ReminderType.NextClassSoon, "快上课了", $"5 分钟后 {entry.Subject} 开始");

        if (App.Settings.RemindDayEnd)
        {
            var lastEntry = allEntries[allEntries.Count - 1];
            if (entry == lastEntry)
            {
                TryFire($"{prefix}_dayend", now, endDt, TimeSpan.Zero,
                    ReminderType.DayEnd, "放学", "今天的课程全部结束");
            }
            else if (ClassifyGap(entry, next, now.Date) == GapKind.Dismissal && endDt.TimeOfDay < NoonCutoff)
            {
                // 2.8：上午最后一节下课也是一次放学（中午回家）。只认"结束时间在 13:00 之前"的长间隔，
                // 避免傍晚的长间隔（如 17:05 下课 → 19:00 晚自习）被误报成"放学" ——
                // 傍晚那一段只压掉「快上课了」（见 DecideGates），不额外插放学提醒。
                TryFire($"{prefix}_noonend", now, endDt, TimeSpan.Zero,
                    ReminderType.MorningDayEnd, "上午放学", "上午课程结束，午间休息");
            }
        }

        // 自习类专属提醒也要跟着"静音边界"走（2026-09-18）：
        // 原来晚读→晚自习会连出「晚读结束」+「晚自习开始」，正是用户嫌吵的那种。
        if (entry.Type == PeriodType.Morning && App.Settings.RemindSpecialPeriod)
        {
            if (!gates.SuppressSpecialStart) TryFire($"{prefix}_mstart", now, startDt, TimeSpan.Zero, ReminderType.MorningStart, "早自习", "早自习开始");
            if (!gates.SuppressSpecialEnd) TryFire($"{prefix}_mend", now, endDt, TimeSpan.Zero, ReminderType.MorningEnd, "早自习", "早自习结束");
        }

        if (entry.Type == PeriodType.Evening && App.Settings.RemindSpecialPeriod)
        {
            if (!gates.SuppressSpecialStart) TryFire($"{prefix}_estart", now, startDt, TimeSpan.Zero, ReminderType.EveningStart, "晚自习", "晚自习开始");
            if (!gates.SuppressSpecialEnd) TryFire($"{prefix}_eend", now, endDt, TimeSpan.Zero, ReminderType.EveningEnd, "晚自习", "晚自习结束");
        }

        if (entry.Type == PeriodType.Reading && App.Settings.RemindSpecialPeriod)
        {
            if (!gates.SuppressSpecialStart) TryFire($"{prefix}_rstart", now, startDt, TimeSpan.Zero, ReminderType.ReadingStart, "晚读", "晚读开始");
            if (!gates.SuppressSpecialEnd) TryFire($"{prefix}_rend", now, endDt, TimeSpan.Zero, ReminderType.ReadingEnd, "晚读", "晚读结束");
        }
    }

    private void CheckExamReminders(DateTime now)
    {
        var cur = _manager.GetCurrentExamSubject(now);
        if (cur == null) return;
        var (exam, subject) = cur.Value;
        var endDt = now.Date + subject.EndTime;
        string key = $"exam_{now:yyyyMMdd}_{subject.Name}_endsoon";
        TryFire(key, now, endDt, TimeSpan.FromMinutes(-15),
            ReminderType.ExamEndSoon, "考试提醒", $"{subject.Name} 还有 15 分钟结束，注意检查");
    }

    /// <summary>
    /// 触发窗口判断（#11 修复，剥离为可复用静态方法 —— SchedulerService/自动化任务引擎将来直接复用）。
    /// 原 ±1.5s 窗口在 UI 卡顿/系统休眠唤醒时会错过触发点且永不补发（key 未入 _firedKeys 但时间已滑过）。
    /// 现改为：触发前 0.5s ~ 后 90s 内且当天未触发 → 补发。软件启动/当天早已滑过的时间窗（diff 远超 90s）不会误补。
    /// </summary>
    internal const double TriggerCatchUpSeconds = 90;

    internal static bool IsInTriggerWindow(DateTime now, DateTime trigger,
        double catchUpSeconds = TriggerCatchUpSeconds)
    {
        var diff = (now - trigger).TotalSeconds;
        return diff >= -0.5 && diff < catchUpSeconds;
    }

    private bool TryFire(string key, DateTime now, DateTime baseDt, TimeSpan offset,
                          ReminderType type, string title, string message)
    {
        if (_firedKeys.Contains(key)) return false;
        var trigger = baseDt + offset;
        if (!IsInTriggerWindow(now, trigger)) return false;
        _firedKeys.Add(key);
        FireReminder(type, title, message);
        return true;
    }

    private void FireReminder(ReminderType type, string title, string message)
    {
        // 下课那一刻 / 考试结束 提醒不播放声音（拖堂时到点不准 / 考试窗口自蜂鸣），其余播放提示音
        if (type != ReminderType.ClassEnd && type != ReminderType.ExamEndSoon)
            PlaySound();

        var handler = Reminder;
        if (handler != null)
        {
            var args = new ReminderEventArgs(type, title, message);
            foreach (EventHandler<ReminderEventArgs> d in handler.GetInvocationList())
            {
                try { d(this, args); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ReminderService] 事件订阅者异常: {ex.Message}"); }
            }
        }
    }

    private void PlaySound()
    {
        if (!App.Settings.EnableReminderSound) return;
        try
        {
            var path = App.Settings.ReminderSoundPath;
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                // 自定义 wav（对齐 WPF SoundPlayer 行为）
                PlaySoundW(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
            }
            else
            {
                // 降级到系统提示音
                MessageBeep(MB_ICONASTERISK);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Stop();
        _manager.DataChanged -= _onDataChanged;
    }
}
