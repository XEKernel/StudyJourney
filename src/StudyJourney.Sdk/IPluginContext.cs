using StudyJourney.Sdk.Context;

namespace StudyJourney.Sdk;

/// <summary>
/// 插件拿到一切能力的**唯一入口**（CORE_ARCHITECTURE §2.2）。
///
/// ⚠ **形态约束（E1 已定）：全部是工厂方法，不是"服务实例属性袋"；成员不超过 6 个。**
/// 之所以不提供 <c>ctx.Schedule</c> / <c>ctx.Notifications</c> 这类属性：
/// 那会让本接口每加一个插件就长两个属性，三年后变成 30 个属性的新 God Object
/// —— 正是 <c>App.axaml.cs</c> 走过的路，只是换了个名字。
///
/// 成员清单（6 个，一个不多）：
/// <list type="number">
/// <item><see cref="GetService{T}"/> —— 取任意已注册服务</item>
/// <item><see cref="GetService(Type)"/> —— 同上，运行期类型查找</item>
/// <item><see cref="GetEventBus"/></item>
/// <item><see cref="GetConfig"/></item>
/// <item><see cref="GetStorage"/></item>
/// <item><see cref="GetLogger"/></item>
/// </list>
/// 后四者之所以不并进 <see cref="GetService{T}"/>：它们在**初始化最早期**就要用
/// （早于任何服务注册），且语义上属"通信 / 配置 / 存储 / 日志"四类基础设施，不是"业务服务"。
/// </summary>
public interface IPluginContext
{
    /// <summary>取已注册的服务；未注册返回 <c>null</c>（不抛异常）。</summary>
    /// <remarks>消费者：全部插件（取别的插件提供的能力）</remarks>
    T? GetService<T>() where T : class;

    /// <summary>按运行期类型取服务；未注册返回 <c>null</c>。</summary>
    /// <remarks>消费者：需要按 Type 动态查找的插件（极少；无此需求时不要用）</remarks>
    object? GetService(Type serviceType);

    /// <summary>事件总线（发布/订阅跨插件事件）。</summary>
    /// <remarks>消费者：提醒/自动化/远程/课表（课表变更通知）</remarks>
    IEventBus GetEventBus();

    /// <summary>本插件绑定的配置句柄（只认识自己 <c>configFields</c> 那批键）。</summary>
    /// <remarks>消费者：所有带配置的插件</remarks>
    IPluginConfig GetConfig();

    /// <summary>本插件的存储句柄（<c>data/&lt;id&gt;/</c>）。</summary>
    /// <remarks>消费者：成绩（SQLite）、诊断（活动记录）、自动化（open-state.json）</remarks>
    IPluginStorage GetStorage();

    /// <summary>带插件 id 前缀的日志器。</summary>
    /// <remarks>消费者：全部插件</remarks>
    IPluginLogger GetLogger();
}
