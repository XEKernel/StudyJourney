using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// 插件加载器（编排器）—— CORE_ARCHITECTURE §3.1 第 8–13 步：
/// 扫描/校验（<see cref="PluginCatalog"/>）→ 程序集加载（<see cref="PluginAssemblyHost"/>）→
/// InitializeAsync → Seal() → StartAsync；退出时逆拓扑序 Stop + Unload。
///
/// **全部失败路径都不抛**（除参数为空）：一个坏插件不能阻止主窗启动（§3.2 E19）。
/// </summary>
public sealed class PluginLoader : IAsyncDisposable
{
    /// <summary>StartAsync 超时（§3.2 E18）。超时**不卸载 ALC**——它可能还在跑。</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    /// <summary>StopAsync 超时（§3.4）。超时直接跳过，退出总预算 3 秒（§3.5）。</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly ServiceRegistry _registry;
    private readonly IEventBus _bus;
    private readonly ConfigStore _config;
    private readonly AppLogger _logger;
    private readonly List<DiscoveredPlugin> _plugins = [];
    private readonly List<(string Id, WeakReference Weak)> _pendingUnloadChecks = [];

    public PluginLoader(ServiceRegistry registry, IEventBus bus, ConfigStore config, AppLogger logger)
    {
        _registry = registry;
        _bus = bus;
        _config = config;
        _logger = logger;
    }

    /// <summary>本次加载的全部结论（按拓扑序）。</summary>
    public IReadOnlyList<PluginLoadResult> Results => _plugins.Select(p => p.ToResult()).ToList();

    /// <summary>
    /// 扫描并加载。返回报告，**不抛异常**（坏插件只影响它自己）。
    /// </summary>
    /// <param name="pluginsDirectory">插件根目录（plugins/）。不存在时不创建（§3.7）。</param>
    /// <param name="dataDirectory">业务数据根目录（data/）。</param>
    public async Task<PluginLoadReport> DiscoverAndLoadAsync(
        string pluginsDirectory,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            // §3.7：不自动创建 plugins/ ——「没装插件」必须和「插件丢了」能区分开。
            _logger.Warn($"未找到插件目录 {pluginsDirectory}，按「没有可用插件」启动（不自动创建）");
            return new PluginLoadReport([]);
        }

        _plugins.AddRange(PluginCatalog.Discover(pluginsDirectory, _logger));

        foreach (var slot in _plugins.Where(p => p.State == PluginState.Sorted))
            PluginAssemblyHost.Load(slot, _logger);

        await InitializeAllAsync(pluginsDirectory, dataDirectory, cancellationToken).ConfigureAwait(false);

        _registry.Seal();   // §3.1 第 12 步：任何 Start 之前封板

