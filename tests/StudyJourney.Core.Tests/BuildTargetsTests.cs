using Xunit;

namespace StudyJourney.Core.Tests;

/// <summary>
/// 测试 3（S2-5）：plugins/Directory.Build.targets 存在且内容正确。
///
/// 这类"检查文件内容"的测试看起来弱，但它守的是一件具体的事：
///   有人为了"顺手修好构建"，会把 BeforeTargets 从 Build 改成 Compile 或 BeforeBuild，
///   或者把文件挪到仓库根（那样会波及 v3.3.0 的两个旧工程）。
///   前者让拦截时机漂移，后者让影响面失控——两种都不会报错，只会静悄悄地变弱。
/// </summary>
public sealed class BuildTargetsTests
{
    private static string TargetsPath => RepoRoot.Combine("plugins", "Directory.Build.targets");

    [Fact(DisplayName = "测试3-a：plugins/Directory.Build.targets 存在")]
    public void Targets_File_Exists()
    {
        Assert.True(File.Exists(TargetsPath), $"找不到 {TargetsPath}");
    }

    [Fact(DisplayName = "测试3-b：拦截时机是 BeforeTargets=\"Build\"")]
    public void Targets_Hooks_Before_Build()
    {
        var text = File.ReadAllText(TargetsPath);

        Assert.Contains("BeforeTargets=\"Build\"", text, StringComparison.Ordinal);

        // 防退化：这些写法都不是我们要的时机
        Assert.DoesNotContain("BeforeTargets=\"Compile\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BeforeTargets=\"BeforeBuild\"", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "测试3-c：targets 里点名禁止 StudyJourney.Core / App")]
    public void Targets_Mentions_Forbidden_Projects()
    {
        var text = File.ReadAllText(TargetsPath);

        Assert.Contains("StudyJourney.Core", text, StringComparison.Ordinal);
        Assert.Contains("StudyJourney.App", text, StringComparison.Ordinal);
        Assert.Contains("StudyJourney.Sdk", text, StringComparison.Ordinal);

        // 必须有 Error（阻断），不能只是 Warning（只提示）
        Assert.Contains("<Error", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<Warning", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "测试3-d：没有把 targets 放到仓库根（会波及 v3.3.0 旧工程）")]
    public void Targets_Is_Not_At_Repo_Root()
    {
        var rootTargets = RepoRoot.Combine("Directory.Build.targets");
        var rootProps = RepoRoot.Combine("Directory.Build.props");

        Assert.False(File.Exists(rootTargets),
            "仓库根出现了 Directory.Build.targets，它会被 v3.3.0 的旧工程继承——请放回 plugins/ 下。");
        Assert.False(File.Exists(rootProps),
            "仓库根出现了 Directory.Build.props，同样会波及旧工程。");
    }

    [Fact(DisplayName = "测试3-e：targets 在 plugins 子目录（就近优先），且只声明一个检查 target")]
    public void Targets_Declares_Check_Target_In_Plugins_Dir()
    {
        var text = File.ReadAllText(TargetsPath);

        Assert.Contains("SJ_CheckPluginReferences", text, StringComparison.Ordinal);
        Assert.Contains("$(MSBuildProjectName)", text, StringComparison.Ordinal);

        // 文件必须真的位于 plugins/ 下（而不是被复制到别处）
        Assert.Equal(
            Path.GetFullPath(RepoRoot.Combine("plugins")),
            Path.GetFullPath(Path.GetDirectoryName(TargetsPath)!),
            ignoreCase: true);
    }
}
