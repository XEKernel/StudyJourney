using StudyJourney.Core.Notifications;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Core.Tests.Notifications;

/// <summary>
/// S3：<see cref="NotificationService"/>（CORE_ARCHITECTURE §2.8）。
///
/// 最该钉死的一条：**静音只静音、弹窗照常**（PITFALLS §A）。老师不会因为静音而错过提醒，
/// 所以"静音"绝不能退化成"不显示"——这条一旦错了，表现是"提醒偶尔不来"，极难查。
/// </summary>
public sealed class NotificationServiceTests
{
    private static NotificationRequest Reminder(bool silent = false, NotificationLevel level = NotificationLevel.Reminder)
        => new()
        {
            Title = "上课了",
            Body = "数学 开始上课",
            Level = level,
            PreferredStyle = NotificationStyle.Capsule,
            Silent = silent,
        };

    [Fact(DisplayName = "S3-01 无渲染器 → notification-unavailable，不抛、不挂死调用方")]
    public async Task Show_WithoutRenderer_FailsCleanly()
    {
        var service = new NotificationService();

        var result = await service.ShowAsync(Reminder());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.NotificationUnavailable, result.Code);
        Assert.False(service.IsAttached);
        Assert.Equal(0, service.LiveCount);
    }

    [Fact(DisplayName = "S3-02 有渲染器 → 返回句柄，句柄唯一，计入 LiveCount")]
    public async Task Show_WithRenderer_ReturnsUniqueHandles()
    {
        var renderer = new RecordingNotificationRenderer();
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        var first = await service.ShowAsync(Reminder());
        var second = await service.ShowAsync(Reminder());

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);
        Assert.NotEqual(first.Value!.Value, second.Value!.Value);
        Assert.Equal(2, service.LiveCount);
        Assert.Equal(2, renderer.Shown.Count);
        Assert.True(service.IsAttached);
    }

    [Fact(DisplayName = "S3-03 静音只影响声音：Silent=true 的通知仍然照常交给渲染器（不丢弃）")]
    public async Task Show_Silent_IsStillDisplayed()
    {
        var renderer = new RecordingNotificationRenderer();
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        var result = await service.ShowAsync(Reminder(silent: true));

        Assert.True(result.IsOk);
        var shown = Assert.Single(renderer.Shown);
        Assert.True(shown.Request.Silent, "静音标记必须原样传下去");
        Assert.Equal("数学 开始上课", shown.Request.Body);
    }

    [Fact(DisplayName = "S3-04 渲染器返回失败 → 原样透传，且不留孤儿句柄")]
    public async Task Show_RendererFailure_PropagatesAndForgets()
    {
        var renderer = new RecordingNotificationRenderer
        {
            FailWith = Result.Fail("render-failed", "托盘不可用"),
        };
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        var result = await service.ShowAsync(Reminder());

        Assert.True(result.IsFailure);
        Assert.Equal("render-failed", result.Code);
        Assert.Equal(0, service.LiveCount);
    }

    [Fact(DisplayName = "S3-05 渲染器抛异常 → 兜住并转成失败结果（业务方不被炸）")]
    public async Task Show_RendererThrows_IsCaught()
    {
        var renderer = new RecordingNotificationRenderer { ThrowOnShow = true };
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        var result = await service.ShowAsync(Reminder());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.NotificationUnavailable, result.Code);
        Assert.Equal(0, service.LiveCount);
    }

    [Fact(DisplayName = "S3-06 Withdraw：调渲染器撤回 + 出队；重复撤回幂等不抛")]
    public async Task Withdraw_RemovesAndIsIdempotent()
    {
        var renderer = new RecordingNotificationRenderer();
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        var shown = await service.ShowAsync(Reminder());
        var handle = shown.Value!;

        await service.WithdrawAsync(handle);
        Assert.Equal(0, service.LiveCount);
        Assert.Single(renderer.Withdrawn);

        await service.WithdrawAsync(handle);   // 再撤一次
        Assert.Single(renderer.Withdrawn);      // 不会重复调渲染器
    }

    [Fact(DisplayName = "S3-07 从未显示过的句柄撤回 → 静默忽略（不抛、不调渲染器）")]
    public async Task Withdraw_UnknownHandle_IsNoOp()
    {
        var renderer = new RecordingNotificationRenderer();
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        await service.WithdrawAsync(new NotificationHandle { Value = Guid.NewGuid() });

        Assert.Empty(renderer.Withdrawn);
    }

    [Fact(DisplayName = "S3-08 WithdrawAll 清空全部在显通知")]
    public async Task WithdrawAll_ClearsEverything()
    {
        var renderer = new RecordingNotificationRenderer();
        var service = new NotificationService();
        service.AttachRenderer(renderer);

        for (var i = 0; i < 3; i++)
            await service.ShowAsync(Reminder());

        await service.WithdrawAllAsync();

        Assert.Equal(0, service.LiveCount);
        Assert.Equal(3, renderer.Withdrawn.Count);
    }

    [Fact(DisplayName = "S3-09 IsMutedForAsync：无策略一律 false；有策略按委托；空科目 false")]
    public async Task IsMutedFor_UsesPolicy()
    {
        var service = new NotificationService();

        Assert.False(await service.IsMutedForAsync("数学"));
        Assert.False(await service.IsMutedForAsync("  "));

        service.MutePolicy = subject => subject == "体育";
        Assert.True(await service.IsMutedForAsync("体育"));
        Assert.False(await service.IsMutedForAsync("数学"));
    }

    [Fact(DisplayName = "S3-10 渲染器晚挂：先 Show 失败不缓存，挂上后立刻能用")]
    public async Task AttachRenderer_AfterFailure_TakesEffectImmediately()
    {
        var service = new NotificationService();
        Assert.True((await service.ShowAsync(Reminder())).IsFailure);

        service.AttachRenderer(new RecordingNotificationRenderer());

        Assert.True((await service.ShowAsync(Reminder())).IsOk);
    }
}

/// <summary>通知渲染器替身：记录收到的请求，可配置失败/抛异常。</summary>
internal sealed class RecordingNotificationRenderer : INotificationRenderer
{
    public List<(NotificationRequest Request, NotificationHandle Handle)> Shown { get; } = [];

    public List<NotificationHandle> Withdrawn { get; } = [];

    public Result? FailWith { get; init; }

    public bool ThrowOnShow { get; init; }

    public Task<Result> ShowAsync(NotificationRequest request, NotificationHandle handle, CancellationToken cancellationToken)
    {
        if (ThrowOnShow)
            throw new InvalidOperationException("模拟渲染器崩溃");

        if (FailWith is { } failure)
            return Task.FromResult(failure);

        Shown.Add((request, handle));
        return Task.FromResult(Result.Ok());
    }

    public Task WithdrawAsync(NotificationHandle handle, CancellationToken cancellationToken)
    {
        Withdrawn.Add(handle);
        return Task.CompletedTask;
    }
}
