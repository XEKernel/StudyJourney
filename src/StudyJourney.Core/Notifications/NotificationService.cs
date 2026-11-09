using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Notifications;

/// <summary>
/// 通知出口（CORE_ARCHITECTURE §2.8）：句柄分配、生命期跟踪、静音判定；**渲染交给**
/// <see cref="INotificationRenderer"/>（App）。
///
/// 两条必须守住的语义：
/// <list type="number">
/// <item><b><see cref="NotificationRequest.Silent"/> 只静音、弹窗照常</b>（PITFALLS §A）：
///       老师不会因为静音而错过提醒，所以本服务**绝不因为静音而丢弃通知**。</item>
/// <item><b>通知失败不能影响调用方业务</b>：更新检查失败、提醒发不出，都不该让插件崩掉。
///       因此 <see cref="ShowAsync"/> 返回 <see cref="Result"/>，且渲染器抛异常在这里兜住。</item>
/// </list>
///
/// ⚠ <b>一处契约缺口（写在这里，避免下一个人当 bug 修）</b>：
/// <see cref="NotificationRequest"/> **没有 Subject 字段**，所以本服务无法在
/// <see cref="ShowAsync"/> 里自动套用按科目静音规则。实际用法是调用方先
/// <see cref="IsMutedForAsync"/> 查一次，再把 <c>Silent</c> 写进请求。
/// 想改成"服务自动判"就得给 DTO 加字段——那会动 §5.12 的 DTO 落盘纪律，留到有真实需求时再议。
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, NotificationHandle> _live = [];
    private readonly IPluginLogger? _logger;
    private INotificationRenderer? _renderer;

    /// <param name="logger">日志；<c>null</c> = 不记。</param>
    public NotificationService(IPluginLogger? logger = null) => _logger = logger;

    /// <summary>
    /// 按科目静音的判据（由提醒插件在 M1-D 注入；<c>null</c> = 一律不静音）。
    /// 用委托而不是接口：判据本身是纯函数（<c>SubjectPresentationRules</c>），不值得为它加一个服务。
    /// </summary>
    public Func<string, bool>? MutePolicy { get; set; }

    /// <summary>是否已挂上渲染器（诊断 / smoke 用）。</summary>
    public bool IsAttached
    {
        get { lock (_gate) return _renderer is not null; }
    }

    /// <summary>仍在显示的条数（诊断用；退出时要清零）。</summary>
    public int LiveCount
    {
        get { lock (_gate) return _live.Count; }
    }

    /// <summary>挂上渲染器（App 在 Avalonia 初始化后调用）。重复挂安全。</summary>
    public void AttachRenderer(INotificationRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        lock (_gate)
            _renderer = renderer;

        _logger?.Info("[Notification] 渲染器已挂上");
    }

    /// <inheritdoc />
    public async Task<Result<NotificationHandle>> ShowAsync(
        NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handle = new NotificationHandle { Value = Guid.NewGuid() };

        INotificationRenderer? renderer;
        lock (_gate)
        {
            renderer = _renderer;
            if (renderer is not null)
                _live[handle.Value] = handle;
        }

        if (renderer is null)
        {
            _logger?.Warn($"[Notification] 无渲染器（宿主未就绪），已丢弃通知「{request.Title}」");
            return Result<NotificationHandle>.Fail(ErrorCodes.NotificationUnavailable,
                "通知服务尚未就绪（壳宿主未挂上），本条通知未显示");
        }

        try
        {
            var shown = await renderer.ShowAsync(request, handle, cancellationToken).ConfigureAwait(false);
            if (shown.IsOk)
                return Result<NotificationHandle>.Ok(handle);

            Forget(handle);
            _logger?.Warn($"[Notification] 渲染失败「{request.Title}」：[{shown.Code}] {shown.Message}");
            return Result<NotificationHandle>.Fail(shown.Code, shown.Message, shown.Exception);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 渲染器抛异常不能炸调用方（提醒/更新都在业务路径上调它）。
            Forget(handle);
            _logger?.Error($"[Notification] 渲染器抛出异常「{request.Title}」", ex);
            return Result<NotificationHandle>.Fail(ErrorCodes.NotificationUnavailable,
                "通知渲染异常：" + ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task WithdrawAsync(NotificationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!Forget(handle))
            return;   // 已经撤过 / 本来就是失败的：幂等

        INotificationRenderer? renderer;
        lock (_gate)
            renderer = _renderer;

        if (renderer is null)
            return;

        try
        {
            await renderer.WithdrawAsync(handle, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Notification] 撤回失败（已忽略）：{ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<bool> IsMutedForAsync(string subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return Task.FromResult(false);

        return Task.FromResult(MutePolicy is { } policy && policy(subject));
    }

    /// <summary>撤回全部仍在显示的通知（退出时调用，避免残留气泡）。</summary>
    public async Task WithdrawAllAsync()
    {
        NotificationHandle[] snapshot;
        lock (_gate)
            snapshot = [.. _live.Values];

        foreach (var handle in snapshot)
            await WithdrawAsync(handle).ConfigureAwait(false);
    }

    private bool Forget(NotificationHandle handle)
    {
        lock (_gate)
            return _live.Remove(handle.Value);
    }
}
