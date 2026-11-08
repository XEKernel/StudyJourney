using StudyJourney.Core.Config;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Core.Tests.Config;

/// <summary>
/// S7：<c>configFields</c> 全局冲突检测（CORE_ARCHITECTURE §5.0.1 / A0-5c）。
///
/// 这一节守的是"结果与加载顺序无关"：多个插件声明同一字段时，**字典序**保留第一个，
/// 其余整份拒载。若实现成"边读边拒"，同一台机器换个文件系统枚举顺序结果就变了。
/// </summary>
public sealed class ConfigFieldClaimsTests
{
    private static PluginManifest Manifest(string id, params string[] fields) => new()
    {
        Id = id,
        Name = id,
        Version = "1.0.0",
        Entry = id + ".dll",
        CoreApi = "1.x",
        ConfigFields = fields,
    };

    [Fact(DisplayName = "S7-18 无冲突：所有字段都被认领")]
    public void NoConflict_AllFieldsClaimed()
    {
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("countdown", "CountdownFontSize", "CountdownTarget"),
            Manifest("reminder", "RemindClassStart"),
        });

        Assert.Empty(report.Conflicts);
        Assert.Empty(report.RejectedPluginIds);
        Assert.Equal("countdown", report.FieldOwners["CountdownFontSize"]);
        Assert.Equal("reminder", report.FieldOwners["RemindClassStart"]);
    }

    [Fact(DisplayName = "S7-19 冲突：字典序保留第一个，后者拒载（与传入顺序无关）")]
    public void Conflict_KeepsLexicographicallyFirst_IndependentOfInputOrder()
    {
        // 刻意把 b 放前面：若实现依赖输入顺序，这条会失败
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("b-plugin", "FontSize"),
            Manifest("a-plugin", "FontSize"),
        });

        Assert.Equal("a-plugin", report.FieldOwners["FontSize"]);
        Assert.Contains("b-plugin", report.RejectedPluginIds);
        Assert.DoesNotContain("a-plugin", report.RejectedPluginIds);

        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("FontSize", conflict.FieldName);
        Assert.Equal("a-plugin", conflict.FirstPluginId);
        Assert.Equal("b-plugin", conflict.SecondPluginId);
    }

    [Fact(DisplayName = "S7-20 冲突提示点名双方插件与字段（§5.0.1 文案）")]
    public void Conflict_Message_NamesBothPluginsAndField()
    {
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("alpha", "FontSize"),
            Manifest("beta", "FontSize"),
        });

        var message = Assert.Single(report.Conflicts).Message;
        Assert.Contains("beta", message, StringComparison.Ordinal);
        Assert.Contains("alpha", message, StringComparison.Ordinal);
        Assert.Contains("FontSize", message, StringComparison.Ordinal);
        Assert.Contains("拒绝加载", message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S7-21 被拒载插件一个字段都不认领（不许半认领）")]
    public void RejectedPlugin_ClaimsNothing()
    {
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("alpha", "FontSize"),
            Manifest("beta", "FontSize", "BetaOnly"),
        });

        Assert.Contains("beta", report.RejectedPluginIds);
        Assert.False(report.FieldOwners.ContainsKey("BetaOnly"), "被拒载插件认领的其它字段也必须作废");
    }

    [Fact(DisplayName = "S7-22 同一插件重复写自己的字段不算冲突")]
    public void SamePluginDuplicateField_IsNotConflict()
    {
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("alpha", "FontSize", "FontSize"),
        });

        Assert.Empty(report.Conflicts);
        Assert.Equal("alpha", report.FieldOwners["FontSize"]);
    }

    [Fact(DisplayName = "S7-23 三方争抢同一字段：只保留字典序第一个，其余全部拒载")]
    public void ThreeWayConflict_OnlyFirstSurvives()
    {
        var report = ConfigFieldClaimReport.Resolve(new[]
        {
            Manifest("charlie", "FontSize"),
            Manifest("bravo", "FontSize"),
            Manifest("alpha", "FontSize"),
        });

        Assert.Equal("alpha", report.FieldOwners["FontSize"]);
        Assert.Equal(new[] { "bravo", "charlie" }, report.RejectedPluginIds.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, report.Conflicts.Count);
    }

    [Fact(DisplayName = "S7-24 空 configFields 合法（纯后台无配置插件）")]
    public void EmptyConfigFields_IsLegal()
    {
        var report = ConfigFieldClaimReport.Resolve(new[] { Manifest("diag") });

        Assert.Empty(report.Conflicts);
        Assert.Empty(report.RejectedPluginIds);
        Assert.Empty(report.FieldOwners);
    }
}
