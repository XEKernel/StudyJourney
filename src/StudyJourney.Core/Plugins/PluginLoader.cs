using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>插件状态机（CORE_ARCHITECTURE §3.0）。失败刻意分成四种：恢复动作完全不同。</summary>
public enum PluginState
{
    /// <summary>扫到了 manifest，正在解析。</summary>
    Discovered,

    /// <summary>manifest 缺失/非法/id 与目录名不符/字段冲突/重复 id（终态）。</summary>
    ManifestInvalid,

    /// <summary>依赖已满足、拓扑序已定。</summary>
    Sorted,

    /// <summary>ALC 加载失败、找不到唯一 IPlugin、coreApi 不匹配、entry 缺失（终态）。</summary>
    LoadFailed,

    /// <summary>程序集已加载、实例已创建。</summary>
    Loaded,

    /// <summary>InitializeAsync 返回失败或抛异常（终态，已注册项已回滚）。</summary>
    InitFailed,

    /// <summary>注册完成。</summary>
    Initialized,

    /// <summary>StartAsync 失败（已回滚）。</summary>
    StartFailed,

    /// <summary>运行中。</summary>
    Started,

    /// <summary>已停止。</summary>
    Stopped,

    /// <summary>ALC 已卸载（或卸载失败，只记 Warn）。</summary>
    Unloaded,
}

/// <summary>单个插件的加载结论（设置页「插件」列表与 --smoke 输出都用它）。</summary>
/// <param name="PluginId">manifest 的 id（manifest 都读不出来时用目录名）。</param>
/// <param name="State">当前状态。</param>
/// <param name="Code">失败时的错误码（<see cref="ErrorCodes"/>）；成功为 <c>null</c>。</param>
/// <param name="Message">失败原因（中文，可直接给用户看）。</param>
public sealed record PluginLoadResult(string PluginId, PluginState State, string? Code = null, string? Message = null)
{
    /// <summary>是否正在运行。</summary>
    public bool IsRunning => State == PluginState.Started;
}

/// <summary>一次完整加载的结果快照。</summary>
/// <param name="Plugins">按加载顺序（拓扑序）排列。</param>
public sealed record PluginLoadReport(IReadOnlyList<PluginLoadResult> Plugins)
{
    /// <summary>成功进入 Started 的插件数。</summary>
    public int RunningCount => Plugins.Count(p => p.IsRunning);
}

