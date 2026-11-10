using System.IO;
using System.Text.RegularExpressions;
using StudyJourney.Plugin.Schedule;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// <see cref="ScheduleProvider"/> 的投影与缓存断言：调休映射必须落到 <see cref="TodaySchedule"/>，
/// 节次必须按 <c>Period</c> 升序，时间串必须是契约要求的 <c>HH:mm</c>。
/// </summary>
public sealed class ScheduleProviderTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);      // 周一
    private static readonly DateOnly Sunday = new(2026, 9, 20);      // 周日（样本里被安排补周五）

    private static ScheduleProvider CreateProvider(TempHost host)
    {
        var provider = new ScheduleProvider(new ScheduleStore(host.Root));
        provider.Reload();
        return provider;
    }

    [Fact]
    public async Task GetDayAsync_调休日_按被补的那一天返回课表()
    {
        using var host = new TempHost();
        Write(host, """
                    {
                      "Entries": [
                        { "DayOfWeek": 5, "Period": 1, "Subject": "周五数学", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 },
                        { "DayOfWeek": 7, "Period": 1, "Subject": "周日自习", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 }
                      ],
                      "MakeupDays": [ { "DateStr": "2026-09-20", "DayOfWeek": 5 } ]
                    }
                    """);
        var provider = CreateProvider(host);

        var result = await provider.GetDayAsync(Sunday);

        Assert.True(result.IsOk);
        var schedule = Assert.IsType<TodaySchedule>(result.Value);
        Assert.Equal(5, schedule.EffectiveDayOfWeek);
        Assert.True(schedule.IsMakeupDay);
        Assert.Equal(Sunday, schedule.Date);
        Assert.Single(schedule.Periods);
        Assert.Equal("周五数学", schedule.Periods[0].Subject);
        Assert.Equal(5, schedule.Periods[0].DayOfWeek);
    }

    [Fact]
    public async Task GetDayAsync_普通周日_归一化为7且不是调休()
    {
        using var host = new TempHost();
        Write(host, """
                    {
                      "Entries": [
                        { "DayOfWeek": 7, "Period": 1, "Subject": "周日自习", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 }
                      ]
                    }
                    """);
        var provider = CreateProvider(host);

        var result = await provider.GetDayAsync(Sunday);

        Assert.True(result.IsOk);
        var schedule = Assert.IsType<TodaySchedule>(result.Value);
        Assert.Equal(7, schedule.EffectiveDayOfWeek);
        Assert.False(schedule.IsMakeupDay);
        Assert.Equal("周日自习", schedule.Periods[0].Subject);
    }

    [Fact]
    public async Task GetDayAsync_节次按Period升序()
    {
        using var host = new TempHost();
        // 故意打乱写入顺序（真实文件里老师也是乱序加的）。
        Write(host, """
                    {
                      "Entries": [
                        { "DayOfWeek": 1, "Period": 3, "Subject": "英语", "StartTimeStr": "09:15", "EndTimeStr": "10:00", "Type": 0 },
                        { "DayOfWeek": 1, "Period": 1, "Subject": "早读", "StartTimeStr": "07:00", "EndTimeStr": "07:50", "Type": 1 },
                        { "DayOfWeek": 1, "Period": 2, "Subject": "生物", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 },
                        { "DayOfWeek": 2, "Period": 1, "Subject": "不是这一天的", "StartTimeStr": "07:00", "EndTimeStr": "07:50", "Type": 0 }
                      ]
                    }
                    """);
        var provider = CreateProvider(host);

        var result = await provider.GetDayAsync(Monday);

        var periods = Assert.IsType<TodaySchedule>(result.Value).Periods;
        Assert.Equal(new[] { 1, 2, 3 }, periods.Select(p => p.Period).ToArray());
        Assert.Equal(new[] { "早读", "生物", "英语" }, periods.Select(p => p.Subject).ToArray());
    }

    [Fact]
    public async Task GetDayAsync_真实样本_时间串全部是HH_mm格式()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var provider = CreateProvider(host);
        var pattern = new Regex(@"^[0-2]\d:[0-5]\d$");

        // 整周都扫一遍：任何一天里有一节课的时间格式不对，主窗/提醒都会跟着错。
        for (var day = 1; day <= 7; day++)
        {
            var result = await provider.GetDayAsync(new DateOnly(2026, 9, 14).AddDays(day - 1));
            Assert.True(result.IsOk);

            foreach (var period in Assert.IsType<TodaySchedule>(result.Value).Periods)
            {
                Assert.Matches(pattern, period.StartTimeStr);
                Assert.Matches(pattern, period.EndTimeStr);
                Assert.False(string.IsNullOrWhiteSpace(period.Subject));
            }
        }
    }

    [Fact]
    public async Task GetDayAsync_空课表_返回空列表不抛()
    {
        using var host = new TempHost();
        var provider = CreateProvider(host);      // 连文件都没有

        var result = await provider.GetDayAsync(Monday);

        Assert.True(result.IsOk);
        var schedule = Assert.IsType<TodaySchedule>(result.Value);
        Assert.Empty(schedule.Periods);
        Assert.Equal(1, schedule.EffectiveDayOfWeek);
        Assert.False(schedule.IsMakeupDay);
    }

    [Fact]
    public async Task GetTodayAsync_等价于GetDayAsync今天()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var provider = CreateProvider(host);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var viaToday = await provider.GetTodayAsync();
        var viaDay = await provider.GetDayAsync(today);

        Assert.True(viaToday.IsOk);
        Assert.Equal(viaDay.Value!.Date, viaToday.Value!.Date);
        Assert.Equal(viaDay.Value!.EffectiveDayOfWeek, viaToday.Value!.EffectiveDayOfWeek);
        Assert.Equal(viaDay.Value!.Periods.Count, viaToday.Value!.Periods.Count);
    }

    [Fact]
    public async Task Reload_自增版本号并立即失效缓存()
    {
        using var host = new TempHost();
        Write(host, """
                    { "Entries": [ { "DayOfWeek": 1, "Period": 1, "Subject": "旧", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 } ] }
                    """);
        var provider = CreateProvider(host);

        var before = await provider.GetDayAsync(Monday);
        Assert.Equal("旧", before.Value!.Periods[0].Subject);
        var versionBefore = provider.Version;

        Write(host, """
                    { "Entries": [ { "DayOfWeek": 1, "Period": 1, "Subject": "新", "StartTimeStr": "08:00", "EndTimeStr": "08:45", "Type": 0 } ] }
                    """);
        var versionAfter = provider.Reload();

        // 关键：200ms TTL 还没过期也必须立刻看到新数据（Reload 要清缓存）。
        var after = await provider.GetDayAsync(Monday);
        Assert.Equal("新", after.Value!.Periods[0].Subject);
        Assert.True(versionAfter > versionBefore);
        Assert.Equal(versionAfter, provider.Version);
    }

    [Fact]
    public void ResolveEffectiveDayOfWeek_走纯函数的同一套语义()
    {
        using var host = new TempHost();
        Write(host, """
                    { "Entries": [], "MakeupDays": [ { "DateStr": "2026-09-20", "DayOfWeek": 5 } ] }
                    """);
        var provider = CreateProvider(host);

        Assert.Equal(5, provider.ResolveEffectiveDayOfWeek(Sunday));
        Assert.Equal(1, provider.ResolveEffectiveDayOfWeek(Monday));
    }

    [Fact]
    public async Task GetDayAsync_时间写法不规范时归一化成HH_mm()
    {
        using var host = new TempHost();
        Write(host, """
                    {
                      "Entries": [
                        { "DayOfWeek": 1, "Period": 1, "Subject": "带秒", "StartTimeStr": "7:05:00", "EndTimeStr": "8:00:00", "Type": 0 }
                      ]
                    }
                    """);
        var provider = CreateProvider(host);

        var result = await provider.GetDayAsync(Monday);

        var period = Assert.Single(Assert.IsType<TodaySchedule>(result.Value).Periods);
        Assert.Equal("07:05", period.StartTimeStr);
        Assert.Equal("08:00", period.EndTimeStr);
    }

    private static void Write(TempHost host, string json)
        => File.WriteAllText(host.SchedulePath, json, new System.Text.UTF8Encoding(false));
}
