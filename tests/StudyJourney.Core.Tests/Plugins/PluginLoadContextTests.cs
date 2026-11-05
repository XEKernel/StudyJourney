using System.Reflection;
using System.Runtime.CompilerServices;
using StudyJourney.Core.Plugins;
using Xunit;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// 测试 8：<see cref="PluginLoadContext"/> 的三个重写（CORE_ARCHITECTURE §0.8）。
///
/// 这组测试守的是**唯一"写错了不报错"的地方**：白名单漏一个 → 类型身份分裂 →
/// 表现为"注册成功但取不到"，而不是编译错误或异常堆栈。
/// </summary>
public sealed class PluginLoadContextTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sj-alc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* 加载过的 dll 可能仍被占用；临时目录留给系统清理，测试不因此失败 */ }
    }

    // ── 8-a/8-b/8-c：白名单里的名字 → Load 返回 null（交还默认 ALC）──

    [Theory(DisplayName = "测试8-a/b/c：白名单程序集 → Load 返回 null（不抛、不自己加载）")]
    [InlineData("StudyJourney.Sdk")]          // 契约层：类型身份一致的唯一来源
    [InlineData("Avalonia.Controls")]         // UI：前缀白名单
    [InlineData("System.Private.CoreLib")]    // 运行时核心：精确白名单
    [InlineData("System.Text.Json")]          // BCL：精确白名单
    [InlineData("netstandard")]
    public void Shared_Assemblies_Return_Null(string name)
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("test", dir);
        try
        {
            // 即使插件目录里真的有一份同名 dll，也必须返回 null：
            // 否则就会出现"插件带着自己的 Sdk.dll"，类型身份当场分裂。
            var got = alc.LoadForTesting(new AssemblyName(name));
            Assert.Null(got);
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    [Fact(DisplayName = "测试8-a-2：插件目录里放了 Sdk.dll 也不加载（不给类型分裂留任何口子）")]
    public void Shared_Assembly_Is_Not_Loaded_Even_If_Present_In_Plugin_Dir()
    {
        var dir = NewTempDir();
        var sdkPath = Path.Combine(AppContext.BaseDirectory, "StudyJourney.Sdk.dll");
        Assert.True(File.Exists(sdkPath), "测试输出目录里应有 StudyJourney.Sdk.dll");
        File.Copy(sdkPath, Path.Combine(dir, "StudyJourney.Sdk.dll"), overwrite: true);

        var alc = new PluginLoadContext("test", dir);
        try
        {
            Assert.Null(alc.LoadForTesting(new AssemblyName("StudyJourney.Sdk")));
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── 8-d：Microsoft.Data.Sqlite 不在白名单 ──

    [Fact(DisplayName = "测试8-d：Microsoft.Data.Sqlite 不在白名单，且插件目录没有 → 返回 null（交还默认 ALC）")]
    public void Sqlite_Is_Not_Whitelisted()
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("test", dir);
        try
        {
            // 现状（M0/M1 无人用 SQLite）：既不是共享程序集，插件目录里也没有 →
            // 返回 null 交给默认 ALC；默认 ALC 里也没有 → 到使用点才报"缺依赖"。
            Assert.False(PluginLoadContext.IsSharedAssembly(new AssemblyName("Microsoft.Data.Sqlite")));
            Assert.Null(alc.LoadForTesting(new AssemblyName("Microsoft.Data.Sqlite")));
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── 8-e：VerifyUnloaded() 未卸载 → false + 诊断 ──

    [Fact(DisplayName = "测试8-e：未调用 Unload 时 VerifyUnloaded() 返回 false 并给出诊断（不抛异常）")]
    public void VerifyUnloaded_Returns_False_When_Not_Unloaded()
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("test", dir);
        try
        {
            Assert.False(alc.UnloadRequested);

            var ok = alc.VerifyUnloaded();   // 不抛异常是这里的第二个断言点

            Assert.False(ok);
            Assert.Contains("尚未调用 Unload", alc.LastDiagnostic, StringComparison.Ordinal);
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    [Fact(DisplayName = "测试8-e-2：已请求卸载后，实例方法仍返回 false 并说明原因（无法观测自身）")]
    public void VerifyUnloaded_Instance_Cannot_Observe_Self_Collection()
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("test", dir);
        alc.Unload();

        Assert.True(alc.UnloadRequested);
        Assert.False(alc.VerifyUnloaded());
        Assert.Contains("无法观测自身", alc.LastDiagnostic, StringComparison.Ordinal);

        TryDelete(dir);
    }

    // ── 8-f：卸载后静态判定 → true ──

    [Fact(DisplayName = "测试8-f：ALC 已 Unload 且无强引用 → VerifyUnloaded(WeakReference) 返回 true")]
    public void Unloaded_Context_Is_Collected()
    {
        var dir = NewTempDir();
        var sdkPath = Path.Combine(AppContext.BaseDirectory, "StudyJourney.Sdk.dll");
        File.Copy(sdkPath, Path.Combine(dir, "StudyJourney.Sdk.dll"), overwrite: true);

        try
        {
            var weak = LoadAndUnload(dir);

            Assert.True(
                PluginLoadContext.VerifyUnloaded(weak, out var detail),
                $"ALC 未被回收：{detail}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// ⚠ 必须 <see cref="MethodImplOptions.NoInlining"/>：否则 JIT 可能把 <c>alc</c> 的栈槽
    /// 延长到调用方，导致 ALC 永远"还有引用"，测试变成假失败。
    /// 这里用 <c>LoadFromAssemblyPath</c> 直接装载 —— 它**绕过 <see cref="PluginLoadContext.Load"/>**，
    /// 所以装的是白名单里的 Sdk.dll 也能装进去（本测试要的正是"装进去再卸干净"）。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(string dir)
    {
        var alc = new PluginLoadContext("unload-test", dir);
        _ = alc.LoadFromAssemblyPath(Path.Combine(dir, "StudyJourney.Sdk.dll"));

        var weak = new WeakReference(alc);
        alc.Unload();
        return weak;
    }

    // ── 8-g：非白名单 + 插件目录里有 → 真的从插件目录加载 ──

    [Fact(DisplayName = "测试8-g：非白名单程序集从插件目录加载成功（Load 的主动路径）")]
    public void Non_Shared_Assembly_Loads_From_Plugin_Directory()
    {
        var dir = NewTempDir();
        const string probe = "xunit.assert";
        var src = Path.Combine(AppContext.BaseDirectory, probe + ".dll");
        Assert.True(File.Exists(src), $"测试输出目录里应有 {probe}.dll（用作非白名单探针）");
        File.Copy(src, Path.Combine(dir, probe + ".dll"), overwrite: true);

        var alc = new PluginLoadContext("test", dir);
        try
        {
            var asm = alc.LoadForTesting(new AssemblyName(probe));

            Assert.NotNull(asm);
            Assert.Equal(probe, asm!.GetName().Name);
            // 关键：它装进的是**这个** ALC，而不是默认 ALC 里那份
            Assert.Contains(asm, alc.Assemblies);
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    [Fact(DisplayName = "测试8-g-2：非白名单且插件目录没有 → 返回 null（让默认 ALC 兜底，不抛异常）")]
    public void Missing_Assembly_Returns_Null_Instead_Of_Throwing()
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("test", dir);
        try
        {
            Assert.Null(alc.LoadForTesting(new AssemblyName("Acme.NotExist.Anywhere")));
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── 8-h：白名单是精确清单，不是 System.* 前缀 ──

    [Theory(DisplayName = "测试8-h：白名单精确匹配（System.* / Microsoft.* 不做前缀放行）")]
    // 精确白名单内
    [InlineData("StudyJourney.Sdk", true)]
    [InlineData("StudyJourney.Sdk.Dto", true)]        // 显式前缀 StudyJourney.Sdk.
    [InlineData("Avalonia.Base", true)]               // 显式前缀 Avalonia.
    [InlineData("FluentAvalonia", true)]
    [InlineData("System.Private.CoreLib", true)]
    [InlineData("System.Text.Json", true)]
    [InlineData("System.Runtime.InteropServices", true)]
    [InlineData("netstandard", true)]
    // 不在白名单 —— 若实现用了 "System.*" 前缀，这几条会全部变成 true
    [InlineData("System.Memory", false)]
    [InlineData("System.Text.Json.Nodes", false)]
    [InlineData("System.Collections.Immutable", false)]
    [InlineData("Microsoft.Data.Sqlite", false)]
    [InlineData("Microsoft.Extensions.Logging", false)]
    // 前缀不能粗到误伤同名第三方包
    [InlineData("AvaloniaXYZ", false)]
    [InlineData("StudyJourney.SdkFoo", false)]
    [InlineData("StudyJourney.Core", false)]
    public void Whitelist_Is_Exact(string name, bool expected)
    {
        Assert.Equal(expected, PluginLoadContext.IsSharedAssembly(new AssemblyName(name)));
    }

    // ── 构造与属性 ──

    [Fact(DisplayName = "测试8-i：上下文可回收、名字带插件 id（调试器里能分辨）")]
    public void Context_Is_Collectible_And_Named()
    {
        var dir = NewTempDir();
        var alc = new PluginLoadContext("countdown", dir);
        try
        {
            Assert.True(alc.IsCollectible);
            Assert.Equal("Plugin:countdown", alc.Name);
            Assert.Equal("countdown", alc.PluginId);
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }
}
