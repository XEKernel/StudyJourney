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

        public ScheduleManager()
        {
            _data = ScheduleData.Load();
        }

        public void Reload()
        {
            _data = ScheduleData.Load();
            DataChanged?.Invoke();
        }

        public void Save()
        {
            _data.Save();
            DataChanged?.Invoke();
        }

        // ── 课表查询 ──────────────────────────────────────────

        /// <summary>获取今天的课程列表（按上课时间排序）</summary>
        public List<ScheduleEntry> GetTodayEntries(DateTime? date = null)
        {
            var d = date ?? DateTime.Today;
            int dow = (int)d.DayOfWeek;
            if (dow == 0) dow = 7;  // 周日转为 7

            return _data.Entries
                .Where(e => e.DayOfWeek == dow)
                .OrderBy(e => e.StartTime)
                .ToList();
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

        /// <summary>获取今天的考试（可能有多场）</summary>
        public List<ExamEntry> GetTodayExams(DateTime? date = null)
        {
            var d = (date ?? DateTime.Today).Date;
            return _data.Exams
                .Where(e => e.Date.Date == d)
                .OrderBy(e => e.Date)
                .ToList();
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

        // ── Excel 导入接口（占位，后续扩展）─────────────────────
        /// <summary>
        /// 从 xlsx 导入课表（需安装 EPPlus 等库）。
        /// 当前为占位接口，返回 false 并附带提示。
        /// </summary>
        public (bool success, string message) ImportFromExcel(string filePath)
        {
            // TODO: 安装 EPPlus（OfficeOpenXml）后实现 xlsx 解析
            // var package = new ExcelPackage(new FileInfo(filePath));
            // var ws = package.Workbook.Worksheets[0];
            // ...
            return (false, "Excel 导入功能需安装 EPPlus NuGet 包后实现。当前支持直接编辑 schedule.json。");
        }

        /// <summary>从 JSON 字符串导入课表，返回是否成功</summary>
        public (bool success, string message) ImportFromJson(string json)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<ScheduleData>(json, opts);
                if (data == null) return (false, "JSON 格式无效");
                // 防止 JSON 中 Entries/Exams 显式设为 null 导致后续崩溃
                data.Entries ??= new List<ScheduleEntry>();
                data.Exams  ??= new System.Collections.ObjectModel.ObservableCollection<ExamEntry>();
                _data = data;
                _data.Save();
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
