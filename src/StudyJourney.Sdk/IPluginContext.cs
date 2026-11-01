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
/// 成员清单（**六类别各恰好一个出口**，一个不多）：
/// <list type="number">
/// <item><see cref="GetService{T}"/> —— 业务服务（由各插件 <c>Register&lt;T&gt;</c> 注册）</item>
/// <item><see cref="GetEventBus"/> —— 通信</item>
/// <item><see cref="GetConfig"/> —— 配置</item>
/// <item><see cref="GetStorage"/> —— 存储</item>
/// <item><see cref="GetLogger"/> —— 日志</item>
/// <item><see cref="GetUI"/> —— UI 宿主（壳）</item>
/// </list>
///
/// **为什么只有 <see cref="GetService{T}"/> 是泛型，其余五个是定名方法**：
/// 后五类是"基础设施"，它们在**初始化最早期**就要用（早于任何服务注册，也早于
/// 插件之间互相 <c>Register</c>），若都走泛型查找，插件作者会写出
/// <c>GetService&lt;IEventBus&gt;()</c> 这种「用业务通道取基础设施」的写法，
/// 而那条路径在初始化期必然返回 <c>null</c> —— 症状是"日志器是空的"，很难查。
/// 定名方法把这些通道固定下来：语义明确、可用时机明确。
///
/// **为什么删掉了 <c>GetService(Type)</c> 重载**：它为"按运行期 Type 动态查找"服务，
/// 但全项目零消费者（E1 纪律：列不出消费者的一律不放）。更关键的是它会打开
/// 「传一个 interface 变量进去拿服务」的口子 —— 而泛型版本在编译期就能查出类型错。
///
/// **加第 7 个成员的条件**：必须先论证「它不属于服务 / 通信 / 配置 / 存储 / 日志 / UI
/// 这六类中的任何一类」。做不到就别加 —— 这个接口每长一个成员，所有插件都要重新理解它。
/// </summary>
public interface IPluginContext
{
    /// <summary>取已注册的业务服务；未注册返回 <c>null</c>（不抛异常）。</summary>
    /// <remarks>消费者：全部插件（取别的插件提供的能力，如 <c>GetService&lt;IScheduleProvider&gt;()</c>）</remarks>
    T? GetService<T>() where T : class;

    /// <summary>事件总线（发布 / 订阅跨插件事件）。</summary>
    /// <remarks>消费者：提醒 / 自动化 / 远程 / 课表（<c>ScheduleChangedEvent</c> 的发布方与订阅方）</remarks>
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

    /// <summary>
    /// UI 宿主（壳）：菜单 / 托盘 / 热键 / 槽位 / 窗口 / 主题。
    /// **必须从 UI 线程调用**（除 <c>PostToUi</c> 与事件回调）。
    /// </summary>
    /// <remarks>
    /// 消费者：倒计时（<c>main.primary</c> 槽位 + 菜单项） /
    /// 课表（<c>main.secondary</c> 槽位 + 课表编辑器入口） /
    /// 自动化（<c>main.corner</c> 胶囊） / 考试（全屏窗口） / 白板（全屏窗口） / PDF（阅读窗）
    /// </remarks>
    IShellService GetUI();
}
