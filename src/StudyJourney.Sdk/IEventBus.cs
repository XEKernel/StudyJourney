namespace StudyJourney.Sdk;

/// <summary>
/// 事件总线（CORE_ARCHITECTURE §2.4 / §4.4）。跨插件通信的**唯一**通道。
///
/// 六条约束：
/// <list type="number">
/// <item>事件类型必须定义在 Sdk（否则两边是不同 ALC 里的两个 Type，永远匹配不上）。</item>
/// <item><see cref="Publish{TEvent}"/> **在调用线程同步派发**（保证因果顺序，不背 UI 依赖）。</item>
/// <item>逐个 handler try-catch，一个抛异常不影响后续订阅者。</item>
/// <item>同线程递归深度上限 8，超限丢弃并记 Error（防"A 改课表→B 收到→B 又改"回环）。</item>
/// <item>派发顺序 = 注册顺序。</item>
/// <item>订阅必须带 owner；卸载时由加载器 <see cref="DisposeOwner"/> 强制清理（否则回调打进已卸载 ALC → 进程崩）。</item>
/// </list>
/// </summary>
public interface IEventBus
{
    /// <summary>同步派发（在调用线程）。纯通知用这个——不阻塞发布者。</summary>
    /// <remarks>消费者：课表插件（发布 ScheduleChangedEvent）、自动化（发布规则状态变化）</remarks>
    void Publish<TEvent>(TEvent ev) where TEvent : class;

    /// <summary>异步派发：依次 await 每个 <see cref="SubscribeAsync{TEvent}"/> 订阅者；Action 订阅者同步执行。</summary>
    /// <remarks>消费者：需要"等所有订阅者处理完"再继续的发布方（少见）</remarks>
    Task PublishAsync<TEvent>(TEvent ev, CancellationToken cancellationToken = default) where TEvent : class;

    /// <summary>订阅（同步处理器）。返回的 IDisposable 必须由插件在 StopAsync 里释放。</summary>
    /// <param name="owner">归属对象，用于 <see cref="DisposeOwner"/> 批量清理。</param>
    /// <remarks>消费者：提醒/自动化/远程/主窗（订阅课表变更）</remarks>
    IDisposable Subscribe<TEvent>(object owner, Action<TEvent> handler) where TEvent : class;

    /// <summary>订阅（异步处理器），由发布方按序 await。</summary>
    /// <param name="owner">归属对象。</param>
    /// <remarks>消费者：需要读盘/网络后再反应的订阅者</remarks>
    IDisposable SubscribeAsync<TEvent>(object owner, Func<TEvent, Task> handler) where TEvent : class;

    /// <summary>移除该 owner 的全部订阅。**加载器在卸载时调用**，插件的 StopAsync 是第一道（这是保险）。</summary>
    /// <remarks>消费者：PluginLoader（§3.4 第 2.4 步）</remarks>
    void DisposeOwner(object owner);
}
