using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace GaokaoCountdown
{
    // ── 节次类型 ───────────────────────────────────────────
    public enum PeriodType
    {
        Normal,     // 普通课
        Morning,    // 早自习
        Evening,    // 晚自习
        Reading,    // 晚读
        Noon,       // 午休/午自习
    }

    // ── 单条课节 ───────────────────────────────────────────
    public class ScheduleEntry
    {
        /// <summary>星期几：1=周一 … 7=周日</summary>
        public int DayOfWeek { get; set; }

        /// <summary>第几节课（从 1 开始）</summary>
        public int Period { get; set; }

        /// <summary>课程名称</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>上课时间，格式 "HH:mm"</summary>
        public string StartTimeStr { get; set; } = "08:00";

        /// <summary>下课时间，格式 "HH:mm"</summary>
        public string EndTimeStr { get; set; } = "08:45";

        /// <summary>节次类型</summary>
        public PeriodType Type { get; set; } = PeriodType.Normal;

        /// <summary>备注（可选）</summary>
        public string Remark { get; set; } = string.Empty;

        // ── 运行时计算属性（不序列化）─────────────────────
        [JsonIgnore]
        public TimeSpan StartTime
        {
            get
            {
                if (TimeSpan.TryParse(StartTimeStr, out var t)) return t;
                return TimeSpan.Zero;
            }
        }

        [JsonIgnore]
        public TimeSpan EndTime
        {
            get
            {
                if (TimeSpan.TryParse(EndTimeStr, out var t)) return t;
                return TimeSpan.FromMinutes(45);
            }
        }

        [JsonIgnore]
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>返回今天这节课的实际 DateTime</summary>
        public DateTime GetStartDateTime(DateTime? date = null)
        {
            var d = (date ?? DateTime.Today).Date;
            return d + StartTime;
        }

        public DateTime GetEndDateTime(DateTime? date = null)
        {
            var d = (date ?? DateTime.Today).Date;
            return d + EndTime;
        }
    }

    // ── 考试科目 ───────────────────────────────────────────
    public class ExamSubject
    {
        public string Name { get; set; } = string.Empty;
        public string StartTimeStr { get; set; } = "09:00";
        public string EndTimeStr { get; set; } = "11:30";

        [JsonIgnore]
        public TimeSpan StartTime
        {
            get { if (TimeSpan.TryParse(StartTimeStr, out var t)) return t; return TimeSpan.Zero; }
        }

        [JsonIgnore]
        public TimeSpan EndTime
        {
            get { if (TimeSpan.TryParse(EndTimeStr, out var t)) return t; return TimeSpan.FromHours(2.5); }
        }

        [JsonIgnore]
        public TimeSpan Duration => EndTime - StartTime;
    }

    // ── 考试条目 ───────────────────────────────────────────
    public class ExamEntry
    {
        public string Name { get; set; } = string.Empty;
        public string DateStr { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public List<ExamSubject> Subjects { get; set; } = new();

        [JsonIgnore]
        public DateTime Date
        {
            get { if (DateTime.TryParse(DateStr, out var d)) return d; return DateTime.Today; }
        }
    }

    // ── 课表根容器 ─────────────────────────────────────────
    public class ScheduleData
    {
        public List<ScheduleEntry> Entries { get; set; } = new();
        public List<ExamEntry> Exams { get; set; } = new();

        /// <summary>按 星期→节次 排序（DataGrid 展示用）</summary>
        public void SortEntries()
        {
            Entries = Entries
                .OrderBy(e => e.DayOfWeek)
                .ThenBy(e => e.Period)
                .ToList();
        }

        private static readonly string _schedulePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schedule.json");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static ScheduleData Load()
        {
            try
            {
                if (File.Exists(_schedulePath))
                {
                    var json = File.ReadAllText(_schedulePath);
                    return JsonSerializer.Deserialize<ScheduleData>(json, _jsonOpts)
                           ?? new ScheduleData();
                }
            }
            catch { /* 文件损坏时静默回退到空课表 */ }
            return new ScheduleData();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, _jsonOpts);
                File.WriteAllText(_schedulePath, json);
            }
            catch { }
        }
    }
}
