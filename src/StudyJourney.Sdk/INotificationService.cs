using StudyJourney.Sdk.Dto;

namespace StudyJourney.Sdk;

/// <summary>
/// 通知出口（CORE_ARCHITECTURE §2.8）。替代 v3.3.0 的 <c>App.ShowSystemNotification</c>。
///
/// ⚠ **静音判定在这里，不在调用方**：按科目静音（<c>SubjectPresentationRules</c>）对所有通知生效，
/// 不只对提醒生效——自动化、更新、诊断发通知时同样要遵守。
///
/// ⚠ **一条必须保留的语义**：<see cref="NotificationRequest.Silent"/> 只静音、**弹窗照常**
/// （PITFALLS §A：老师不会因为静音而错过提醒）。
/// </summary>
public interface INotificationService
{
    /// <summary>显示一条通知。返回的 handle 可用于撤回。</summary>
    /// <remarks>消费者：提醒（各种提醒）、自动化（触发提示）、更新（下载/失败）、诊断（导出完成）</remarks>
    Task<Result<NotificationHandle>> ShowAsync(NotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>撤回一条仍在显示的通知（例如用"更新完成"替换"正在下载"）。</summary>
    /// <remarks>消费者：更新（进度 → 完成）</remarks>
    Task WithdrawAsync(NotificationHandle handle);

    /// <summary>查询"该科目当前是否静音"（供插件决定要不要额外播放声音）。</summary>
    /// <remarks>消费者：提醒（按科目静音）、自动化（听力节次）</remarks>
    Task<bool> IsMutedForAsync(string subject, CancellationToken cancellationToken = default);
}
