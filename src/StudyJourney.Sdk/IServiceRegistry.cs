namespace StudyJourney.Sdk;

/// <summary>
/// 服务注册表（CORE_ARCHITECTURE §2.3 / §4.2）。插件之间**唯一**的能力发现方式。
///
/// 四条硬约束：
/// <list type="number">
/// <item>只在 <see cref="IPlugin.InitializeAsync"/> 阶段注册；<see cref="Seal"/> 之后返回 registry-sealed。</item>
/// <item>同一类型只允许一个实现；重复注册返回 service-conflict（**不覆盖**）。</item>
/// <item>只有单例，没有工厂 / 作用域（v1 不引 DI 容器）。</item>
/// <item>注册带 owner（加载器内部重载），卸载时按 owner 批量清理。</item>
/// </list>
/// </summary>
public interface IServiceRegistry
{
    /// <summary>注册单例服务。</summary>
    /// <returns>
    /// 成功 → Ok；已注册 → service-conflict；已封板 → registry-sealed；
    /// 注册核心保留接口 → reserved-service。
    /// </returns>
    /// <remarks>消费者：全部插件（在 Initialize 里注册自己的对外能力）</remarks>
    Result Register<TService>(TService instance) where TService : class;

    /// <summary>取服务；未注册返回 <c>null</c>。</summary>
    /// <remarks>消费者：插件之间互相取能力</remarks>
    TService? Get<TService>() where TService : class;

    /// <summary>取服务；未注册返回 service-missing 的失败结果。</summary>
    /// <remarks>消费者：依赖方在 Initialize 第一行断言"我的依赖在不在"（§4.3 L2）</remarks>
    Result<TService> GetRequired<TService>() where TService : class;

    /// <summary>探测某服务是否已注册。</summary>
    /// <remarks>消费者：可选依赖（有则增强、无则降级）的插件</remarks>
    bool IsRegistered<TService>() where TService : class;

    /// <summary>封板：由加载器在"全部 Initialize 完成、任何 Start 之前"调用，之后拒收注册。</summary>
    /// <remarks>消费者：PluginLoader（第 12 步）。二次调用幂等。</remarks>
    void Seal();
}
