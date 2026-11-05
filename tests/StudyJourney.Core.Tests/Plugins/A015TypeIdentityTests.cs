using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using StudyJourney.Core.Plugins;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;
using Xunit.Abstractions;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// **A0-15 验收（M0 的两条否决项之一，CORE_ARCHITECTURE §0.8 / §9.1）**。
///
/// 守的是一个"写错了不报错"的故障：插件 ALC 若自己加载了一份 <c>StudyJourney.Sdk</c> 或
/// <c>Avalonia</c>，同一个接口就变成两个不同的 <see cref="Type"/>。症状是
/// 「注册成功但 <c>Get&lt;T&gt;()</c> 返回 null」或 <c>InvalidCastException</c>，
/// 而报错信息完全不指向 ALC——那类 bug 会在 M3 同时有 4 个插件时才炸，返工面是全量的。
///
/// **不通过 → M0 不算完成，不得进 M1。**
///
/// 三条测试共用一个真实插件（<c>tests/fixtures/A0_15_Probe</c>）：
/// <list type="bullet">
/// <item>A0-15-a：插件注册的 <see cref="IScheduleProvider"/> 与核心侧是同一个 <c>Type</c>；</item>
/// <item>A0-15-b：插件看到的 <c>TextBlock</c> 与核心侧是同一个 <c>Type</c>（同一个 dll 文件）；</item>
/// <item>A0-15-c：用完 Unload 之后能被真正回收。</item>
/// </list>
/// </summary>
public sealed class A015TypeIdentityTests
{
    private const string ProbeAssemblyFile = "A0_15_Probe.dll";
    private const string ProbePluginTypeName = "StudyJourney.Plugin.A015Probe.ProbePlugin";

    private readonly ITestOutputHelper _out;

    public A015TypeIdentityTests(ITestOutputHelper output) => _out = output;

    // ── 夹具 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 造一个**像真的**插件目录：里面有插件本体，也有它自己的 <c>StudyJourney.Sdk.dll</c>
    /// （真实插件目录一定会有这份——ProjectReference 会复制；所以"即使有也不许加载"才是真考验）。
    /// </summary>
    private static string CreatePluginDirectory(bool includeAvaloniaCopy)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sj-a015-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var baseDir = AppContext.BaseDirectory;
        File.Copy(Path.Combine(baseDir, ProbeAssemblyFile), Path.Combine(dir, ProbeAssemblyFile), overwrite: true);

        var sdk = Path.Combine(baseDir, "StudyJourney.Sdk.dll");
        Assert.True(File.Exists(sdk), "测试输出目录里应有 StudyJourney.Sdk.dll");
        File.Copy(sdk, Path.Combine(dir, "StudyJourney.Sdk.dll"), overwrite: true);

