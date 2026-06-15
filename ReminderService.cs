using System;
using System.Collections.Generic;
using System.Media;
using System.Windows.Threading;

namespace GaokaoCountdown
{
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
    /// 提醒服务：每秒轮询，在关键时刻触发事件并弹出 Windows 通知（可选声音）。
    /// 通过事件解耦，主窗口/课表栏订阅后自行处理 UI。
    /// </summary>
    public class ReminderService : IDisposable
    {
        private readonly ScheduleManager _manager;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;

        // ── 已触发集合（防止同一秒多次触发）─────────────────
        private readonly HashSet<string> _firedKeys = new();
        private DateTime _lastClearDay = DateTime.Today;

        // ── 60 秒倒计时状态 ────────────────────────────────
        private DispatcherTimer? _countdown60Timer;
        private int _countdown60Remaining;

        // ── 事件 ──────────────────────────────────────────
        public event EventHandler<ReminderEventArgs>? Reminder;
        /// <summary>60 秒倒计时每秒更新（参数=剩余秒数），倒计时结束时参数=0</summary>
        public event EventHandler<int>? Countdown60Tick;

        public ReminderService(ScheduleManager manager, AppSettings settings)
        {
            _manager = manager;
            _settings = settings;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop()  => _timer.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;

            // 每天零点清空已触发集合
            if (now.Date != _lastClearDay)
            {
                _firedKeys.Clear();
                _lastClearDay = now.Date;
            }

            var entries = _manager.GetTodayEntries();
            if (entries.Count == 0) return;

            foreach (var entry in entries)
            {
                CheckClassReminders(entry, now, entries);
            }

            // 检查考试提醒
            if (_settings.EnableExamMode)
                CheckExamReminders(now);
        }

        private void CheckClassReminders(ScheduleEntry entry, DateTime now, List<ScheduleEntry> allEntries)
        {
            var startDt = entry.GetStartDateTime(now.Date);
            var endDt   = entry.GetEndDateTime(now.Date);
            string prefix = $"{now:yyyyMMdd}_{entry.DayOfWeek}_{entry.Period}";

            // 上课时间到
            if (_settings.RemindClassStart)
                TryFire($"{prefix}_start", now, startDt, TimeSpan.Zero,
                    ReminderType.ClassStart, "上课了", $"{entry.Subject} 开始上课");

            // 上课后 20 分钟
            if (_settings.RemindClassMid)
                TryFire($"{prefix}_mid", now, startDt, TimeSpan.FromMinutes(20),
                    ReminderType.ClassMid, "上课提醒", $"{entry.Subject} 已上课 20 分钟");

            // 距下课还有 1 分钟
            if (_settings.RemindClassEndSoon)
            {
                if (TryFire($"{prefix}_endsoon", now, endDt, TimeSpan.FromMinutes(-1),
                    ReminderType.ClassEndSoon, "即将下课", $"{entry.Subject} 还有 1 分钟下课"))
                {
                    StartCountdown60();
                }
            }

            // 下课时间到
            if (_settings.RemindClassEnd)
                TryFire($"{prefix}_end", now, endDt, TimeSpan.Zero,
                    ReminderType.ClassEnd, "下课", $"{entry.Subject} 下课了");

            // 距下节课还有 5 分钟
            if (_settings.RemindNextClassSoon)
                TryFire($"{prefix}_nextclass", now, startDt, TimeSpan.FromMinutes(-5),
                    ReminderType.NextClassSoon, "快上课了", $"5 分钟后 {entry.Subject} 开始");

            // 放学（最后一节下课）
            if (_settings.RemindDayEnd)
            {
                var lastEntry = allEntries[allEntries.Count - 1];
                if (entry == lastEntry)
                    TryFire($"{prefix}_dayend", now, endDt, TimeSpan.Zero,
                        ReminderType.DayEnd, "放学", "今天的课程全部结束");
            }

            // 特殊节次
            if (entry.Type == PeriodType.Morning && _settings.RemindSpecialPeriod)
            {
                TryFire($"{prefix}_mstart", now, startDt, TimeSpan.Zero,
                    ReminderType.MorningStart, "早自习", "早自习开始");
                TryFire($"{prefix}_mend", now, endDt, TimeSpan.Zero,
                    ReminderType.MorningEnd, "早自习", "早自习结束");
            }

            if (entry.Type == PeriodType.Evening && _settings.RemindSpecialPeriod)
            {
                TryFire($"{prefix}_estart", now, startDt, TimeSpan.Zero,
                    ReminderType.EveningStart, "晚自习", "晚自习开始");
                TryFire($"{prefix}_eend", now, endDt, TimeSpan.Zero,
                    ReminderType.EveningEnd, "晚自习", "晚自习结束");
            }

            if (entry.Type == PeriodType.Reading && _settings.RemindSpecialPeriod)
            {
                TryFire($"{prefix}_rstart", now, startDt, TimeSpan.Zero,
                    ReminderType.ReadingStart, "晚读", "晚读开始");
                TryFire($"{prefix}_rend", now, endDt, TimeSpan.Zero,
                    ReminderType.ReadingEnd, "晚读", "晚读结束");
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
        /// 检查 now 是否落在 [triggerTime - 0.5s, triggerTime + 0.5s) 区间内，
        /// 且该 key 尚未触发过；满足则触发提醒并返回 true。
        /// </summary>
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
            // 播放声音
            PlaySound();

            // 发出事件（UI 订阅者通过 ReminderWindow 显示自定义通知）
            Reminder?.Invoke(this, new ReminderEventArgs(type, title, message));
        }

        private void PlaySound()
        {
            if (!_settings.EnableReminderSound) return;
            try
            {
                var path = _settings.ReminderSoundPath;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var player = new SoundPlayer(path);
                    player.Play();
                }
                else
                {
                    // 降级到系统提示音
                    SystemSounds.Asterisk.Play();
                }
            }
            catch { }
        }


        // ── 60 秒倒计时 ────────────────────────────────────
        private void StartCountdown60()
        {
            _countdown60Timer?.Stop();
            _countdown60Remaining = 60;
            Countdown60Tick?.Invoke(this, _countdown60Remaining);

            _countdown60Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdown60Timer.Tick += (s, e) =>
            {
                _countdown60Remaining--;
                Countdown60Tick?.Invoke(this, _countdown60Remaining);
                if (_countdown60Remaining <= 0)
                    _countdown60Timer?.Stop();
            };
            _countdown60Timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            _countdown60Timer?.Stop();
        }
    }
}