/// <summary>
/// 插件加载器（编排器）—— CORE_ARCHITECTURE §3.1 第 8–13 步的整条链：
/// 扫 plugins/*/plugin.json → 校验 manifest → 全局 configFields 冲突 →
/// coreApi 比对 → 依赖拓扑排序 → 建 ALC → 加载程序集 → 找唯一 <see cref="IPlugin"/> → 建实例 →
/// 校验 id → InitializeAsync → Seal() → StartAsync；退出时逆序 Stop + Unload。
///
/// ⚠ 它与 <see cref="PluginLoadContext"/> 的分工：后者只负责「程序集从哪来」（ALC），
/// 本类负责「什么时候建、建完干什么」。别把两件事混在一个类里。
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
    private readonly List<Handle> _handles = [];
    private readonly List<(string Id, WeakReference Weak)> _pendingUnloadChecks = [];

    public PluginLoader(ServiceRegistry registry, IEventBus bus, ConfigStore config, AppLogger logger)
    {
        _registry = registry;
        _bus = bus;
        _config = config;
        _logger = logger;
    }

    /// <summary>本次加载的全部结论（按拓扑序）。</summary>
    public IReadOnlyList<PluginLoadResult> Results => _handles.Select(h => h.ToResult()).ToList();

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

        Discover(pluginsDirectory);
        DetectFieldConflicts();
        CheckApiCompatibility();
        SortByDependencies();
        LoadAssemblies();
        await InitializeAllAsync(pluginsDirectory, dataDirectory, cancellationToken).ConfigureAwait(false);

        _registry.Seal();   // §3.1 第 12 步：任何 Start 之前封板

        await StartAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var handle in _handles)
        {
            var result = handle.ToResult();
            if (result.IsRunning)
                _logger.Info($"插件 {result.PluginId} 已启动（版本 {handle.Manifest?.Version}）");
            else if (result.Code is not null)
                _logger.Error($"插件 {result.PluginId} 未能启动：[{result.Code}] {result.Message}");
        }

        return new PluginLoadReport(Results);
    }

    /// <summary>逆拓扑序停止并卸载全部插件（§3.5 退出时序）。单个插件失败不阻断其它插件。</summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _pendingUnloadChecks.Clear();

        for (var i = _handles.Count - 1; i >= 0; i--)
        {
            var handle = _handles[i];

            if (handle.Plugin is not null
                && handle.State is PluginState.Initialized or PluginState.Started or PluginState.StartFailed)
            {
                try
                {
                    await handle.Plugin.StopAsync(cancellationToken)
                        .WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 卸载是尽力而为：插件停不下来也必须继续（否则程序关不掉，§3.5）。
                    _logger.Warn($"插件 {handle.Id} StopAsync 失败/超时，已跳过：{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (handle.Context is not null)
            {
                _bus.DisposeOwner(handle.Context.EventOwner);   // 保险：插件没释放订阅也不会回调进已卸载 ALC
                _registry.RemoveAllOf(handle.Id);
            }

            var weak = UnloadAndDropStrongReference(handle);
            handle.State = PluginState.Unloaded;
            if (weak is not null)
                _pendingUnloadChecks.Add((handle.Id, weak));
        }

        // ⚠ 回收判定必须在**全新的栈**上做（§7 陷阱 11/12）：异步状态机里残留的
        // 强引用（以及 JIT 的栈槽）会让 ALC 在本帧内恒 IsAlive → 恒假 Warn。
        // 换线程判定才观测得到真实回收情况；判定失败按约定只 Warn、不阻断退出（§3.4）。
        if (_pendingUnloadChecks.Count > 0)
        {
            await Task.Run(VerifyUnloadedContexts, CancellationToken.None).ConfigureAwait(false);
            _pendingUnloadChecks.Clear();
        }
    }

    private void VerifyUnloadedContexts()
    {
        foreach (var (id, weak) in _pendingUnloadChecks)
        {
            if (PluginLoadContext.VerifyUnloaded(weak, out var detail))
                _logger.Debug($"插件 {id} 的 ALC 已回收");
            else
                _logger.Warn($"插件 {id} 已卸载但内存未释放（不阻断）：{detail}");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    // ── 第 8 步：扫描 + manifest 校验 ────────────────────────────────────

    private void Discover(string pluginsDirectory)
    {
        foreach (var dir in Directory.GetDirectories(pluginsDirectory).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;   // E1：没有清单的目录直接跳过（可能是用户手工放的空目录）

            var handle = new Handle(Path.GetFileName(dir), dir);
            _handles.Add(handle);

            PluginManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                handle.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid, $"plugin.json 读取/解析失败：{ex.Message}");
                _logger.Error($"插件目录 {handle.Id} 的 plugin.json 损坏", ex);
                continue;
            }

            var invalid = Validate(handle.Id, manifest);
            if (invalid is not null)
            {
                handle.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid, invalid);
                _logger.Error($"插件 {handle.Id} 清单非法：{invalid}");
                continue;
            }

            handle.Manifest = manifest;
            handle.State = PluginState.Discovered;

            // 已声明但 v1 不实现的钩子字段：只记 Warn（§2.11）。
            if (manifest!.ConfigSection is not null)
                _logger.Warn($"插件 {handle.Id} 声明了 configSection（嵌套节尚未实现），已按扁平字段处理");
            if (manifest.ConfigFieldsShared is not null)
                _logger.Warn($"插件 {handle.Id} 声明了 configFieldsShared（只读共享字段 v1 未实现），已忽略");
            if (!string.IsNullOrWhiteSpace(manifest.Version) && !Version.TryParse(manifest.Version, out _))
                _logger.Warn($"插件 {handle.Id} 的 version「{manifest.Version}」不是 x.y.z 形式（只告警，不拒载）");
        }

        // E5：同一 id 出现两次 → 后一个拒载（依赖图里 id 必须唯一）。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in _handles.Where(h => h.Manifest is not null).ToList())
        {
            if (!seen.Add(handle.Manifest!.Id))
            {
                handle.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid,
                    $"id「{handle.Manifest.Id}」重复（先扫描到的同 id 插件保留）");
                _logger.Error($"插件目录 {handle.Id} 的 id 与另一个插件重复，已拒载");
            }
        }
    }

    private static string? Validate(string directoryName, PluginManifest? manifest)
    {
        if (manifest is null)
            return "plugin.json 为空或不是 JSON 对象";

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "缺少必需字段 id";
        if (string.IsNullOrWhiteSpace(manifest.Entry))
            return "缺少必需字段 entry";
        if (string.IsNullOrWhiteSpace(manifest.CoreApi))
            return "缺少必需字段 coreApi";

        // E4：id 必须等于目录名（忽略大小写），否则「卸载时删错目录」。
        if (!string.Equals(manifest.Id, directoryName, StringComparison.OrdinalIgnoreCase))
            return $"id「{manifest.Id}」与目录名「{directoryName}」不一致";

        // "core" 是核心的 owner 名：插件占用它会让按 owner 清理时误伤核心登记。
        if (string.Equals(manifest.Id, ServiceRegistry.CoreOwner, StringComparison.OrdinalIgnoreCase))
            return "id 不能是保留名 core";

        return null;
    }

    // ── 第 9 步：全局 configFields 冲突（§5.0.1）────────────────────────

    private void DetectFieldConflicts()
    {
        var valid = _handles.Where(h => h.Manifest is not null).Select(h => h.Manifest!).ToList();
        if (valid.Count == 0)
            return;

        var report = ConfigFieldClaimReport.Resolve(valid);
        foreach (var handle in _handles)
        {
            if (handle.Manifest is null || !report.RejectedPluginIds.Contains(handle.Manifest.Id))
                continue;

            var conflict = report.Conflicts.FirstOrDefault(c => c.SecondPluginId == handle.Manifest.Id);
            handle.Fail(PluginState.LoadFailed, ErrorCodes.FieldConflict,
                conflict?.Message ?? $"配置字段与其它插件冲突：{handle.Manifest.Id}");
            _logger.Error(conflict?.Message ?? $"插件 {handle.Manifest.Id} 配置字段冲突，已拒载");
        }
    }

    // ── E7：coreApi 比对（D2：只比主版本）───────────────────────────────

    private void CheckApiCompatibility()
    {
        foreach (var handle in _handles)
        {
            if (handle.Manifest is null || handle.State != PluginState.Discovered)
                continue;

            if (!CoreInfo.IsApiCompatible(handle.Manifest.CoreApi))
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.CoreApiMismatch,
                    $"插件声明 coreApi「{handle.Manifest.CoreApi}」，当前核心为 {CoreInfo.ApiVersion}（主版本必须一致）");
                _logger.Error($"插件 {handle.Manifest.Id} 的 coreApi 与核心不兼容，已拒载");
            }
        }
    }

    // ── 第 9 步：依赖图 + 拓扑排序（E8 / E9）────────────────────────────

    private void SortByDependencies()
    {
        var candidates = _handles.Where(h => h.State == PluginState.Discovered && h.Manifest is not null).ToList();
        var byId = candidates.ToDictionary(h => h.Manifest!.Id, h => h, StringComparer.Ordinal);

        // L1：依赖的**插件 id** 是否都在本次扫描结果里（§4.3；服务存在性只能在 L2 由插件自己断言）。
        var alive = new List<Handle>();
        foreach (var handle in candidates)
        {
            var missing = handle.Manifest!.Dependencies
                .Where(dep => !byId.ContainsKey(dep))
                .ToList();

            if (missing.Count > 0)
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.DependencyMissing,
                    $"缺少依赖插件：{string.Join(", ", missing)}");
                _logger.Error($"插件 {handle.Manifest.Id} 依赖缺失：{string.Join(", ", missing)}");
                continue;
            }

            alive.Add(handle);
        }

        // Kahn 拓扑排序：出不了队的 = 环上节点（或依赖环的节点）→ dependency-cycle。
        var indegree = alive.ToDictionary(h => h.Manifest!.Id, _ => 0, StringComparer.Ordinal);
        foreach (var handle in alive)
            indegree[handle.Manifest!.Id] = handle.Manifest.Dependencies.Distinct(StringComparer.Ordinal).Count();

        var queue = new Queue<Handle>(alive.Where(h => indegree[h.Manifest!.Id] == 0));
        var ordered = new List<Handle>(alive.Count);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            current.State = PluginState.Sorted;
            ordered.Add(current);

            foreach (var dependent in alive)
            {
                if (dependent.State == PluginState.Sorted)
                    continue;

                if (!dependent.Manifest!.Dependencies.Contains(current.Manifest!.Id, StringComparer.Ordinal))
                    continue;

                if (--indegree[dependent.Manifest!.Id] == 0)
                    queue.Enqueue(dependent);
            }
        }

        foreach (var handle in alive.Where(h => h.State != PluginState.Sorted))
        {
            handle.Fail(PluginState.LoadFailed, ErrorCodes.DependencyCycle,
                $"依赖成环：{handle.Manifest!.Id} → {string.Join(" → ", handle.Manifest.Dependencies)}");
            _logger.Error($"插件 {handle.Manifest!.Id} 依赖成环，已拒载");
        }

        // 把成功的按拓扑序排到前面，失败的保持原顺序排在后面（报告按这个顺序输出）。
        var failed = _handles.Where(h => h.State != PluginState.Sorted).ToList();
        _handles.Clear();
        _handles.AddRange(ordered);
        _handles.AddRange(failed);
    }

    // ── 第 10 步：建 ALC → 加载程序集 → 找唯一 IPlugin → 建实例 ─────────

    private void LoadAssemblies()
    {
        foreach (var handle in _handles.Where(h => h.State == PluginState.Sorted).ToList())
        {
            var manifest = handle.Manifest!;
            var entry = Path.Combine(handle.Directory, manifest.Entry);

            if (!File.Exists(entry))
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.EntryMissing, $"入口文件缺失：{manifest.Entry}");
                _logger.Error($"插件 {manifest.Id} 的入口文件缺失：{entry}");
                continue;
            }

            // 每个插件一个可回收 ALC —— 共享白名单由 PluginLoadContext 负责（§0.8）。
            var alc = new PluginLoadContext(manifest.Id, handle.Directory);
            handle.Alc = alc;

            Assembly assembly;
            try
            {
                assembly = alc.LoadFromAssemblyPath(entry);
            }
            catch (Exception ex)
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed,
                    $"程序集加载失败：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {manifest.Id} 程序集加载失败", ex);
                handle.State = PluginState.Unloaded;
                UnloadAndDropStrongReference(handle);
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed,
                    "程序集类型加载失败：" + string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
                _logger.Error($"插件 {manifest.Id} 的类型加载失败", ex);
                handle.State = PluginState.Unloaded;
                UnloadAndDropStrongReference(handle);
                continue;
            }

            // E11：必须恰好一个 IPlugin 实现（抽象类不算）。
            var pluginTypes = types
                .Where(t => t is { IsAbstract: false, IsInterface: false })
                .Where(t => typeof(IPlugin).IsAssignableFrom(t))
                .ToList();

            if (pluginTypes.Count != 1)
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.PluginTypeAmbiguous,
                    $"程序集里应恰好有 1 个 IPlugin 实现，实际 {pluginTypes.Count} 个");
                _logger.Error($"插件 {manifest.Id} 的入口类型不唯一（{pluginTypes.Count} 个）");
                handle.State = PluginState.Unloaded;
                UnloadAndDropStrongReference(handle);
                continue;
            }

            IPlugin plugin;
            try
            {
                plugin = (IPlugin)Activator.CreateInstance(pluginTypes[0])!;
            }
            catch (Exception ex)
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed,
                    $"插件实例创建失败：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {manifest.Id} 的实例创建失败", ex);
                handle.State = PluginState.Unloaded;
                UnloadAndDropStrongReference(handle);
                continue;
            }

            // E13：插件声明的 Id 必须与清单一致。
            if (!string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
            {
                handle.Fail(PluginState.LoadFailed, ErrorCodes.IdMismatch,
                    $"插件 Id「{plugin.Id}」与清单 id「{manifest.Id}」不一致");
                _logger.Error($"插件 {manifest.Id} 的 Id 与清单不一致，已拒载");
                handle.State = PluginState.Unloaded;
                UnloadAndDropStrongReference(handle);
                continue;
            }

            handle.Plugin = plugin;
            handle.State = PluginState.Loaded;
        }
    }

    // ── 第 11 步：InitializeAsync（只注册，不开工）──────────────────────

    private async Task InitializeAllAsync(string pluginsDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        foreach (var handle in _handles.Where(h => h.State == PluginState.Loaded).ToList())
        {
            var manifest = handle.Manifest!;
            var context = new PluginContext(
                manifest.Id,
                _registry,
                _bus,
                _config.CreateHandle(manifest.Id, manifest.ConfigFields),
                new PluginStorage(manifest.Id, pluginsDirectory, dataDirectory),
                _logger.ForPlugin(manifest.Id));
            handle.Context = context;

            try
            {
                var result = await handle.Plugin!.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                if (result.IsOk)
                {
                    handle.State = PluginState.Initialized;
                    continue;
                }

                handle.Fail(PluginState.InitFailed, result.Code, result.Message);
            }
            catch (Exception ex)
            {
                handle.Fail(PluginState.InitFailed, ErrorCodes.PluginThrew,
                    $"InitializeAsync 抛异常：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {manifest.Id} 初始化抛异常", ex);
            }

            // 失败回滚：该插件注册的服务与订阅一律清掉（§3.1 11.2 的"loader 统一清"）。
            _bus.DisposeOwner(context.EventOwner);
            _registry.RemoveAllOf(manifest.Id);
        }
    }

    // ── 第 13 步：StartAsync（超时 10 秒；失败回滚）──────────────────────

    private async Task StartAllAsync(CancellationToken cancellationToken)
    {
        foreach (var handle in _handles.Where(h => h.State == PluginState.Initialized).ToList())
        {
            try
            {
                var result = await handle.Plugin!.StartAsync(cancellationToken)
                    .WaitAsync(StartTimeout, cancellationToken).ConfigureAwait(false);

                if (result.IsOk)
                {
                    handle.State = PluginState.Started;
                    continue;
                }

                handle.Fail(PluginState.StartFailed, result.Code, result.Message);
            }
            catch (TimeoutException)
            {
                // E18：超时说明它还活着 —— **不卸载 ALC**（避免正在跑的代码访问已释放元数据）。
                handle.Fail(PluginState.StartFailed, ErrorCodes.PluginLoadFailed,
                    $"StartAsync 超过 {StartTimeout.TotalSeconds:0} 秒未返回（不卸载，下次启动再试）");
                _logger.Warn($"插件 {handle.Manifest!.Id} 启动超时（{StartTimeout.TotalSeconds:0} 秒），已标记失败但不卸载");
                continue;
            }
            catch (Exception ex)
            {
                handle.Fail(PluginState.StartFailed, ErrorCodes.PluginThrew,
                    $"StartAsync 抛异常：{ex.GetType().Name}: {ex.Message}");
                _logger.Error($"插件 {handle.Manifest!.Id} 启动抛异常", ex);
            }

            // 启动失败回滚：调 StopAsync（尽力而为）+ 清理注册项。
            try
            {
                await handle.Plugin!.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"插件 {handle.Manifest!.Id} 启动失败回滚时 StopAsync 又抛异常：{ex.Message}");
            }

            if (handle.Context is not null)
            {
                _bus.DisposeOwner(handle.Context.EventOwner);
                _registry.RemoveAllOf(handle.Manifest!.Id);
            }
        }
    }

    /// <summary>卸载并丢掉全部强引用，只返回弱引用。**禁止内联**（否则 alc 栈槽会被延长到调用方）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? UnloadAndDropStrongReference(Handle handle)
    {
        // 三处强引用必须全部清掉，否则 ALC 永远不回收（插件实例/上下文/加载上下文）。
        handle.Plugin = null;
        handle.Context = null;

        var alc = handle.Alc;
        handle.Alc = null;
        if (alc is null)
            return null;

        var weak = new WeakReference(alc);
        alc.Unload();
        return weak;
    }

    /// <summary>加载过程中的可变句柄（报告里一律转成不可变的 <see cref="PluginLoadResult"/>）。</summary>
    private sealed class Handle(string id, string directory)
    {
        public string Id { get; } = id;

        public string Directory { get; } = directory;

        public PluginManifest? Manifest { get; set; }

        public PluginState State { get; set; } = PluginState.Discovered;

        public string? Code { get; private set; }

        public string? Message { get; private set; }

        public PluginLoadContext? Alc { get; set; }

        public IPlugin? Plugin { get; set; }

        public PluginContext? Context { get; set; }

        public void Fail(PluginState state, string? code, string? message)
        {
            State = state;
            Code = code;
            Message = message;
        }

        public PluginLoadResult ToResult() => new(Manifest?.Id ?? Id, State, Code, Message);
    }
}
