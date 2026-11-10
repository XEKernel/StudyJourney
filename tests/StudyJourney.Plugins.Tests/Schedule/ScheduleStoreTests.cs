using System.IO;
using System.Text;
using StudyJourney.Plugin.Schedule;
using StudyJourney.Plugin.Schedule.Models;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// <see cref="ScheduleStore"/> 的读写断言。**全部基于真实样本**（v3.3.0 老师机器上的
/// <c>schedule.json</c> 副本）：格式猜对了但读不懂真实文件等于没做。重点钉住两条纪律——
/// 读失败**绝不覆盖原文件**；<c>Exams</c> 是过路数据，Save 往返后必须逐字段不变。
/// </summary>
public sealed class ScheduleStoreTests
{
    // ── 读取 ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_真实样本_顶层各节数量与首条内容正确()
    {
        using var host = new TempHost();
        host.CopyRealSample();

        var data = new ScheduleStore(host.Root).Load();

        Assert.Equal(79, data.Entries.Count);
        Assert.Equal(2, data.Exams.Count);
        Assert.Equal(14, data.TimeTemplates.Count);
        Assert.Empty(data.MakeupDays);
        Assert.Empty(data.DayTimeTemplates);

        var first = data.Entries[0];
        Assert.Equal(1, first.DayOfWeek);
        Assert.Equal(1, first.Period);
        Assert.Equal("早读", first.Subject);
        Assert.Equal("07:00", first.StartTimeStr);
        Assert.Equal("07:50", first.EndTimeStr);
        Assert.Equal(PeriodType.Morning, first.Type);
    }

    [Fact]
    public void Load_文件不存在_返回空数据且不创建文件()
    {
        using var host = new TempHost();

        var data = new ScheduleStore(host.Root).Load();

        Assert.Empty(data.Entries);
        Assert.Empty(data.Exams);
        Assert.Empty(data.TimeTemplates);
        Assert.Empty(data.DayTimeTemplates);
        Assert.Empty(data.MakeupDays);
        Assert.False(File.Exists(host.SchedulePath));
    }

    [Fact]
    public void Load_JSON损坏_返回空数据且原文件一个字节都没动()
    {
        using var host = new TempHost();
        const string broken = "{ \"Entries\": [ { \"DayOfWeek\": 1,";
        File.WriteAllText(host.SchedulePath, broken, new UTF8Encoding(false));
        var before = File.ReadAllBytes(host.SchedulePath);

        var data = new ScheduleStore(host.Root).Load();

        Assert.Empty(data.Entries);
        Assert.Equal(before, File.ReadAllBytes(host.SchedulePath));
    }

    [Fact]
    public void Load_带UTF8_BOM的文件也能读()
    {
        using var host = new TempHost();
        var json = """
                   { "Entries": [ { "DayOfWeek": 2, "Period": 1, "Subject": "数学", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 } ] }
                   """;
        // 记事本"另存为 UTF-8"会带 BOM；Utf8JsonReader 本身不认 BOM，读取路径必须自己剥。
        File.WriteAllText(host.SchedulePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var data = new ScheduleStore(host.Root).Load();

        Assert.Single(data.Entries);
        Assert.Equal("数学", data.Entries[0].Subject);
        Assert.Equal(2, data.Entries[0].DayOfWeek);
    }

    [Fact]
    public void Load_集合写成null_归一化成空集合不抛()
    {
        using var host = new TempHost();
        File.WriteAllText(host.SchedulePath, """{ "Entries": null, "Exams": null, "MakeupDays": null }""");

        var data = new ScheduleStore(host.Root).Load();

        Assert.All(
            new object?[] { data.Entries, data.Exams, data.TimeTemplates, data.DayTimeTemplates, data.MakeupDays },
            Assert.NotNull);
    }

    // ── 写入 ──────────────────────────────────────────────────────────

    [Fact]
    public void Save往返_所有顶层节的数量与关键字段不变()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var store = new ScheduleStore(host.Root);

        var original = store.Load();
        var saved = store.Save(original);
        var reloaded = store.Load();

        Assert.True(saved.IsOk, saved.Message);
        Assert.Equal(original.Entries.Count, reloaded.Entries.Count);
        Assert.Equal(original.Exams.Count, reloaded.Exams.Count);
        Assert.Equal(original.TimeTemplates.Count, reloaded.TimeTemplates.Count);
        Assert.Equal(original.MakeupDays.Count, reloaded.MakeupDays.Count);
        Assert.Equal(original.DayTimeTemplates.Count, reloaded.DayTimeTemplates.Count);

        for (var i = 0; i < original.Entries.Count; i++)
            Assert.Equal(Describe(original.Entries[i]), Describe(reloaded.Entries[i]));

        var firstTemplate = reloaded.TimeTemplates[0];
        Assert.Equal(1, firstTemplate.Period);
        Assert.Equal("07:00", firstTemplate.StartTime);
        Assert.Equal("07:50", firstTemplate.EndTime);
        Assert.Equal(PeriodType.Morning, firstTemplate.Type);
    }

    [Fact]
    public void Save往返_Exams一节逐字段原样保留()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var store = new ScheduleStore(host.Root);

