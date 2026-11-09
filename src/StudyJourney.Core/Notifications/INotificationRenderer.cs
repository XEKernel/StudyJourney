using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Notifications;

/// <summary>
/// 通知的**渲染层**抽象：真正把一条通知画出来（主窗胶囊 / Windows 托盘气泡）。
///
/// 与 <see cref="Shell.IUiHost"/> 同理放 <c>Core</c>（插件看不见），由 App 实现：
/// 画胶囊要主窗、发系统气泡要托盘图标，两者都是"窗口生命周期"而非"能力"。
/// </summary>
public interface INotificationRenderer
{
    /// <summary>显示一条通知。失败只影响这一条，不影响后续通知。</summary>
    Task<Result> ShowAsync(NotificationRequest request, NotificationHandle handle, CancellationToken cancellationToken);

    /// <summary>撤回一条仍在显示的通知（已消失时静默成功）。</summary>
    Task WithdrawAsync(NotificationHandle handle, CancellationToken cancellationToken);
}
