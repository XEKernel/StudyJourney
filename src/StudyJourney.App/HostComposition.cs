using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Notifications;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Core.Shell;
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

    /// <summary>壳（注册表 + 失败语义 + 宿主转发）。插件拿到的是它 <c>ForPlugin</c> 出来的 scoped 实例。</summary>
    public required ShellService Shell { get; init; }

    public required NotificationService Notifications { get; init; }

    public required PluginLoader Loader { get; init; }

    public required string PluginsDirectory { get; init; }

    public required string DataDirectory { get; init; }

    /// <summary>
    /// 正常退出，顺序不能颠倒：
    /// ① 先停插件（卸载会摘掉菜单/槽位，此时宿主还在，界面能跟着更新）；
    /// ② 再解绑热键、关通知（避免残留在系统里）；
    /// ③ 最后由 UI 层销毁托盘。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Loader.DisposeAsync().ConfigureAwait(false);
        await Notifications.WithdrawAllAsync().ConfigureAwait(false);
        Shell.Dispose();
    }
}

/// <summary>
/// 组合根（CORE_ARCHITECTURE §3.1 第 2–7 步）。
///
/// 装配顺序不能调：先有日志/配置，再有注册表/总线，最后才是加载器；
/// 核心服务（<c>IServiceRegistry</c> / <c>IEventBus</c> / <c>IShellService</c> / <c>INotificationService</c>）
/// 必须在**任何插件 Initialize 之前**注册完。
///
/// M1-A 仍未装配：主题服务（明暗切换）、设置窗、更新编排（M5）。
/// </summary>
public static class HostComposition
{
    /// <summary>装配核心对象。**不加载插件**（加载是 <see cref="PluginLoader.DiscoverAndLoadAsync"/> 的事）。</summary>
    /// <param name="pluginsDirectory">插件根目录（通常是 exe 旁边的 plugins/）。</param>
    /// <param name="dataDirectory">业务数据根目录（通常是 exe 旁边的 data/）。</param>
    /// <param name="configFilePath">settings.json 路径；<c>null</c> 或文件不存在 → 纯内存空配置。</param>
    /// <param name="logSink">日志输出目标；<c>null</c> = 控制台。测试注入收集器。</param>
    /// <param name="hotKeyBinder">热键绑定层；<c>null</c> = 真实 Win32 实现。测试注入替身。</param>
    public static HostServices Build(
        string pluginsDirectory,
        string dataDirectory,
        string? configFilePath = null,
        Action<string>? logSink = null,
        IHotKeyBinder? hotKeyBinder = null)
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

        // 第 5 步：壳与通知（两者都"宿主可以晚到"：注册照收，Attach 时重放）
        var shell = new ShellService(hotKeyBinder ?? new Win32HotKeyBinder(), logger);
        var notifications = new NotificationService(logger);

        // ⚠ 必须在任何插件 Initialize 之前注册：它们都是核心保留能力，
        // 插件若抢先注册会拿到 reserved-service 或 service-conflict（都能在加载报告里看到）。
        registry.Register<IShellService>(shell.ForPlugin(ServiceRegistry.CoreOwner));
        registry.Register<INotificationService>(notifications);

        // 第 7 步：加载器
        var loader = new PluginLoader(registry, bus, config, shell, logger);

        return new HostServices
        {
            Logger = logger,
            Config = config,
            Registry = registry,
            Bus = bus,
            Shell = shell,
            Notifications = notifications,
            Loader = loader,
            PluginsDirectory = Path.GetFullPath(pluginsDirectory),
            DataDirectory = Path.GetFullPath(dataDirectory),
        };
    }
}