        var original = store.Load();
        store.Save(original);
        var reloaded = store.Load();

        Assert.Equal(original.Exams.Count, reloaded.Exams.Count);

        for (var i = 0; i < original.Exams.Count; i++)
            Assert.Equal(Describe(original.Exams[i]), Describe(reloaded.Exams[i]));

        // 顺带把具体内容也钉住，防止"数量对了但内容被默认值顶掉"。
        Assert.Equal("2026 高考第二次模拟", reloaded.Exams[0].Name);
        Assert.Equal("2026-09-15", reloaded.Exams[0].DateStr);
        Assert.Equal("语文", reloaded.Exams[0].Subjects[0].Name);
        Assert.Equal("09:00", reloaded.Exams[0].Subjects[0].StartTimeStr);
        Assert.Equal("11:30", reloaded.Exams[0].Subjects[0].EndTimeStr);
    }

    [Fact]
    public void Save_写回的是PascalCase键名_不加JsonPropertyName也对得上老文件()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var store = new ScheduleStore(host.Root);

        store.Save(store.Load());
        var text = File.ReadAllText(host.SchedulePath);

        Assert.Contains("\"Entries\"", text);
        Assert.Contains("\"Exams\"", text);
        Assert.Contains("\"TimeTemplates\"", text);
        Assert.Contains("\"DayTimeTemplates\"", text);
        Assert.Contains("\"MakeupDays\"", text);
        Assert.Contains("\"DayOfWeek\"", text);
        Assert.Contains("\"StartTimeStr\"", text);
        // 派生属性一旦混进模型就会被写回文件（v3.3.0 的 "TimeDisplay" 事故），这里反着断言一次。
        Assert.DoesNotContain("TimeDisplay", text);
    }

    [Fact]
    public void Save往返_MakeupDays与按天模板都保留()
    {
        using var host = new TempHost();
        var store = new ScheduleStore(host.Root);

        var data = new ScheduleData
        {
            Entries =
            [
                new ScheduleEntry
                {
                    DayOfWeek = 5, Period = 1, Subject = "数学",
                    StartTimeStr = "08:00", EndTimeStr = "08:45", Type = PeriodType.Normal,
                },
            ],
            MakeupDays = [new MakeupDay { DateStr = "2026-09-20", DayOfWeek = 5 }],
            DayTimeTemplates = new Dictionary<int, List<TimeTemplate>>
            {
                [5] =
                [
                    new TimeTemplate
                    {
                        Period = 1, StartTime = "07:30", EndTime = "08:15", Type = PeriodType.Morning,
                    },
                ],
            },
        };

        Assert.True(store.Save(data).IsOk);

        var reloaded = store.Load();
        Assert.Single(reloaded.MakeupDays);
        Assert.Equal("2026-09-20", reloaded.MakeupDays[0].DateStr);
        Assert.Equal(5, reloaded.MakeupDays[0].DayOfWeek);

        Assert.True(reloaded.DayTimeTemplates.ContainsKey(5));
        Assert.Equal("07:30", reloaded.DayTimeTemplates[5][0].StartTime);
        Assert.Equal("08:15", reloaded.DayTimeTemplates[5][0].EndTime);
        Assert.Equal(PeriodType.Morning, reloaded.DayTimeTemplates[5][0].Type);
    }

    [Fact]
    public void Save_原子写_不残留tmp文件()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var store = new ScheduleStore(host.Root);

        Assert.True(store.Save(store.Load()).IsOk);

        Assert.True(File.Exists(host.SchedulePath));
        Assert.False(File.Exists(host.SchedulePath + ".tmp"));
        Assert.False(File.Exists(host.SchedulePath + ".bak"));
        Assert.Single(Directory.GetFiles(host.Root));
    }

    [Fact]
    public void Save_目标文件已存在时也能覆盖成功()
    {
        using var host = new TempHost();
        var store = new ScheduleStore(host.Root);

        var first = new ScheduleData
        {
            Entries = [new ScheduleEntry { DayOfWeek = 1, Period = 1, Subject = "旧" }],
        };
        var second = new ScheduleData
        {
            Entries = [new ScheduleEntry { DayOfWeek = 2, Period = 1, Subject = "新" }],
        };

        Assert.True(store.Save(first).IsOk);
        Assert.True(store.Save(second).IsOk);

        var reloaded = store.Load();
        Assert.Single(reloaded.Entries);
        Assert.Equal("新", reloaded.Entries[0].Subject);
    }

    /// <summary>把一条课节压成一个可比较的字符串（失败时两侧内容都看得见）。</summary>
    private static string Describe(ScheduleEntry entry)
        => $"{entry.DayOfWeek}/{entry.Period}/{entry.Subject}/{entry.StartTimeStr}-{entry.EndTimeStr}/{entry.Type}";

    /// <summary>把一场考试连同全部科目压成一个可比较的字符串。</summary>
    private static string Describe(ExamEntry exam)
        => $"{exam.Name}|{exam.DateStr}|" +
           string.Join(";", exam.Subjects.Select(s => $"{s.Name}{s.StartTimeStr}-{s.EndTimeStr}"));
}
