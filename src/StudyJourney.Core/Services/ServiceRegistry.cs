using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Services;

/// <summary>一条服务登记（诊断/自检输出用）。</summary>
/// <param name="ServiceType">注册时用的服务类型（通常是接口）。</param>
/// <param name="Owner">归属方：核心固定为 <c>"core"</c>，插件为它的 id。</param>
public sealed record ServiceRegistration(Type ServiceType, string Owner);

/// <summary>
/// 服务注册表的唯一实现（CORE_ARCHITECTURE §4.1–§4.3 / §4.8）。
///
/// 四条硬约束（<see cref="IServiceRegistry"/> 的注释里有同样的四条，这里说清"怎么实现"）：
/// <list type="number">
/// <item><b>只在 Initialize 阶段注册</b>：<see cref="Seal"/> 之后一律 <c>registry-sealed</c>；</item>
/// <item><b>同类型只能一个实现</b>：重复注册返回 <c>service-conflict</c>，**绝不覆盖**——
///       覆盖会让"谁生效"取决于加载顺序；</item>
/// <item><b>只有单例</b>：不引 DI 容器，没有工厂/作用域；</item>
/// <item><b>注册必须带 owner</b>：核心用 <see cref="CoreOwner"/>，插件用插件 id
///       （由 <c>PluginContext</c> 的注册表门面注入）。卸载时按 owner 批量清理。</item>
/// </list>
///
/// 为什么核心要在构造时把自己注册进去：插件 <c>InitializeAsync</c> 的第一行就是
/// <c>ctx.GetService&lt;IServiceRegistry&gt;()</c>，返回 <c>null</c> 的话插件连自己的服务都没法注册。
/// </summary>
public sealed class ServiceRegistry : IServiceRegistry
{
    /// <summary>核心自己的 owner 名。**插件 id 不允许叫这个名字**（由加载器校验）。</summary>
    public const string CoreOwner = "core";

    /// <summary>
    /// 核心保留接口（插件注册一律 <c>reserved-service</c>，§4.1）。
    /// 只收"一旦被替换就会破坏插件间隔离"的那些；<c>INotificationService</c> 这类刻意不保留。
    /// </summary>
    private static readonly HashSet<Type> ReservedServices =
    [
        typeof(IServiceRegistry),
        typeof(IEventBus),
        typeof(IShellService),
    ];

    private readonly object _gate = new();
    private readonly Dictionary<Type, Entry> _services = new();
    private readonly IPluginLogger? _logger;
    private bool _sealed;

    public ServiceRegistry(IPluginLogger? logger = null)
    {
        _logger = logger;

        // §4.8 第 1 项：注册表自己必须可被取到（core owner，跳过保留检查属于核心的正常行为）。
        RegisterOwned(CoreOwner, (IServiceRegistry)this);
    }

    /// <summary>封板后为 true（诊断用）。</summary>
    public bool IsSealed
    {
        get { lock (_gate) return _sealed; }
    }

    /// <summary>当前全部登记的快照（<c>--smoke</c> 打印"注册了哪些服务"用）。</summary>
    public IReadOnlyList<ServiceRegistration> Registrations
    {
        get
        {
            lock (_gate)
                return _services.Select(pair => new ServiceRegistration(pair.Key, pair.Value.Owner)).ToList();
        }
    }

    /// <summary>插件/核心的公开注册入口：owner 固定为 <see cref="CoreOwner"/>（插件走门面，见 <c>PluginServiceRegistry</c>）。</summary>
    public Result Register<TService>(TService instance) where TService : class
        => RegisterOwned(CoreOwner, instance);

    /// <summary>
    /// 带 owner 的注册（§4.2 约束 4）。**插件不能直接调用它**——它由 <c>PluginServiceRegistry</c>
    /// 门面转发，owner 是插件 id，卸载时 <see cref="RemoveAllOf"/> 才能按 owner 一次清干净。
    /// </summary>
    public Result RegisterOwned<TService>(string owner, TService instance) where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(instance);

        var serviceType = typeof(TService);

        lock (_gate)
        {
            if (_sealed)
                return Result.Fail(ErrorCodes.RegistrySealed,
                    $"注册表已封板（全部插件 Initialize 已完成），拒绝注册 {serviceType.Name}");

            if (!string.Equals(owner, CoreOwner, StringComparison.Ordinal) && ReservedServices.Contains(serviceType))
                return Result.Fail(ErrorCodes.ReservedService,
                    $"插件 {owner} 不能注册核心保留服务 {serviceType.Name}");

            if (_services.TryGetValue(serviceType, out var existing))
                return Result.Fail(ErrorCodes.ServiceConflict,
                    $"{serviceType.Name} 已由 {existing.Owner} 注册（重复注册不覆盖）");

            _services[serviceType] = new Entry(owner, instance);
        }

        _logger?.Debug($"[ServiceRegistry] {owner} 注册 {serviceType.Name}");
        return Result.Ok();
    }

    /// <inheritdoc />
    public TService? Get<TService>() where TService : class
    {
        lock (_gate)
            return _services.TryGetValue(typeof(TService), out var entry) ? entry.Instance as TService : null;
    }

    /// <inheritdoc />
    public Result<TService> GetRequired<TService>() where TService : class
    {
        var service = Get<TService>();
        return service is null
            ? Result<TService>.Fail(ErrorCodes.ServiceMissing, $"未注册服务 {typeof(TService).Name}")
            : Result<TService>.Ok(service);
    }

    /// <inheritdoc />
    public bool IsRegistered<TService>() where TService : class
    {
        lock (_gate)
            return _services.ContainsKey(typeof(TService));
    }

    /// <inheritdoc />
    public void Seal()
    {
        lock (_gate)
            _sealed = true;
    }

    /// <summary>
    /// 按 owner 批量清理（§3.4 第 2.5 步）。返回被移除的服务类型（便于日志核对）。
    /// **只删指定 owner 的登记**，核心与其它插件的登记一条都不动。
    /// </summary>
    public IReadOnlyList<Type> RemoveAllOf(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        lock (_gate)
        {
            var removed = _services
                .Where(pair => string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var type in removed)
                _services.Remove(type);

            return removed;
        }
    }

    private sealed record Entry(string Owner, object Instance);
}