        if (includeAvaloniaCopy)
        {
            // 故意把 Avalonia 也放进插件目录：白名单若失效，Load 会把这份装进插件 ALC，
            // 于是插件侧 TextBlock 的 Assembly.Location 指向临时目录 → A0-15-b 立刻失败。
            foreach (var name in new[] { "Avalonia.dll", "Avalonia.Base.dll", "Avalonia.Controls.dll" })
            {
                var src = Path.Combine(baseDir, name);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(dir, name), overwrite: true);
            }
        }

        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* 加载过的 dll 可能仍被占用；留给系统清理 */ }
    }

    /// <summary>在探针程序集里找**唯一**的 <see cref="IPlugin"/> 实现，并把"为什么找不到"说清楚。</summary>
    private static Type FindPluginType(Assembly probe)
    {
        var all = probe.GetTypes();
        var impls = all
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(IPlugin).IsAssignableFrom(t))
            .ToArray();

        if (impls.Length == 1)
            return impls[0];

        // 这条失败信息本身就是诊断：若 Sdk 被加载了两份，impls 会是 0 个。
        throw new Xunit.Sdk.XunitException(
            $"探针程序集里应恰好有 1 个 IPlugin 实现，实际 {impls.Length} 个。\n" +
            $"  核心侧 typeof(IPlugin).AssemblyQualifiedName：{typeof(IPlugin).AssemblyQualifiedName}\n" +
            $"  探针程序集里的类型：{string.Join(", ", all.Select(t => t.FullName))}\n" +
            "  若为 0 个 → 极可能是 StudyJourney.Sdk 被加载了两份（类型身份分裂，A0-15 否决项）。");
    }

    // ── A0-15-a ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-15-a：插件注册的 IScheduleProvider 与核心侧是同一个 Type")]
    public async Task ScheduleProvider_Type_Identity_Is_Shared()
    {
        var dir = CreatePluginDirectory(includeAvaloniaCopy: false);
        var alc = new PluginLoadContext("a015-probe", dir);

        try
        {
            var probe = alc.LoadFromAssemblyPath(Path.Combine(dir, ProbeAssemblyFile));
            var pluginType = FindPluginType(probe);
            var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;

            Assert.Equal("a015-probe", plugin.Id);

            var registry = new TestServiceRegistry();
            var init = await plugin.InitializeAsync(new TestPluginContext(registry));
            Assert.True(init.IsOk, $"插件 Initialize 失败：{init.Code} / {init.Message}");

            var provider = registry.Get<IScheduleProvider>();
            Assert.NotNull(provider);

            var pluginSideInterface = provider!.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == nameof(IScheduleProvider));

            // 证据（每次运行都打印，不只失败时）：
            _out.WriteLine("[A0-15-a] 插件侧 IScheduleProvider AQN : " + (pluginSideInterface?.AssemblyQualifiedName ?? "(未实现)"));
            _out.WriteLine("[A0-15-a] 核心侧 IScheduleProvider AQN : " + typeof(IScheduleProvider).AssemblyQualifiedName);
            _out.WriteLine("[A0-15-a] 插件侧 Assembly.Location      : " + (pluginSideInterface?.Assembly.Location ?? "(n/a)"));
            _out.WriteLine("[A0-15-a] 核心侧 Assembly.Location      : " + typeof(IScheduleProvider).Assembly.Location);
            _out.WriteLine("[A0-15-a] ReferenceEquals               : " + ReferenceEquals(pluginSideInterface, typeof(IScheduleProvider)));
            _out.WriteLine("[A0-15-a] 插件 ALC 已加载程序集         : " + string.Join(", ", alc.Assemblies.Select(a => a.GetName().Name)));

            Assert.True(
                ReferenceEquals(pluginSideInterface, typeof(IScheduleProvider)),
                "**A0-15 否决项失败：IScheduleProvider 类型身份分裂。**\n" +
                $"  插件侧 Type.AssemblyQualifiedName：{pluginSideInterface?.AssemblyQualifiedName ?? "(未实现该接口)"}\n" +
                $"  核心侧 Type.AssemblyQualifiedName：{typeof(IScheduleProvider).AssemblyQualifiedName}\n" +
                $"  插件侧 Assembly.Location：{pluginSideInterface?.Assembly.Location ?? "(n/a)"}\n" +
                $"  核心侧 Assembly.Location：{typeof(IScheduleProvider).Assembly.Location}\n" +
                $"  插件 ALC 已加载程序集：{string.Join(", ", alc.Assemblies.Select(a => a.GetName().Name))}");
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── A0-15-b ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-15-b：插件看到的 TextBlock 与核心侧同一个 Type（同一 dll）— Resolving/白名单路径")]
    public void Avalonia_Type_Identity_Is_Shared()
    {
        // 关键：插件目录里**放了一份 Avalonia**。白名单失效就会被装进插件 ALC。
        var dir = CreatePluginDirectory(includeAvaloniaCopy: true);

        // 先在核心侧碰一次：确保默认 ALC 已持有 Avalonia.Controls，这样"第二份"才无处可藏。
        var coreTextBlock = typeof(Avalonia.Controls.TextBlock);
        Assert.NotEmpty(coreTextBlock.Assembly.Location);

        var alc = new PluginLoadContext("a015-probe", dir);

        try
        {
            var probe = alc.LoadFromAssemblyPath(Path.Combine(dir, ProbeAssemblyFile));
            var pluginType = probe.GetType(ProbePluginTypeName, throwOnError: true)!;

            var method = pluginType.GetMethod("TextBlockType", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            var pluginSide = (Type)method!.Invoke(null, null)!;

            // 证据：两个 Type 的 Assembly.Location 是否指向同一个 dll 文件
            _out.WriteLine("[A0-15-b] 插件侧 TextBlock AQN      : " + pluginSide.AssemblyQualifiedName);
            _out.WriteLine("[A0-15-b] 核心侧 TextBlock AQN      : " + coreTextBlock.AssemblyQualifiedName);
            _out.WriteLine("[A0-15-b] 插件侧 Assembly.Location  : " + pluginSide.Assembly.Location);
            _out.WriteLine("[A0-15-b] 核心侧 Assembly.Location  : " + coreTextBlock.Assembly.Location);
            _out.WriteLine("[A0-15-b] ReferenceEquals          : " + ReferenceEquals(pluginSide, coreTextBlock));
            _out.WriteLine("[A0-15-b] 插件目录里是否有 Avalonia : " + File.Exists(Path.Combine(dir, "Avalonia.Controls.dll")));
            _out.WriteLine("[A0-15-b] 插件 ALC 已加载程序集     : " + string.Join(", ", alc.Assemblies.Select(a => a.GetName().Name)));

            Assert.True(
                ReferenceEquals(pluginSide, coreTextBlock),
                "**A0-15 否决项失败：Avalonia.Controls.TextBlock 类型身份分裂。**\n" +
                $"  插件侧 Assembly.Location：{pluginSide.Assembly.Location}\n" +
                $"  核心侧 Assembly.Location：{coreTextBlock.Assembly.Location}\n" +
                $"  插件侧 AQN：{pluginSide.AssemblyQualifiedName}\n" +
                $"  核心侧 AQN：{coreTextBlock.AssemblyQualifiedName}\n" +
                (pluginSide.Assembly.Location == coreTextBlock.Assembly.Location
                    ? "  (Location 相同但 Type 不同 —— 说明同一个文件被装进了两个 ALC)"
                    : "  (Location 不同 —— 说明插件自己加载了插件目录里的那份 Avalonia)") +
                $"\n  插件 ALC 已加载程序集：{string.Join(", ", alc.Assemblies.Select(a => a.GetName().Name))}");

            // 同一份文件的更强断言：连磁盘路径都应当一模一样
            Assert.Equal(coreTextBlock.Assembly.Location, pluginSide.Assembly.Location);
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── A0-15-b-2 ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-15-b-2：Resolving 兜底只认白名单，绝不为插件自己的程序集兜底")]
    public void Resolving_Backstop_Only_Serves_Whitelist()
    {
        // ⚠ 为什么需要这条：白名单正确时 `Load` 返回 null 后默认 ALC 就解析成功了，
        // Resolving 那条兜底**根本不会被触发**（A0-15-b 走的是"白名单 + 默认 ALC"路径）。
        // 一个永不执行的兜底等于没写，所以这里直接调它。
        var dir = CreatePluginDirectory(includeAvaloniaCopy: false);
        var alc = new PluginLoadContext("a015-probe", dir);

        try
        {
            var onResolving = typeof(PluginLoadContext).GetMethod(
                "OnResolving", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(onResolving);

            Assembly? Invoke(AssemblyName name) =>
                (Assembly?)onResolving!.Invoke(alc, [AssemblyLoadContext.Default, name]);

            // ① 白名单内 + 默认 ALC 持有 → 返回默认那份（同一实例，不是又加载一份）
            var sdkBackstop = Invoke(new AssemblyName("StudyJourney.Sdk"));
            Assert.Same(typeof(IPlugin).Assembly, sdkBackstop);

            // ② 非白名单，但默认 ALC **确实持有**（xunit.assert 就在默认 ALC 里）
            //    → 必须返回 null。若这里返回了那份，等于"插件自己的依赖被静默重定向到核心的副本"，
            //      这是 Layout / 版本不匹配类故障的温床。
            var xunitName = typeof(Xunit.Assert).Assembly.GetName();
            Assert.False(PluginLoadContext.IsSharedAssembly(xunitName));
            Assert.Null(Invoke(xunitName));

            // ③ 白名单内但默认 ALC 没有（没加载过）→ null（交还默认 ALC 去自己加载）
            Assert.Null(Invoke(new AssemblyName("FluentAvalonia")));

            _out.WriteLine("[A0-15-b-2] StudyJourney.Sdk 兜底返回默认那份 : " + (sdkBackstop is not null));
            _out.WriteLine("[A0-15-b-2] 非白名单 " + xunitName.Name + " 兜底返回 : null（正确）");
            _out.WriteLine("[A0-15-b-2] 白名单但未加载 FluentAvalonia 返 : null（交还默认 ALC）");
        }
        finally
        {
            alc.Unload();
            TryDelete(dir);
        }
    }

    // ── A0-15-c ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-15-c：加载→取服务→Unload 后，ALC 能被真正回收")]
    public void Unload_After_Using_Probe_Collects_Cleanly()
    {
        var dir = CreatePluginDirectory(includeAvaloniaCopy: true);

        try
        {
            var weak = LoadUseAndUnload(dir);

            var collected = PluginLoadContext.VerifyUnloaded(weak, out var detail);
            _out.WriteLine("[A0-15-c] WeakReference.IsAlive（GC 后）: " + weak.IsAlive);
            _out.WriteLine("[A0-15-c] VerifyUnloaded 返回          : " + collected);
            _out.WriteLine("[A0-15-c] detail                       : " + (detail ?? "(null = 已回收)"));

            Assert.True(
                collected,
                "**A0-15 否决项失败：ALC 卸载后未被回收。**\n" + $"  原因：{detail}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// 加载 → 注册服务 → 取服务 → 卸载，全程只返回弱引用。
    ///
    /// ⚠ <see cref="MethodImplOptions.NoInlining"/> 必需：否则 JIT 可能把 <c>alc</c> 的栈槽
    /// 延长到调用方，ALC 永远"还有引用" → 测试变成恒假失败。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadUseAndUnload(string dir)
    {
        var alc = new PluginLoadContext("a015-probe", dir);
        var probe = alc.LoadFromAssemblyPath(Path.Combine(dir, ProbeAssemblyFile));
        var pluginType = probe.GetType(ProbePluginTypeName, throwOnError: true)!;
        var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;

        var registry = new TestServiceRegistry();
        // 探针的 Initialize 是同步完成的（Task.FromResult），同步等待不会死锁。
        var init = plugin.InitializeAsync(new TestPluginContext(registry)).GetAwaiter().GetResult();
        _ = registry.Get<IScheduleProvider>();

        var weak = new WeakReference(alc);
        alc.Unload();
        _ = init;

        return weak;
    }
}
