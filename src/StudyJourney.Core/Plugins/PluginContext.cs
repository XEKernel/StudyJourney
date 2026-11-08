using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// <see cref="IPluginContext"/> 的实现：插件拿到一切能力的**唯一入口**（§2.2）。
///
/// 两个刻意的"门面"设计（不是多余的一层）：
/// <list type="number">
/// <item><b>注册表门面</b>：插件 <c>ctx.GetService&lt;IServiceRegistry&gt;()</c> 拿到的是绑定到
///       <b>插件 id</b> 的门面，注册出来的服务 owner 就是该插件 → 卸载时按 owner 一次清干净（§4.2 约束 4）。
///       若把核心的真身给插件，注册就全记在 <c>core</c> 名下，卸载清理形同虚设。</item>
/// <item><b>总线门面</b>：插件订阅时传的 owner 被替换成"本上下文"，卸载时
///       <c>DisposeOwner(context)</c> 必然清掉它的全部订阅。插件传错 owner 也不会漏（§4.4 约束 6）。</item>
/// </list>
///
/// ⚠ <b>M0 没有壳</b>：<see cref="GetUI"/> 在壳未装配时抛 <see cref="NotSupportedException"/>。
/// 菜单/托盘/热键/槽位是 M1 的事（D-5 已拍板 M0 不实现槽位），这里不做"假壳"。
/// </summary>
public sealed class PluginContext : IPluginContext
{
    private readonly ServiceRegistry _registry;
    private readonly IServiceRegistry _registryFacade;
    private readonly IEventBus _busFacade;
    private readonly IPluginConfig _config;
    private readonly IPluginStorage _storage;
    private readonly IPluginLogger _logger;
    private readonly IShellService? _shell;

    public PluginContext(
        string pluginId,
        ServiceRegistry registry,
        IEventBus eventBus,
        IPluginConfig config,
        IPluginStorage storage,
        IPluginLogger logger,
        IShellService? shell = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        PluginId = pluginId;
        _registry = registry;
        _config = config;
        _storage = storage;
        _logger = logger;
        _shell = shell;
        _registryFacade = new PluginServiceRegistry(registry, pluginId);
        _busFacade = new PluginEventBus(eventBus, this);
    }

    /// <summary>本上下文属于哪个插件（加载器用于核对 <c>plugin.Id</c>）。</summary>
    public string PluginId { get; }

    /// <summary>加载器卸载时用它调 <c>EventBus.DisposeOwner</c>（= 总线门面的 owner）。</summary>
    internal object EventOwner => this;

    /// <inheritdoc />
    public T? GetService<T>() where T : class
    {
        // 注册表/总线必须返回"绑定到本插件"的门面，而不是核心真身（见类注释）。
        if (typeof(T) == typeof(IServiceRegistry))
            return (T)_registryFacade;

        if (typeof(T) == typeof(IEventBus))
            return (T)_busFacade;

        return _registry.Get<T>();
    }

    /// <inheritdoc />
    public IEventBus GetEventBus() => _busFacade;

    /// <inheritdoc />
    public IPluginConfig GetConfig() => _config;

    /// <inheritdoc />
    public IPluginStorage GetStorage() => _storage;

    /// <inheritdoc />
    public IPluginLogger GetLogger() => _logger;

    /// <inheritdoc />
    public IShellService GetUI()
        => _shell ?? throw new NotSupportedException(
            "M0 的空白壳不提供 IShellService（菜单/托盘/热键/槽位属 M1，D-5 已拍板 M0 不实现槽位）");
}

/// <summary>
/// 绑定到某个插件的注册表门面：<c>Register</c> 把 owner 记成插件 id，其余方法直通。
/// <see cref="Seal"/> 是加载器的专属动作（§3.1 第 12 步），插件调用**无效**（不抛，避免插件互相伤害）。
/// </summary>
internal sealed class PluginServiceRegistry(ServiceRegistry inner, string pluginId) : IServiceRegistry
{
    public Result Register<TService>(TService instance) where TService : class
        => inner.RegisterOwned(pluginId, instance);

    public TService? Get<TService>() where TService : class => inner.Get<TService>();

    public Result<TService> GetRequired<TService>() where TService : class => inner.GetRequired<TService>();

    public bool IsRegistered<TService>() where TService : class => inner.IsRegistered<TService>();

    public void Seal()
    {
        // 封板只能由加载器在"全部 Initialize 完成、任何 Start 之前"发起（§3.1 第 12 步）。
    }
}

/// <summary>
/// 绑定到某个插件上下文的事件总线门面：订阅的 owner 一律替换为上下文，
/// 卸载时 <c>DisposeOwner(context)</c> 一定清得到（§4.4 约束 6 的落地方式）。
/// </summary>
internal sealed class PluginEventBus(IEventBus inner, object eventOwner) : IEventBus
{
    public void Publish<TEvent>(TEvent ev) where TEvent : class => inner.Publish(ev);

    public Task PublishAsync<TEvent>(TEvent ev, CancellationToken cancellationToken = default) where TEvent : class
        => inner.PublishAsync(ev, cancellationToken);

    public IDisposable Subscribe<TEvent>(object owner, Action<TEvent> handler) where TEvent : class
        => inner.Subscribe(eventOwner, handler);

    public IDisposable SubscribeAsync<TEvent>(object owner, Func<TEvent, Task> handler) where TEvent : class
        => inner.SubscribeAsync(eventOwner, handler);

    public void DisposeOwner(object owner) => inner.DisposeOwner(eventOwner);
}
