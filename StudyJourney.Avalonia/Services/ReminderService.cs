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
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;

    private readonly HashSet<string> _firedKeys = new();
    private DateTime _lastClearDay = DateTime.Today;

    private DateTime _cachedDay = DateTime.MinValue;
    private List<ScheduleEntry> _cachedEntries = new();
    private readonly Action _onDataChanged;

    public event EventHandler<ReminderEventArgs>? Reminder;

    public ReminderService(ScheduleManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _onDataChanged = () => _cachedDay = DateTime.MinValue;
        _manager.DataChanged += _onDataChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Helpers.TimeSimulator.Now;

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

        if (_settings.EnableExamMode)
            CheckExamReminders(now);
    }

    private void CheckClassReminders(ScheduleEntry entry, DateTime now, List<ScheduleEntry> allEntries)
    {
        var startDt = entry.GetStartDateTime(now.Date);
        var endDt = entry.GetEndDateTime(now.Date);
        string prefix = $"{now:yyyyMMdd}_{entry.DayOfWeek}_{entry.Period}";

        if (_settings.RemindClassStart)
            TryFire($"{prefix}_start", now, startDt, TimeSpan.Zero,
                ReminderType.ClassStart, "上课了", $"{entry.Subject} 开始上课");

        if (_settings.RemindClassMid)
            TryFire($"{prefix}_mid", now, startDt, TimeSpan.FromMinutes(20),
                ReminderType.ClassMid, "上课提醒", $"{entry.Subject} 已上课 20 分钟");

        if (_settings.RemindClassEndSoon)
        {
            TryFire($"{prefix}_endsoon", now, endDt, TimeSpan.FromMinutes(-1),
                ReminderType.ClassEndSoon, "即将下课", $"{entry.Subject} 还有 1 分钟下课");
        }

        if (_settings.RemindClassEnd)
            TryFire($"{prefix}_end", now, endDt, TimeSpan.Zero,
                ReminderType.ClassEnd, "下课", $"{entry.Subject} 下课了");

        if (_settings.RemindNextClassSoon)
            TryFire($"{prefix}_nextclass", now, startDt, TimeSpan.FromMinutes(-5),
                ReminderType.NextClassSoon, "快上课了", $"5 分钟后 {entry.Subject} 开始");

        if (_settings.RemindDayEnd)
        {
            var lastEntry = allEntries[allEntries.Count - 1];
            if (entry == lastEntry)
                TryFire($"{prefix}_dayend", now, endDt, TimeSpan.Zero,
                    ReminderType.DayEnd, "放学", "今天的课程全部结束");
        }

        if (entry.Type == PeriodType.Morning && _settings.RemindSpecialPeriod)
        {
            TryFire($"{prefix}_mstart", now, startDt, TimeSpan.Zero, ReminderType.MorningStart, "早自习", "早自习开始");
            TryFire($"{prefix}_mend", now, endDt, TimeSpan.Zero, ReminderType.MorningEnd, "早自习", "早自习结束");
        }

        if (entry.Type == PeriodType.Evening && _settings.RemindSpecialPeriod)
        {
            TryFire($"{prefix}_estart", now, startDt, TimeSpan.Zero, ReminderType.EveningStart, "晚自习", "晚自习开始");
            TryFire($"{prefix}_eend", now, endDt, TimeSpan.Zero, ReminderType.EveningEnd, "晚自习", "晚自习结束");
        }

        if (entry.Type == PeriodType.Reading && _settings.RemindSpecialPeriod)
        {
            TryFire($"{prefix}_rstart", now, startDt, TimeSpan.Zero, ReminderType.ReadingStart, "晚读", "晚读开始");
            TryFire($"{prefix}_rend", now, endDt, TimeSpan.Zero, ReminderType.ReadingEnd, "晚读", "晚读结束");
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

    private bool TryFire(string key, DateTime now, DateTime baseDt, TimeSpan offset,
                          ReminderType type, string title, string message)
    {
        if (_firedKeys.Contains(key)) return false;
        var trigger = baseDt + offset;
        var diff = (now - trigger).TotalSeconds;
        if (diff >= -0.5 && diff < 1.0)
        {
            _firedKeys.Add(key);
            FireReminder(type, title, message);
            return true;
        }
        return false;
    }

    private void FireReminder(ReminderType type, string title, string message)
    {
        // ClassEndSoon / ClassEnd / ExamEndSoon 使用大字覆盖层，不播放声音
        if (type != ReminderType.ClassEndSoon && type != ReminderType.ClassEnd && type != ReminderType.ExamEndSoon)
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
        if (!_settings.EnableReminderSound) return;
        try
        {
            var path = _settings.ReminderSoundPath;
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
