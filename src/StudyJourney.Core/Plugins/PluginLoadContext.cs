using System.Reflection;
using System.Runtime.Loader;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// 每个插件一个的可回收 <see cref="AssemblyLoadContext"/>（CORE_ARCHITECTURE §0.8 / §4.6）。
///
/// **这个类的全部难点只有一个：让"共享程序集"在插件与核心之间是同一份。**
/// 一旦插件 ALC 自己加载了一份 <c>StudyJourney.Sdk</c>，<c>IScheduleProvider</c> 就变成
/// 两个不同的 <see cref="Type"/> —— 症状是"注册成功但 <c>Get&lt;T&gt;()</c> 返回 null"
/// 或 <c>InvalidCastException</c>，而报错信息完全不指向 ALC。所以：
///
/// <list type="number">
/// <item><see cref="Load"/> 对白名单**返回 <c>null</c>**（= 交还默认 ALC），不是"加载失败"；</item>
/// <item><see cref="OnResolving"/> 只做**兜底**：把共享程序集重定向到默认 ALC 已加载的那一份；</item>
/// <item>卸载后由 <see cref="VerifyUnloaded(WeakReference, out string?)"/> 判回收，**只 Warn 不阻断**。</item>
/// </list>
///
/// **白名单为什么是精确清单，而不是 <c>System.*</c> 前缀**：前缀会把核心自己没加载的
/// <c>System.Memory</c> / <c>System.Text.Json.Nodes</c> 也判成"共享"→ 交还默认 ALC → 默认 ALC 里也没有
/// → 插件当场加载失败，而报错只字不提白名单。**窄了顶多"多加载一份"（可修），宽了是"插件根本起不来"（难查）。**
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// 精确白名单：这些程序集**永远**由默认 ALC 提供（§0.8 第 1 条）。
    /// 只收"核心或 App 一定会加载、且插件必须与之共享类型身份"的程序集。
    /// </summary>
    private static readonly HashSet<string> SharedExactNames = new(StringComparer.Ordinal)
    {
        // 契约层：类型身份一致的唯一来源
        "StudyJourney.Sdk",

        // 运行时核心（插件里任何类型都间接依赖它）
        "System.Private.CoreLib",
        "netstandard",

        // 插件签名与 DTO 直接用到的 BCL 面
        "System.Runtime",
        "System.Collections",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Linq",
        "System.Text.Json",
        "System.Runtime.InteropServices",

        // UI 框架（裸名，其余走前缀）
        "Avalonia",
        "FluentAvalonia",
    };

    /// <summary>
    /// 显式命名空间前缀（**只有三个**）。除此之外一律精确比对 —— 见类注释。
    /// 带尾点，避免 <c>Avalonia</c> 误配到"名字以 Avalonia 开头的第三方包"（如 <c>AvaloniaXYZ</c>）。
    /// </summary>
    private static readonly string[] SharedNamePrefixes =
    [
        "StudyJourney.Sdk.",
        "Avalonia.",
        "FluentAvalonia.",
    ];

    private readonly string _pluginDirectory;
    private readonly WeakReference _self;
    private bool _unloadRequested;

    /// <summary>最近一次诊断信息（<see cref="VerifyUnloaded()"/> 的结论说明）。仅用于测试与日志。</summary>
    public string? LastDiagnostic { get; private set; }

    /// <summary>本上下文对应的插件 id（= <c>plugin.json</c> 的 <c>id</c>，也用于 ALC 名字）。</summary>
    public string PluginId { get; }

    /// <summary><c>Unload()</c> 是否已被请求。**只读 <c>Unloading</c> 事件，不重写 <c>Unload</c>**（§0.8 第 12 条）。</summary>
    public bool UnloadRequested => _unloadRequested;

    /// <param name="pluginId">插件 id（用于 ALC 名字，便于调试器里分辨）。</param>
    /// <param name="pluginDirectory">插件目录；非白名单程序集从这里加载。</param>
    public PluginLoadContext(string pluginId, string pluginDirectory)
        : base($"Plugin:{pluginId}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        PluginId = pluginId;
        _pluginDirectory = Path.GetFullPath(pluginDirectory);
        _self = new WeakReference(this);

        // 兜底通道：Load 返回 null 后，若默认 ALC 也解析不到，运行时会问这里。
        Resolving += OnResolving;

        // 不重写 Unload（§0.8 第 12 条）——只订阅 Unloading 记下"已请求卸载"。
        // 注意签名：Unloading 是 Action<AssemblyLoadContext>（单参），Resolving 才是双参 Func。
        Unloading += _ => _unloadRequested = true;
    }

    /// <summary>
    /// 判断某个程序集是否属于"必须与核心共享类型身份"的集合。
    ///
    /// <c>internal</c> 供测试直接断言白名单边界（精确匹配 vs 前缀），不必构造真实 dll。
    /// </summary>
    internal static bool IsSharedAssembly(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
            return false;

        if (SharedExactNames.Contains(name))
            return true;

        foreach (var prefix in SharedNamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 解析插件自己的程序集。
    ///
    /// **绝不抛异常**：返回 <c>null</c> 的语义是"交还默认 ALC 继续找"，不是"加载失败"（§0.8 第 1 条）。
    /// 这里抛异常会让"某个可选依赖缺失"升级成"整个插件起不来"。
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsSharedAssembly(assemblyName))
            return null;

        if (assemblyName.Name is null)
            return null;

        var path = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        if (!File.Exists(path))
            return null;   // 交还默认 ALC 兜底；真没有时由调用点报"缺依赖"

        try
        {
            return LoadFromAssemblyPath(path);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException)
        {
            // 文件坏了/被锁：同样不抛，交还默认 ALC。加载失败的真实原因由上层根据"找不到类型"报出。
            LastDiagnostic = $"加载 {path} 失败：{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 兜底解析：**只处理白名单**，把请求重定向到默认 ALC 已加载的那一份。
    ///
    /// ⚠ **绝不能在这里加载插件自己的程序集**（§0.8 第 10 条）：那会让"Load 里漏了一条路径"变成
    /// "偶尔某一版能跑"，问题从确定性故障退化成随机故障。
    /// </summary>
    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (!IsSharedAssembly(assemblyName))
            return null;

        var name = assemblyName.Name;
        if (name is null)
            return null;

        foreach (var loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.Ordinal))
                return loaded;
        }

        return null;
    }

    /// <summary>
    /// 实例版自检：**只能回答"是否还没请求卸载"**。
    ///
    /// ⚠ 为什么它无法返回 <c>true</c>：调用它就意味着 <c>this</c> 在栈上被强引用，
    /// 因此本 ALC 一定还没被回收。**真正的"已回收"判定必须用
    /// <see cref="VerifyUnloaded(WeakReference, out string?)"/>**（卸载前先抓住弱引用，丢掉强引用后再判）。
    /// 保留实例版是为了让"忘了调 <c>Unload()</c>"这一常见错误在调用点就能被看见。
    /// </summary>
    public bool VerifyUnloaded()
    {
        if (!_unloadRequested)
        {
            LastDiagnostic = $"插件 {PluginId} 尚未调用 Unload()，无法判定回收";
            Warn(LastDiagnostic);
            return false;
        }

        LastDiagnostic =
            $"插件 {PluginId} 已请求卸载，但实例方法无法观测自身回收（栈上仍持有 this）；" +
            "请改用 VerifyUnloaded(WeakReference, out string?)";
        Warn(LastDiagnostic);
        return false;
    }

    /// <summary>
    /// 静态判定：**这是唯一能真正回答"ALC 是否已被回收"的入口**（§0.8 第 3 条）。
    ///
    /// 用法：卸载前 <c>var w = new WeakReference(alc);</c> → <c>alc.Unload();</c> → 丢掉强引用 →
    /// 调本方法。<c>GC.Collect</c> 两次 + 等终结器，是因为 collectible ALC 的卸载是分两轮的
    /// （第二轮才真正释放），只收一次常常仍 <c>IsAlive == true</c>。
    /// </summary>
    /// <param name="contextRef">卸载前创建的、指向该 ALC 的弱引用。</param>
    /// <param name="detail">回收失败时给出人话原因；成功时为 <c>null</c>。</param>
    public static bool VerifyUnloaded(WeakReference contextRef, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(contextRef);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (contextRef.IsAlive)
        {
            detail = "ALC 仍被引用未回收：常见原因是插件自己 new 的窗口/静态事件/线程还持有引用（§3.4）";
            Warn(detail);
            return false;
        }

        detail = null;
        return true;
    }

    /// <summary>
    /// 仅供测试调用 <see cref="Load"/>（它是 <c>protected override</c>）。
    /// 生产代码不要用 —— 加载路径唯一入口是运行时与本类的 <see cref="Load"/>。
    /// </summary>
    internal Assembly? LoadForTesting(AssemblyName assemblyName) => Load(assemblyName);

    private static void Warn(string message) => System.Diagnostics.Debug.WriteLine($"[Warn] {message}");
}
