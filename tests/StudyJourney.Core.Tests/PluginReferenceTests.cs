using Xunit;

namespace StudyJourney.Core.Tests;

/// <summary>
/// 测试 2（S2-4）：plugins/Test 的 csproj 只引用 Sdk。
///
/// 这是 Directory.Build.targets 的**测试侧镜像**：
///   targets 在构建期拦（会真的失败），这里在测试期再断言一次。
///   两者不是重复劳动——targets 只在"构建 plugins 子树"时生效，
///   而有人可能直接改 csproj 却只跑测试不构建，或 CI 只跑测试。
///
/// ⚠ 断言不看文件名写死：目录里应只有一个 csproj，名字随 S1 的 renaming 而变
///   （当前是 plugins/Test/Test.csproj，曾经是 plugins/TestPlugin/TestPlugin.csproj）。
/// </summary>
public sealed class PluginReferenceTests
{
    private const string TestPluginDir = "plugins/Test";

    private static string PluginCsproj()
    {
        var dir = RepoRoot.Combine("plugins", "Test");
        Assert.True(Directory.Exists(dir), $"找不到插件目录：{dir}");

        var files = Directory.GetFiles(dir, "*.csproj");
        Assert.Single(files);
        return files[0];
    }

    [Fact(DisplayName = "测试2-a：插件目录存在且只有一个 csproj")]
    public void Plugin_Project_Exists()
    {
        var csproj = PluginCsproj();
        Assert.EndsWith("Test.csproj", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "测试2-b：插件只引用 StudyJourney.Sdk（且仅此一个）")]
    public void Plugin_References_Only_Sdk()
    {
        var refs = RepoRoot.ReadProjectReferences(PluginCsproj());

        Assert.Single(refs);
        Assert.EndsWith("src/StudyJourney.Sdk/StudyJourney.Sdk.csproj", refs[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "测试2-c：插件没有引用 Core / App / 其它插件")]
    public void Plugin_Has_No_Forbidden_References()
    {
        var refs = RepoRoot.ReadProjectReferences(PluginCsproj());

        Assert.DoesNotContain(refs, r => r.Contains("StudyJourney.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("StudyJourney.App", StringComparison.OrdinalIgnoreCase));

        // 其它插件（StudyJourney.Plugin.* 的实现工程）也不许直接引用
        Assert.DoesNotContain(refs, r =>
            r.Contains("/plugins/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "测试2-d：插件没有引用 Avalonia.Desktop（那是 App 的）")]
    public void Plugin_Has_No_Desktop_Package()
    {
        var doc = System.Xml.Linq.XDocument.Load(PluginCsproj());
        var packages = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.DoesNotContain("Avalonia.Desktop", packages);

        // 本阶段插件一个包都不该自己声明——依赖全部经 Sdk 传递
        Assert.Empty(packages);
    }
}