        await StartAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var slot in _plugins)
        {
            var result = slot.ToResult();
            if (result.IsRunning)
                _logger.Info($"插件 {result.PluginId} 已启动（版本 {slot.Manifest?.Version}）");
            else if (result.Code is not null)
                _logger.Error($"插件 {result.PluginId} 未能启动：[{result.Code}] {result.Message}");
        }

        return new PluginLoadReport(Results);
    }

    /// <summary>逆拓扑序停止并卸载全部插件（§3.5 退出时序）。单个插件失败不阻断其它插件。</summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _pendingUnloadChecks.Clear();

        for (var i = _plugins.Count - 1; i >= 0; i--)
        {
            var slot = _plugins[i];

            if (slot.Plugin is not null
                && slot.State is PluginState.Initialized or PluginState.Started or PluginState.StartFailed)
            {
                try
                {
                    await slot.Plugin.StopAsync(cancellationToken)
                        .WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 卸载是尽力而为：插件停不下来也必须继续（否则程序关不掉，§3.5）。
                    _logger.Warn($"插件 {slot.RuntimeId} StopAsync 失败/超时，已跳过：{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (slot.Context is not null)
            {
                _bus.DisposeOwner(slot.Context.EventOwner);   // 保险：插件没释放订阅也不会回调进已卸载 ALC
                _registry.RemoveAllOf(slot.RuntimeId);
            }

            var weak = PluginAssemblyHost.Unload(slot);
            slot.State = PluginState.Unloaded;
            if (weak is not null)
                _pendingUnloadChecks.Add((slot.RuntimeId, weak));
        }

        // ⚠ 回收判定必须换一个**全新的栈**（§7 陷阱 11/12）：本方法的异步状态机会残留强引用，
        // 在同一帧里 GC 会让 ALC 恒 IsAlive → 恒假 Warn。判定失败只 Warn、不阻断退出（§3.4）。
        if (_pendingUnloadChecks.Count > 0)
        {
            var pending = _pendingUnloadChecks.ToList();
            await Task.Run(() =>
            {
                foreach (var (id, weak) in pending)
                    PluginAssemblyHost.VerifyCollected(id, weak, _logger);
            }, CancellationToken.None).ConfigureAwait(false);
            _pendingUnloadChecks.Clear();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    // ── 第 11 步：InitializeAsync（只注册，不开工）──────────────────────

    private async Task InitializeAllAsync(string pluginsDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        foreach (var slot in _plugins.Where(p => p.State == PluginState.Loaded))
        {
            var manifest = slot.Manifest!;
            var context = new PluginContext(
                manifest.Id,
                _registry,
                _bus,
                _config.CreateHandle(manifest.Id, manifest.ConfigFields),
                new PluginStorage(manifest.Id, pluginsDirectory, dataDirectory),
                _logger.ForPlugin(manifest.Id));
            slot.Context = context;

            try
            {
                var result = await slot.Plugin!.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                if (result.IsOk)
                {
                    slot.State = PluginState.Initialized;
                    continue;
                }

                slot.Fail(PluginState.InitFailed, result.Code, result.Message);
            }
            catch (Exception ex)
            {
                slot.Fail(PluginState.InitFailed, ErrorCodes.PluginThrew,
                    $"InitializeAsync 抛异常：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {manifest.Id} 初始化抛异常", ex);
            }

            // 失败回滚：该插件注册的服务与订阅一律清掉（§3.1 11.2 的「loader 统一清」）。
            _bus.DisposeOwner(context.EventOwner);
            _registry.RemoveAllOf(manifest.Id);
        }
    }

    // ── 第 13 步：StartAsync（超时 10 秒；失败回滚）──────────────────────

    private async Task StartAllAsync(CancellationToken cancellationToken)
    {
        foreach (var slot in _plugins.Where(p => p.State == PluginState.Initialized))
        {
            try
            {
                var result = await slot.Plugin!.StartAsync(cancellationToken)
                    .WaitAsync(StartTimeout, cancellationToken).ConfigureAwait(false);

                if (result.IsOk)
                {
                    slot.State = PluginState.Started;
                    continue;
                }

                slot.Fail(PluginState.StartFailed, result.Code, result.Message);
            }
            catch (TimeoutException)
            {
                // E18：超时说明它还活着 —— **不卸载 ALC**（避免正在跑的代码访问已释放元数据）。
                slot.Fail(PluginState.StartFailed, ErrorCodes.PluginLoadFailed,
                    $"StartAsync 超过 {StartTimeout.TotalSeconds:0} 秒未返回（不卸载，下次启动再试）");
                _logger.Warn($"插件 {slot.RuntimeId} 启动超时（{StartTimeout.TotalSeconds:0} 秒），已标记失败但不卸载");
                continue;
            }
            catch (Exception ex)
            {
                slot.Fail(PluginState.StartFailed, ErrorCodes.PluginThrew,
                    $"StartAsync 抛异常：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {slot.RuntimeId} 启动抛异常", ex);
            }

            // 启动失败回滚：调 StopAsync（尽力而为）+ 清理注册项。
            try
            {
                await slot.Plugin!.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"插件 {slot.RuntimeId} 启动失败回滚时 StopAsync 又抛异常：{ex.Message}");
            }

            if (slot.Context is not null)
            {
                _bus.DisposeOwner(slot.Context.EventOwner);
                _registry.RemoveAllOf(slot.RuntimeId);
            }
        }
    }
}
