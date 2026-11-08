using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;

namespace StudyJourney.App;

/// <summary>
/// 组合根装配出的整套核心对象（宿主层持有，生命周期 = 进程）。
/// </summary>
public sealed class HostServices : IAsyncDisposable
{
    public required AppLogger Logger { get; init; }

    public required ConfigStore Config { get; init; }

    public required ServiceRegistry Registry { get; init; }

    public required EventBus Bus { get; init; }

    public required PluginLoader Loader { get; init; }

    public required string PluginsDirectory { get; init; }

    public required string DataDirectory { get; init; }

    /// <summary>正常退出：逆拓扑序 Stop + Unload（<see cref="PluginLoader.DisposeAsync"/> 内含）。</summary>
    public ValueTask DisposeAsync() => Loader.DisposeAsync();
}

/// <summary>
/// 组合根（CORE_ARCHITECTURE §3.1 第 2–7 步的 M0 子集）。
///
/// 装配顺序不能调：先有日志/配置，再有注册表/总线，最后才是加载器；
/// 核心服务（<c>IServiceRegistry</c> / <c>IEventBus</c>）必须在**任何插件 Initialize 之前**注册完。
///
/// M0 刻意**不装配**：<c>IShellService</c>（空白壳没有菜单/托盘/槽位，M1）、
/// <c>INotificationService</c>（M1）、主题服务（M1）。插件若调 <c>ctx.GetUI()</c> 会明确抛错，
/// 而不是拿到一个"看起来能用"的假壳。
/// </summary>
public static class HostComposition
{
    /// <summary>装配核心对象。**不加载插件**（加载是 <see cref="PluginLoader.DiscoverAndLoadAsync"/> 的事）。</summary>
    /// <param name="pluginsDirectory">插件根目录（通常是 exe 旁边的 plugins/）。</param>
    /// <param name="dataDirectory">业务数据根目录（通常是 exe 旁边的 data/）。</param>
    /// <param name="configFilePath">settings.json 路径；<c>null</c> 或文件不存在 → 纯内存空配置。</param>
    /// <param name="logSink">日志输出目标；<c>null</c> = 控制台。测试注入收集器。</param>
    public static HostServices Build(
        string pluginsDirectory,
        string dataDirectory,
        string? configFilePath = null,
        Action<string>? logSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var logger = new AppLogger(logSink);

        // 第 2 步：配置（读不出来就用空树，绝不因此起不来，§5.7）
        var config = configFilePath is not null && File.Exists(configFilePath)
            ? ConfigStore.Load(configFilePath, logger)
            : ConfigStore.CreateInMemory(logger: logger);

        // 第 4 步：核心服务（注册表构造时已自注册 IServiceRegistry）
        var registry = new ServiceRegistry(logger);
        var bus = new EventBus(logger);
        registry.Register<IEventBus>(bus);

        // 第 7 步：加载器
        var loader = new PluginLoader(registry, bus, config, logger);

        return new HostServices
        {
            Logger = logger,
            Config = config,
            Registry = registry,
            Bus = bus,
            Loader = loader,
            PluginsDirectory = Path.GetFullPath(pluginsDirectory),
            DataDirectory = Path.GetFullPath(dataDirectory),
        };
    }
}
