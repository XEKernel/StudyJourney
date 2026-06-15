using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GaokaoCountdown
{
    /// <summary>课表管理器：加载/保存课表，查询当前/下一节课</summary>
    public class ScheduleManager
    {
        private ScheduleData _data;

        public ScheduleData Data => _data;

        public ScheduleManager()
        {
            _data = ScheduleData.Load();
        }

        public void Reload() => _data = ScheduleData.Load();

        public void Save() => _data.Save();

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

        /// <summary>获取当前正在上的课（当前时间在 [start, end] 之间），无则返回 null</summary>
        public ScheduleEntry? GetCurrentEntry(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            return GetTodayEntries(dt.Date)
                .FirstOrDefault(e => tod >= e.StartTime && tod <= e.EndTime);
        }

        /// <summary>获取下一节课（当前时间之后，今天还没开始的最近一节），无则返回 null</summary>
        public ScheduleEntry? GetNextEntry(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var tod = dt.TimeOfDay;
            return GetTodayEntries(dt.Date)
                .FirstOrDefault(e => e.StartTime > tod);
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
            var endDt = cur.GetEndDateTime(dt.Date);
            var remaining = endDt - dt;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>当前课的上课进度 0.0~1.0，当前不在上课时间返回 null</summary>
        public double? GetCurrentProgress(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var cur = GetCurrentEntry(dt);
            if (cur == null) return null;
            var elapsed = dt.TimeOfDay - cur.StartTime;
            if (cur.Duration.TotalSeconds <= 0) return null;
            return Math.Clamp(elapsed.TotalSeconds / cur.Duration.TotalSeconds, 0, 1);
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
                    .FirstOrDefault(s => tod >= s.StartTime && tod <= s.EndTime);
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
                _data = data;
                _data.Save();
                return (true, $"导入成功：{data.Entries.Count} 节课，{data.Exams.Count} 场考试");
            }
            catch (Exception ex)
            {
                return (false, $"JSON 解析错误：{ex.Message}");
            }
        }
    }
}
