using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StudyJourney.Avalonia.Models
{
    /// <summary>课表管理器：加载/保存课表，查询当前/下一节课</summary>
    public class ScheduleManager
    {
        private ScheduleData _data;

        /// <summary>数据变更事件（导入/保存后触发，供提醒服务等刷新缓存）</summary>
        public event Action? DataChanged;

        public ScheduleData Data => _data;

        // ── 查询结果缓存（2026-10-01 性能优化）──────────────────────────
        // 背景：主窗口每秒的刷新链会**反复**查询"今天的课"——
        //   GetTodayEntries 被 GetCurrentEntry / GetNextEntry / GetTimeToEndOfCurrent /
        //   GetCurrentProgress / UpdateClassProgress 各自再调一遍，一秒内约 10 次；
        //   自动化引擎每条规则每 tick 也要查一次。
        // 而每次查询都是 Entries.Where(按星期几) + OrderBy(开始时间) + ToList() 的**全表扫描 + 排序 + 分配**，
        //   一天累计近百万次。实测这是空载 CPU 占用的主要来源之一。
        //
        // 做法：按"日期"缓存查询结果，配 200ms 短 TTL。
        //   · 为什么不只靠 DataChanged 失效：课表编辑器会**直接改** Data.Entries（增删条目）
        //     而不一定立刻发事件 → 只靠事件会读到过期数据。短 TTL 兜住这种情况（最多延迟 0.2 秒，
        //     对"老师改课表"这种分钟级操作完全无感），事件触发时则立即清空（正常路径零延迟）。
        //   · 锁保护：远程改课表（HTTP 后台线程 → Reload）与 UI 线程读缓存可能并发。
        private readonly Dictionary<DateTime, (List<ScheduleEntry> Items, DateTime At)> _todayCache = new();
        private readonly Dictionary<DateTime, (List<ExamEntry> Items, DateTime At)> _examCache = new();
        private const int CacheTtlMs = 200;

        /// <summary>清空查询缓存（数据变更时调用）</summary>
        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _todayCache.Clear();
                _examCache.Clear();
            }
        }

        private readonly object _cacheLock = new();

        public ScheduleManager()
        {
            _data = ScheduleData.Load();
        }

        public void Reload()
        {
            _data = ScheduleData.Load();
            InvalidateCache();
            DataChanged?.Invoke();
        }

        public void Save()
        {
            _data.Save();
            InvalidateCache();
            DataChanged?.Invoke();
        }

        // ── 课表查询 ──────────────────────────────────────────

        /// <summary>
        /// 把日期映射为"那天实际上哪一天的课 / 哪一天的自动化规则"（1=周一 … 7=周日）。
        ///
        /// 2026-09-21 新增（用户反馈）：调休补课时，周日补周五的课 ——
        /// 自动化规则是按星期几配的（周五中午的听力写在 TriggerDays=[5]），
        /// 不映射的话调休日就"什么都没发生"。
        /// 有这个映射后，所有下游（取课表 / 提醒 / 自动化）自动跟着走。
        /// </summary>
        public int GetEffectiveDayOfWeek(DateTime date)
            => ScheduleData.ResolveEffectiveDayOfWeek(date, _data.MakeupDays);

        /// <summary>今天是调休日吗（供 UI 提示用）</summary>
        public MakeupDay? GetMakeupDay(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            return _data.MakeupDays?.FirstOrDefault(x => x.DateStr == key);
        }

        /// <summary>
        /// 获取今天的课程列表（按上课时间排序）。调休日取"被补的那一天"的课表。
        /// ⚠ 返回的是**内部缓存列表**：只读使用，调用方**不得修改**（2026-10-01 性能优化）。
        /// </summary>
        public List<ScheduleEntry> GetTodayEntries(DateTime? date = null)
        {
            var d = (date ?? DateTime.Today).Date;

            lock (_cacheLock)
            {
                if (_todayCache.TryGetValue(d, out var hit) &&
                    (DateTime.UtcNow - hit.At).TotalMilliseconds < CacheTtlMs)
                    return hit.Items;

                int dow = GetEffectiveDayOfWeek(d);
                var list = _data.Entries
                    .Where(e => e.DayOfWeek == dow)
                    .OrderBy(e => e.StartTime)
                    .ToList();

                // 跨天课凌晨会查"昨天"，正常只有 1~2 个键；留点余量防无界增长
                if (_todayCache.Count > 4) _todayCache.Clear();
                _todayCache[d] = (list, DateTime.UtcNow);
                return list;
            }
        }

        /// <summary>获取当前正在上的课（含提前2分钟预备铃），无则返回 null</summary>
        public ScheduleEntry? GetCurrentEntry(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            var prep = TimeSpan.FromMinutes(2);

            // 预备铃提前2分钟，老师即到，进入上课模式
            // 今天的课：普通课 [start-prep, end)；跨天课从 start-prep 起延续到次日凌晨
            var today = GetTodayEntries(dt.Date).Where(e =>
                e.EndTime < e.StartTime
                    ? tod >= e.StartTime - prep
                    : tod >= e.StartTime - prep && tod < e.EndTime);

            // 昨天跨天课的凌晨延续（如昨晚 22:00-00:30 → 今天 00:00-00:30 仍在上）
            var yesterday = Enumerable.Empty<ScheduleEntry>();
            if (tod < TimeSpan.FromHours(6))
            {
                yesterday = GetTodayEntries(dt.Date.AddDays(-1))
                    .Where(e => e.EndTime < e.StartTime && tod < e.EndTime);
            }

            // 重叠时优先高节次（下一节的预备铃覆盖上一节的末尾）
            return today.Concat(yesterday)
                .OrderByDescending(e => e.Period)
                .FirstOrDefault();
        }

        /// <summary>获取下一节课（当前时间之后，今天还没开始的最近一节），无则返回 null</summary>
        public ScheduleEntry? GetNextEntry(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            return GetTodayEntries(dt.Date)
                .FirstOrDefault(e =>
                {
                    // 跨天课（22:00-00:30）：若当前在跨天课的结束时段内，视为"今天最后一节已结束"
                    if (e.EndTime < e.StartTime)
                        return tod < e.StartTime - TimeSpan.FromMinutes(2); // 跨天课开始前才视为下一节
                    return e.StartTime > tod;
                });
        }

        /// <summary>距离下节课开始的剩余时间，无下节课返回 null</summary>
        public TimeSpan? GetTimeToNextEntry(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var next = GetNextEntry(dt);
            if (next == null) return null;
            var startDt = next.GetStartDateTime(dt.Date);
            return startDt - dt;
        }

        /// <summary>距离当前课结束的剩余时间，不在上课返回 null</summary>
        public TimeSpan? GetTimeToEndOfCurrent(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var cur = GetCurrentEntry(dt);
            if (cur == null) return null;
            var endDt = cur.GetEndDateTimeActual(dt.Date);
            var remaining = endDt - dt;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>当前课的上课进度 0.0~1.0，当前不在上课时间返回 null</summary>
        public double? GetCurrentProgress(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var cur = GetCurrentEntry(dt);
            if (cur == null) return null;
            // 跨天课进度
            TimeSpan start = cur.StartTime, end = cur.EndTime;
            if (end < start) end += TimeSpan.FromHours(24);   // 跨天：结束时间视为次日
            var elapsed = dt.TimeOfDay - start;
            if (elapsed < TimeSpan.Zero) elapsed += TimeSpan.FromHours(24);
            var duration = end - start;
            if (duration.TotalSeconds <= 0) return null;
            return Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
        }

        // ── 考试查询 ──────────────────────────────────────────

        /// <summary>
        /// 获取今天的考试（可能有多场）。
        /// ⚠ 返回的是**内部缓存列表**：只读使用，调用方**不得修改**。
        /// </summary>
        public List<ExamEntry> GetTodayExams(DateTime? date = null)
        {
            var d = (date ?? DateTime.Today).Date;

            lock (_cacheLock)
            {
                if (_examCache.TryGetValue(d, out var hit) &&
                    (DateTime.UtcNow - hit.At).TotalMilliseconds < CacheTtlMs)
                    return hit.Items;

                var list = _data.Exams
                    .Where(e => e.Date.Date == d)
                    .OrderBy(e => e.Date)
                    .ToList();

                if (_examCache.Count > 4) _examCache.Clear();
                _examCache[d] = (list, DateTime.UtcNow);
                return list;
            }
        }

        /// <summary>获取当前正在考试的科目，无则 null</summary>
        public (ExamEntry exam, ExamSubject subject)? GetCurrentExamSubject(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            foreach (var exam in GetTodayExams(dt.Date))
            {
                var sub = exam.Subjects
                    .FirstOrDefault(s => tod >= s.StartTime && tod < s.EndTime);
                if (sub != null) return (exam, sub);
            }
            return null;
        }

        /// <summary>获取下一个考试科目</summary>
        public (ExamEntry exam, ExamSubject subject)? GetNextExamSubject(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            foreach (var exam in GetTodayExams(dt.Date))
            {
                var sub = exam.Subjects
                    .OrderBy(s => s.StartTime)
                    .FirstOrDefault(s => s.StartTime > tod);
                if (sub != null) return (exam, sub);
            }
            return null;
        }

        // ── JSON 导入 ──────────────────────────────────────────
        /// <summary>从 JSON 字符串导入课表，返回是否成功</summary>
        public (bool success, string message) ImportFromJson(string json)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize(json, AppJsonContext.Default.ScheduleData);
                if (data == null) return (false, "JSON 格式无效");
                // 防止 JSON 中 Entries/Exams 显式设为 null 导致后续崩溃
                data.Entries ??= new List<ScheduleEntry>();
                data.Exams  ??= new System.Collections.ObjectModel.ObservableCollection<ExamEntry>();
                _data = data;
                _data.Save();
                InvalidateCache();
                DataChanged?.Invoke();
                return (true, $"导入成功：{data.Entries.Count} 节课，{data.Exams.Count} 场考试");
            }
            catch (Exception ex)
            {
                return (false, $"JSON 解析错误：{ex.Message}");
            }
        }
    }
}
