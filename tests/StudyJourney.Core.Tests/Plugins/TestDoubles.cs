using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// 测试用的最小服务注册表。
///
/// **这不是 S9 的 <c>ServiceRegistry</c> 实现**，也不会变成它 —— 它只是让 A0-15 能跑：
/// 探针插件在 <c>InitializeAsync</c> 里要往"某个注册表"塞服务，核心侧再从同一个注册表取回来。
/// 语义按 <see cref="IServiceRegistry"/> 的契约做（重复注册不覆盖、封板后拒收），
/// 这样即使哪天有人拿它去测别的行为，也不会得出"注册表可以随便覆盖"这种错误结论。
/// </summary>
internal sealed class TestServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<Type, object> _services = new();
    private bool _sealed;

    public TestServiceRegistry()
    {
        // 真实核心会把自己注册进注册表（保留服务，§4.2）——插件通过
        // ctx.GetService<IServiceRegistry>() 拿到它才能注册自己的服务。
        // 这里必须同样做，否则插件第一步就拿不到注册表（A0-15-a 首版就是这么挂的）。
        _services[typeof(IServiceRegistry)] = this;
    }

    public Result Register<TService>(TService instance) where TService : class
    {
        if (_sealed)
            return Result.Fail(ErrorCodes.RegistrySealed, "注册表已封板，拒收注册");

        if (!_services.TryAdd(typeof(TService), instance))
            return Result.Fail(ErrorCodes.ServiceConflict, $"{typeof(TService).Name} 已被注册（不覆盖）");

        return Result.Ok();
    }

    public TService? Get<TService>() where TService : class
        => _services.TryGetValue(typeof(TService), out var o) ? (TService)o : null;

    public Result<TService> GetRequired<TService>() where TService : class
    {
        var got = Get<TService>();
        return got is null
            ? Result<TService>.Fail(ErrorCodes.ServiceMissing, $"未注册服务 {typeof(TService).Name}")
            : Result<TService>.Ok(got);
    }

    public bool IsRegistered<TService>() where TService : class => _services.ContainsKey(typeof(TService));

    public void Seal() => _sealed = true;
}

/// <summary>
/// 测试用的最小 <see cref="IPluginContext"/>。
///
/// **只实现 A0-15 需要的那一条路径**（<see cref="GetService{T}"/>）；其余五个通道一律抛
/// <see cref="NotSupportedException"/> —— 刻意如此：如果哪天探针插件开始偷偷用事件总线/配置，
/// 测试会立刻炸出来，而不是拿着一个空实现静默通过。
/// </summary>
internal sealed class TestPluginContext : IPluginContext
{
    private readonly IServiceRegistry _registry;

    public TestPluginContext(IServiceRegistry registry) => _registry = registry;

    public T? GetService<T>() where T : class => _registry.Get<T>();

    public IEventBus GetEventBus() => throw new NotSupportedException("A0-15 测试不提供事件总线");

    public IPluginConfig GetConfig() => throw new NotSupportedException("A0-15 测试不提供配置");

    public IPluginStorage GetStorage() => throw new NotSupportedException("A0-15 测试不提供存储");

    public IPluginLogger GetLogger() => throw new NotSupportedException("A0-15 测试不提供日志");

    public IShellService GetUI() => throw new NotSupportedException("A0-15 测试不提供 UI 宿主");
}
