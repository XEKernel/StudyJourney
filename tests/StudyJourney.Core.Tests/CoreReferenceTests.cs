using System.Reflection;
using Xunit;

namespace StudyJourney.Core.Tests;

/// <summary>
/// 测试 1（S2-3）：Core 能被引用而不启动 Avalonia。
///
/// 这是 F1「Core / App 分立」的价值所在：
///   测试工程只引用 Core，就能拿到核心全部能力；
///   若 Core 自己拖进了桌面层，它就"有能力"起一个桌面进程，
///   这条测试就是那个边界的哨兵。
///
/// ⚠ 刻意**不**断言"进程里没有 Avalonia.Application 实例"——
///    那要反射私有字段，Avalonia 版本一升就碎，且它测的是运行时行为，
///    而 M0 要测的是"Core 的依赖面干净"。
/// </summary>
public sealed class CoreReferenceTests
{
    /// <summary>任何出现在 Core 引用列表里都算越界的桌面层程序集。</summary>
    private static readonly string[] DesktopAssemblies =
    [
        "Avalonia.Desktop",
        "Avalonia.Win32",
        "Avalonia.Skia",
        "Avalonia.Native",
        "Avalonia.X11",
        "Avalonia.FreeDesktop",
        "Avalonia.Themes.Fluent",
    ];

    [Fact(DisplayName = "测试1-a：Core 程序集可被引用并加载")]
    public void Core_Assembly_Is_Referenceable()
    {
        // Core 在 M0 阶段还没有任何类型（S3 写的是 Sdk），所以按程序集名加载，
        // 而不是 typeof(某类型)。这同样证明"引用了 Core 就能把它拉起来"。
        var asm = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName("StudyJourney.Core"));

        Assert.Equal("StudyJourney.Core", asm.GetName().Name);
        Assert.NotEmpty(asm.GetName().Name!);
    }

    [Fact(DisplayName = "测试1-b：Core 的引用列表里没有任何桌面层程序集")]
    public void Core_Does_Not_Reference_Desktop_Assemblies()
    {
        var asm = System.Reflection.Assembly.Load(new System.Reflection.AssemblyName("StudyJourney.Core"));

        var referenced = asm.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        var offending = referenced
            .Where(r => DesktopAssemblies.Any(d => r.Equals(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"Core 引用了桌面层程序集：{string.Join(", ", offending)}。\n" +
            $"当前引用列表：{string.Join(", ", referenced)}");
    }

    [Fact(DisplayName = "测试1-c：进程内没有加载 Avalonia.Desktop")]
    public void Desktop_Assembly_Is_Not_Loaded_In_Process()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain("Avalonia.Desktop", loaded, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 测试 1-d：正向证明"Avalonia 在 Core 的编译期引用闭包里"。
    ///
    /// 为什么不用 Assembly.GetReferencedAssemblies() 判正向：
    ///   AssemblyRef 表只记录**实际用到**的程序集。Core 目前一行代码都没有，
    ///   一行 Avalonia 都没用 → 引用列表里本来就不会出现 Avalonia，
    ///   这不是"没引用"，而是"还没用"。要等 S10 的 ShellHost 用到 Control 才会出现。
    ///   所以正向事实改从 csproj 的包引用闭包里取：Sdk 声明 Avalonia，Core 引用 Sdk。
    /// </summary>
    [Fact(DisplayName = "测试1-d：Avalonia 在 Core 的编译期引用闭包里（Sdk → Avalonia）")]
    public void Avalonia_Is_In_Core_Compile_Closure()
    {
        var sdkCsproj = RepoRoot.Combine("src", "StudyJourney.Sdk", "StudyJourney.Sdk.csproj");
        Assert.True(File.Exists(sdkCsproj), $"找不到 {sdkCsproj}");

        var sdkPackages = System.Xml.Linq.XDocument.Load(sdkCsproj)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.Contains("Avalonia", sdkPackages);

        // Core 必须直接引用 Sdk，否则上面的 Avalonia 传不到 Core
        var coreRefs = RepoRoot.ReadProjectReferences(
            RepoRoot.Combine("src", "StudyJourney.Core", "StudyJourney.Core.csproj"));

        Assert.Contains(coreRefs, r => r.EndsWith("StudyJourney.Sdk/StudyJourney.Sdk.csproj", StringComparison.Ordinal));

        // 反向：Sdk 不得引桌面壳（Avalonia.Desktop 只属于 App）
        Assert.DoesNotContain("Avalonia.Desktop", sdkPackages);
    }
}
