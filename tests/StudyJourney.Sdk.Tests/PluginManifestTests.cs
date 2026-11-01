using System.Text.Json;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Sdk.Tests;

/// <summary>
/// 测试 5-a：<see cref="PluginManifest"/> 序列化往返。
///
/// 守的是一件具体的事：<c>plugin.json</c> 是**人工手写的文件**，
/// 字段名一旦写错（camelCase 写成 snake_case、configFields 写成 configKeys），
/// 症状是"插件被拒载"或"配置读不到"，而不是编译错误。
/// 往返测试保证"我们写出去的 JSON 我们自己读得回来"。
/// </summary>
public sealed class PluginManifestTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private static PluginManifest Sample() => new()
    {
        Id = "countdown",
        Name = "倒计时",
        Version = "1.2.3",
        Entry = "StudyJourney.Plugin.Countdown.dll",
        CoreApi = "1.x",
        Dependencies = ["schedule"],
        ConfigFields = ["GaokaoDateStr", "StartDateStr", "ShowDays"],
        ConfigSection = null,
        ConfigFieldsShared = null,
        Description = "高考倒计时与每日一言",
        Author = "XEKernel",
        MinHostVersion = "4.0.0",
    };

    [Fact(DisplayName = "测试5-a-1：PluginManifest 序列化 → 反序列化，字段全部一致")]
    public void Manifest_RoundTrips()
    {
        var original = Sample();

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<PluginManifest>(json, Options);

        Assert.NotNull(back);
        Assert.Equal(original.Id, back!.Id);
        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.Version, back.Version);
        Assert.Equal(original.Entry, back.Entry);
        Assert.Equal(original.CoreApi, back.CoreApi);
        Assert.Equal(original.Description, back.Description);
        Assert.Equal(original.Author, back.Author);
        Assert.Equal(original.MinHostVersion, back.MinHostVersion);
        Assert.Null(back.ConfigSection);
        Assert.Null(back.ConfigFieldsShared);

        Assert.Equal(original.Dependencies, back.Dependencies);
        Assert.Equal(original.ConfigFields, back.ConfigFields);
    }

    [Fact(DisplayName = "测试5-a-2：JSON 里必须出现 configFields（不是 configKeys）")]
    public void Json_Uses_ConfigFields_Key()
    {
        var json = JsonSerializer.Serialize(Sample(), Options);

        Assert.Contains("\"configFields\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"configKeys\"", json, StringComparison.Ordinal);

        // 其余必需键也要真的写出去
        foreach (var key in new[] { "\"id\"", "\"name\"", "\"version\"", "\"entry\"", "\"coreApi\"", "\"dependencies\"" })
            Assert.Contains(key, json, StringComparison.Ordinal);

        // 可选项为 null 时仍会输出（STJ 默认），这不影响读取；但只要不出现值就行
        Assert.Contains("\"configSection\": null", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "测试5-a-3：缺省值合法（空数组、无 configSection）")]
    public void Manifest_With_Minimal_Json_Is_Valid()
    {
        const string minimal = """
        {
          "id": "test",
          "name": "测试",
          "version": "0.0.1",
          "entry": "StudyJourney.Plugin.Test.dll",
          "coreApi": "1.x",
          "configFields": ["TestField"]
        }
        """;

        var m = JsonSerializer.Deserialize<PluginManifest>(minimal, Options);

        Assert.NotNull(m);
        Assert.Equal("test", m!.Id);
        Assert.Single(m.ConfigFields);
        Assert.Empty(m.Dependencies);
        Assert.Null(m.ConfigSection);
    }

    [Fact(DisplayName = "测试5-a-5：老清单残留已废弃的 hasSettingsPage 键，必须被安静忽略")]
    public void Deprecated_HasSettingsPage_Key_Is_Ignored()
    {
        // 该字段已从 manifest 删除（设置页有无改由 ISettingsContributor.GetPages() 决定）。
        // 但老师/旧版本手写的 plugin.json 里可能还留着它 —— 手写文件不该因为
        // 一个废弃键就整份读不了（"插件被拒载"而原因看不出来，是最难查的一类）。
        const string withDeprecated = """
        {
          "id": "test",
          "name": "测试",
          "version": "0.0.1",
          "entry": "StudyJourney.Plugin.Test.dll",
          "coreApi": "1.x",
          "configFields": ["TestField"],
          "hasSettingsPage": false
        }
        """;

        var m = JsonSerializer.Deserialize<PluginManifest>(withDeprecated, Options);

        Assert.NotNull(m);
        Assert.Equal("test", m!.Id);
        Assert.Single(m.ConfigFields);

        // 且我们自己写出去的 JSON 里不该再有这个键
        Assert.DoesNotContain("hasSettingsPage", JsonSerializer.Serialize(m, Options), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "测试5-a-4：coreApi 只比对主版本")]
    [InlineData("1.x", true)]
    [InlineData("1", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.0", false)]
    [InlineData("0.9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void CoreApi_Compat_Only_Compares_Major(string? coreApi, bool expected)
    {
        Assert.Equal(expected, CoreInfo.IsApiCompatible(coreApi));
    }
}
