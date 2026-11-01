using System.Reflection;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Sdk.Tests;

/// <summary>
/// 测试 5-c：<see cref="ISettingsPage"/> 的**形状**。
///
/// 为什么不断言"Load 与 Apply 同实例"：那是**实现**的性质，需要 mock 一个页面跑一遍，
/// 而 M0 阶段没有任何实现（实现要到 M1 的倒计时插件）。
/// 能在这层守住的只有"接口只暴露这三个成员，且签名不许漂"——
/// 一旦有人加了第四个成员或把 Apply 改成 void，三条隐含语义就无处附着。
/// </summary>
public sealed class SettingsPageContractTests
{
    // ⚠ 必须排除 IsSpecialName：属性访问器（get_IsDirty）在反射里也是一个 MethodInfo，
    //   不排除就会把"2 个方法 + 1 个属性"数成"3 个方法"。
    private static readonly MethodInfo[] Methods =
        typeof(ISettingsPage)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .ToArray();

    private static readonly PropertyInfo[] Properties =
        typeof(ISettingsPage).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact(DisplayName = "测试5-c-1：ISettingsPage 恰好 3 个成员（2 方法 + 1 属性）")]
    public void Has_Exactly_Three_Members()
    {
        Assert.Equal(2, Methods.Length);
        Assert.Single(Properties);

        Assert.Equal(
            new[] { "Apply", "Load" },
            Methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("IsDirty", Properties[0].Name);
    }

    [Fact(DisplayName = "测试5-c-2：Load 无参无返回；Apply 无参且返回 Result；IsDirty 是只读 bool")]
    public void Signatures_Are_Stable()
    {
        var load = typeof(ISettingsPage).GetMethod("Load")!;
        Assert.Empty(load.GetParameters());
        Assert.Equal(typeof(void), load.ReturnType);

        var apply = typeof(ISettingsPage).GetMethod("Apply")!;
        Assert.Empty(apply.GetParameters());
        Assert.Equal(typeof(Result), apply.ReturnType);

        var dirty = typeof(ISettingsPage).GetProperty("IsDirty")!;
        Assert.Equal(typeof(bool), dirty.PropertyType);
        Assert.True(dirty.CanRead);
        Assert.False(dirty.CanWrite);
    }

    [Fact(DisplayName = "测试5-c-3：三条隐含语义写在接口注释里（防被删）")]
    public void Contract_Doc_Is_Present()
    {
        // 注释不是代码，但它是这份契约唯一的"说明书"。
        // 用源码文件做一次存在性断言：三条语义的关键词必须都在。
        var path = LocateSdkFile("ISettingsPage.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("同一个配置实例", text, StringComparison.Ordinal);
        Assert.Contains("必须实现", text, StringComparison.Ordinal);
        Assert.Contains("控件当前值", text, StringComparison.Ordinal);
        Assert.Contains("v3.2.2", text, StringComparison.Ordinal);
    }

    private static string LocateSdkFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "StudyJourney.Sdk", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"找不到 src/StudyJourney.Sdk/{fileName}");
    }
}
