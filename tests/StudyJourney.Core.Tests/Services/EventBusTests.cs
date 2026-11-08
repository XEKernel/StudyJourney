using StudyJourney.Core.Services;
using StudyJourney.Core.Tests.TestSupport;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Core.Tests.Services;

/// <summary>
/// S9：事件总线（CORE_ARCHITECTURE §4.4）。守四条容易被实现悄悄破坏的性质：
/// 派发顺序 = 注册顺序、单个订阅者异常不影响其它订阅者/发布方、
/// 两种订阅重载都要真的被调用、owner 清理必须彻底（否则卸载后回调打进已卸载 ALC → 进程崩）。
/// </summary>
public sealed class EventBusTests
{
    private sealed class FakeEvent(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class LoopEvent
    {
    }

    private sealed class OtherEvent
    {
    }

    private readonly RecordingLogger _log = new();
    private readonly object _owner = new();

    [Fact(DisplayName = "S9-09 派发顺序 = 注册顺序")]
    public void Publish_DispatchOrder_IsRegistrationOrder()
    {
        var bus = new EventBus(_log);
        var order = new List<string>();

        bus.Subscribe<FakeEvent>(_owner, _ => order.Add("first"));
        bus.Subscribe<FakeEvent>(_owner, _ => order.Add("second"));
        bus.Subscribe<FakeEvent>(_owner, _ => order.Add("third"));

        bus.Publish(new FakeEvent(1));

        Assert.Equal(new[] { "first", "second", "third" }, order.ToArray());
    }

    [Fact(DisplayName = "S9-10 异常隔离（同步）：中间那个抛，前后都收到，发布方不炸")]
    public void Publish_OneHandlerThrows_OthersStillRun()
    {
        var bus = new EventBus(_log);
        var received = new List<string>();

        bus.Subscribe<FakeEvent>(_owner, _ => received.Add("before"));
        bus.Subscribe<FakeEvent>(_owner, _ => throw new InvalidOperationException("故意抛"));
        bus.Subscribe<FakeEvent>(_owner, _ => received.Add("after"));

        var exception = Record.Exception(() => bus.Publish(new FakeEvent(1)));

        Assert.Null(exception);
        Assert.Equal(new[] { "before", "after" }, received.ToArray());
        Assert.True(_log.Contains("error", "已隔离"));
    }

    [Fact(DisplayName = "S9-11 异常隔离（异步）+ 两种订阅重载都真的被调用")]
    public async Task PublishAsync_InvokesBothOverloads_AndIsolatesExceptions()
    {
        var bus = new EventBus(_log);
        var received = new List<string>();

        bus.Subscribe<FakeEvent>(_owner, _ => received.Add("sync"));
        bus.SubscribeAsync<FakeEvent>(_owner, async _ =>
        {
            await Task.Yield();
            received.Add("async");
        });
        bus.SubscribeAsync<FakeEvent>(_owner, _ => throw new InvalidOperationException("异步故意抛"));
        bus.Subscribe<FakeEvent>(_owner, _ => received.Add("sync-after"));

        await bus.PublishAsync(new FakeEvent(1));

        Assert.Equal(new[] { "sync", "async", "sync-after" }, received.ToArray());
        Assert.True(_log.Contains("error", "已隔离"));
    }

    [Fact(DisplayName = "S9-12 同步 Publish 也会执行异步订阅者（不静默丢事件）")]
    public void Publish_AlsoAwaitsAsyncSubscribers()
    {
        var bus = new EventBus(_log);
        var received = 0;

        bus.SubscribeAsync<FakeEvent>(_owner, _ =>
        {
            received++;
            return Task.CompletedTask;
        });

        bus.Publish(new FakeEvent(1));

        Assert.Equal(1, received);
    }

    [Fact(DisplayName = "S9-13 DisposeOwner 清掉该 owner 的全部订阅（含异步），别人不受影响")]
    public async Task DisposeOwner_RemovesAllSubscriptionsOfThatOwner()
    {
        var bus = new EventBus(_log);
        var otherOwner = new object();
        var mine = 0;
        var theirs = 0;

        bus.Subscribe<FakeEvent>(_owner, _ => mine++);
        bus.SubscribeAsync<FakeEvent>(_owner, _ => { mine++; return Task.CompletedTask; });
        bus.Subscribe<FakeEvent>(otherOwner, _ => theirs++);

        bus.DisposeOwner(_owner);

        bus.Publish(new FakeEvent(1));
        await bus.PublishAsync(new FakeEvent(1));

        Assert.Equal(0, mine);
        Assert.Equal(2, theirs);
    }

    [Fact(DisplayName = "S9-14 单个 handle.Dispose 只移除那一条，且幂等")]
    public void SubscriptionHandle_RemovesOnlyItself()
    {
        var bus = new EventBus(_log);
        var mine = 0;
        var theirs = 0;

        var handle = bus.Subscribe<FakeEvent>(_owner, _ => mine++);
        bus.Subscribe<FakeEvent>(_owner, _ => theirs++);

        handle.Dispose();
        handle.Dispose();   // 幂等

        bus.Publish(new FakeEvent(1));

        Assert.Equal(0, mine);
        Assert.Equal(1, theirs);
    }

    [Fact(DisplayName = "S9-15 不同类型的事件互不干扰")]
    public void DifferentEventTypes_AreIsolated()
    {
        var bus = new EventBus(_log);
        var fakeCount = 0;
        var otherCount = 0;

        bus.Subscribe<FakeEvent>(_owner, _ => fakeCount++);
        bus.Subscribe<OtherEvent>(_owner, _ => otherCount++);

        bus.Publish(new FakeEvent(1));

        Assert.Equal(1, fakeCount);
        Assert.Equal(0, otherCount);
    }

    [Fact(DisplayName = "S9-16 递归深度超过 8 层：丢弃 + Error，不炸（防回环）")]
    public void Publish_RecursionDepth_IsCapped()
    {
        var bus = new EventBus(_log);
        var count = 0;
        var recurse = true;

        bus.Subscribe<LoopEvent>(_owner, _ =>
        {
            count++;
            if (recurse && count < 20)
                bus.Publish(new LoopEvent());
        });

        bus.Publish(new LoopEvent());

        Assert.Equal(EventBus.MaxRecursionDepth, count);
        Assert.True(_log.Contains("error", "递归深度"));

        // 深度必须在派发结束后复位，否则正常事件会被永久吞掉
        recurse = false;
        bus.Publish(new LoopEvent());
        Assert.Equal(EventBus.MaxRecursionDepth + 1, count);
    }

    [Fact(DisplayName = "S9-17 没有订阅者时 Publish / PublishAsync 是空操作（不抛）")]
    public async Task Publish_WithoutSubscribers_IsNoOp()
    {
        var bus = new EventBus(_log);

        bus.Publish(new FakeEvent(1));
        await bus.PublishAsync(new FakeEvent(1));

        Assert.Empty(_log.Entries);
    }
}
