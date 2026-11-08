using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Services;

/// <summary>
/// 事件总线的唯一实现（CORE_ARCHITECTURE §4.4 / §4.5）。
///
/// 六条约束的落地方式：
/// <list type="number">
/// <item>事件类型必须定义在 Sdk —— 这是约定，总线只能按 <see cref="Type"/> 精确匹配（ALC 下同名不同类型永远碰不上）；</item>
/// <item><see cref="Publish{TEvent}"/> **在调用线程同步派发**（不切线程、不排队，保证因果顺序）；</item>
/// <item>逐个 handler <c>try-catch</c>：一个订阅者抛异常不影响其它订阅者，也绝不让发布方炸；</item>
/// <item>同一逻辑调用链递归深度 ≤ 8，超限丢弃 + <c>Error</c>（防"A 改课表→B 收到→B 又改"回环）；</item>
/// <item>派发顺序 = 注册顺序（内部用有序 <see cref="List{T}"/>，不用字典遍历顺序）；</item>
/// <item>订阅必须带 owner，<see cref="DisposeOwner"/> 强制清理（否则回调打进已卸载 ALC → 进程崩）。</item>
/// </list>
///
/// ⚠ <b>同步 <see cref="Publish{TEvent}"/> 遇到异步订阅者会阻塞等待</b>：契约要求同步派发，
/// 而"跳过异步订阅者"会让订阅方静默丢事件。M0 没有 UI 同步上下文，没有死锁面；
/// M1 起在 UI 线程上发布带 IO 的异步处理器时要留意（优先用 <see cref="PublishAsync{TEvent}"/>）。
/// </summary>
public sealed class EventBus : IEventBus
{
    /// <summary>同一逻辑调用链的最大递归深度（§4.4 约束 4）。</summary>
    public const int MaxRecursionDepth = 8;

    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Subscription>> _byType = new();
    private readonly IPluginLogger? _logger;
    private readonly AsyncLocal<int> _depth = new();

    public EventBus(IPluginLogger? logger = null) => _logger = logger;

    /// <inheritdoc />
    public void Publish<TEvent>(TEvent ev) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(ev);

        var snapshot = Snapshot(typeof(TEvent));
        if (snapshot.Count == 0)
            return;

        if (!TryEnter())
            return;

        try
        {
            foreach (var subscription in snapshot)
            {
                // 同步派发：异步处理器在这里被阻塞等待（见类注释的取舍说明）。
                try
                {
                    subscription.Handler(ev).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[EventBus] 订阅者 {Describe(subscription.Owner)} 处理 {typeof(TEvent).Name} 抛异常（已隔离）", ex);
                }
            }
        }
        finally
        {
            _depth.Value--;
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent ev, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(ev);

        var snapshot = Snapshot(typeof(TEvent));
        if (snapshot.Count == 0)
            return;

        if (!TryEnter())
            return;

        try
        {
            foreach (var subscription in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await subscription.Handler(ev).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[EventBus] 订阅者 {Describe(subscription.Owner)} 处理 {typeof(TEvent).Name} 抛异常（已隔离）", ex);
                }
            }
        }
        finally
        {
            _depth.Value--;
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(object owner, Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        return Add(typeof(TEvent), owner, ev =>
        {
            handler((TEvent)ev);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public IDisposable SubscribeAsync<TEvent>(object owner, Func<TEvent, Task> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        return Add(typeof(TEvent), owner, ev => handler((TEvent)ev));
    }

    /// <inheritdoc />
    public void DisposeOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            foreach (var list in _byType.Values)
                list.RemoveAll(subscription => ReferenceEquals(subscription.Owner, owner));
        }
    }

    private IDisposable Add(Type eventType, object owner, Func<object, Task> handler)
    {
        var subscription = new Subscription(owner, handler);

        lock (_gate)
        {
            if (!_byType.TryGetValue(eventType, out var list))
            {
                list = new List<Subscription>();
                _byType[eventType] = list;
            }

            list.Add(subscription);   // 有序：派发顺序 = 注册顺序
        }

        return new Handle(this, eventType, subscription);
    }

    private void Remove(Type eventType, Subscription subscription)
    {
        lock (_gate)
        {
            if (_byType.TryGetValue(eventType, out var list))
                list.Remove(subscription);
        }
    }

    private List<Subscription> Snapshot(Type eventType)
    {
        lock (_gate)
            return _byType.TryGetValue(eventType, out var list) ? list.ToList() : [];
    }

    /// <summary>递归深度闸门：超过上限丢弃本次事件并记 <c>Error</c>（不抛）。</summary>
    private bool TryEnter()
    {
        var depth = _depth.Value;
        if (depth >= MaxRecursionDepth)
        {
            _logger?.Error($"[EventBus] 同步派发递归深度已达 {MaxRecursionDepth} 层，本次事件被丢弃（疑似订阅者互相触发形成回环）");
            return false;
        }

        _depth.Value = depth + 1;
        return true;
    }

    private static string Describe(object owner) => owner.GetType().Name;

    private sealed class Subscription(object owner, Func<object, Task> handler)
    {
        public object Owner { get; } = owner;

        public Func<object, Task> Handler { get; } = handler;
    }

    private sealed class Handle(EventBus bus, Type eventType, Subscription subscription) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            bus.Remove(eventType, subscription);
        }
    }
}
