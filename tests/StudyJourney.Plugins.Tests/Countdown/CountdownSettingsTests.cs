using System.Text.Json.Nodes;
using StudyJourney.Plugin.Countdown;
using Xunit;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>
/// 配置容错测试：节为 null / 键缺失 / 类型不符 三种情形都必须"读得下去"，
/// 且只有**类型不符**才记 Warn（缺失是常态，天天告警等于没告警）。
/// </summary>
public sealed class CountdownSettingsTests
{
    [Fact(DisplayName = "M1B-13 空配置节（null）→ 8 个字段全用默认值，零告警")]
    public void NullSection_UsesAllDefaults()
    {
        var logger = new FakePluginLogger();
        var settings = CountdownSettings.FromSection(null, logger);

        Assert.Equal("2027-06-07 09:00:00", settings.GaokaoDateStr);
        Assert.Equal("2024-08-24", settings.StartDateStr);
        Assert.True(settings.ShowDays);
        Assert.True(settings.ShowHours);
        Assert.True(settings.ShowMinutes);
        Assert.True(settings.ShowSeconds);
        Assert.True(settings.ShowProgressBar);
        Assert.False(settings.CountdownProgressBarStyle);
        Assert.Empty(logger.Warns);
    }

    [Fact(DisplayName = "M1B-14 空对象（键全缺失）→ 同样是默认值")]
    public void EmptyObject_UsesAllDefaults()
    {
        var settings = CountdownSettings.FromSection(new JsonObject(), null);

        Assert.Equal(CountdownSettings.DefaultGaokaoDate, settings.GaokaoDateStr);
        Assert.Equal(CountdownSettings.DefaultStartDate, settings.StartDateStr);
        Assert.True(settings.ShowDays);
        Assert.False(settings.CountdownProgressBarStyle);
    }

    [Fact(DisplayName = "M1B-15 8 个字段都给出非默认值时被逐一读到")]
    public void AllFieldsProvided_AreRead()
    {
        var section = new JsonObject
        {
            ["GaokaoDateStr"] = "2028-06-07 09:00",
            ["StartDateStr"] = "2025-09-01",
            ["ShowDays"] = false,
            ["ShowHours"] = false,
            ["ShowMinutes"] = false,
            ["ShowSeconds"] = false,
            ["ShowProgressBar"] = false,
            ["CountdownProgressBarStyle"] = true,
        };

        var settings = CountdownSettings.FromSection(section, null);

        Assert.Equal("2028-06-07 09:00", settings.GaokaoDateStr);
        Assert.Equal("2025-09-01", settings.StartDateStr);
        Assert.False(settings.ShowDays);
        Assert.False(settings.ShowHours);
        Assert.False(settings.ShowMinutes);
        Assert.False(settings.ShowSeconds);
        Assert.False(settings.ShowProgressBar);
        Assert.True(settings.CountdownProgressBarStyle);
    }

    [Theory(DisplayName = "M1B-16 布尔字段类型不符（存了字符串）→ 回落默认值 + 记 Warn，不抛")]
    [InlineData("ShowDays", true)]
    [InlineData("ShowHours", true)]
    [InlineData("ShowMinutes", true)]
    [InlineData("ShowSeconds", true)]
    [InlineData("ShowProgressBar", true)]
    [InlineData("CountdownProgressBarStyle", false)]
    public void BoolField_WrongType_FallsBackAndWarns(string key, bool expected)
    {
        var logger = new FakePluginLogger();
        var settings = CountdownSettings.FromSection(new JsonObject { [key] = "true" }, logger);

        Assert.Equal(expected, ReadBool(settings, key));
        var warn = Assert.Single(logger.Warns);
        Assert.Contains(key, warn, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "M1B-17 字符串 \"True\"/\"FALSE\" 等写法不被当作布尔（大小写不猜）")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("False")]
    [InlineData("FALSE")]
    public void BoolField_StringForms_AreRejected(string text)
    {
        var logger = new FakePluginLogger();
        var settings = CountdownSettings.FromSection(new JsonObject { ["ShowHours"] = text }, logger);

        Assert.True(settings.ShowHours);          // 默认 true："False" 不会被静默当成 false
        Assert.Single(logger.Warns);
    }

    [Fact(DisplayName = "M1B-18 日期字段类型不符（数字）→ 回落默认值 + 记 Warn")]
    public void DateField_WrongType_FallsBackAndWarns()
    {
        var logger = new FakePluginLogger();
        var settings = CountdownSettings.FromSection(new JsonObject { ["GaokaoDateStr"] = 20270607 }, logger);

        Assert.Equal(CountdownSettings.DefaultGaokaoDate, settings.GaokaoDateStr);
        Assert.Single(logger.Warns);
    }

    [Fact(DisplayName = "M1B-19 日期串非法不抛：原样保存，解析失败留给插件降级")]
    public void InvalidDateText_IsKeptVerbatim()
    {
        const string bad = "明年六月";
        var logger = new FakePluginLogger();

        var settings = CountdownSettings.FromSection(new JsonObject { ["GaokaoDateStr"] = bad }, logger);

        Assert.Equal(bad, settings.GaokaoDateStr);
        Assert.False(CountdownMath.TryParseDateTime(settings.GaokaoDateStr, out _));
        Assert.Empty(logger.Warns);   // 解析是插件的事，本类只负责取值
    }

    [Fact(DisplayName = "M1B-20 键名区分大小写：\"showdays\" 不被当成 ShowDays")]
    public void FieldNames_AreCaseSensitive()
    {
        var logger = new FakePluginLogger();
        var settings = CountdownSettings.FromSection(new JsonObject { ["showdays"] = false }, logger);

        Assert.True(settings.ShowDays);
        Assert.Empty(logger.Warns);
    }

    private static bool ReadBool(CountdownSettings settings, string key) => key switch
    {
        "ShowDays" => settings.ShowDays,
        "ShowHours" => settings.ShowHours,
        "ShowMinutes" => settings.ShowMinutes,
        "ShowSeconds" => settings.ShowSeconds,
        "ShowProgressBar" => settings.ShowProgressBar,
        "CountdownProgressBarStyle" => settings.CountdownProgressBarStyle,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "未知布尔字段"),
    };
}
