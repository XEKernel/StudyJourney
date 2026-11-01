namespace StudyJourney.Sdk.Dto;

/// <summary>通知严重级别（决定图标与配色）。</summary>
public enum NotificationLevel
{
    /// <summary>普通信息。</summary>
    /// <remarks>消费者：更新（有新版本）、诊断（导出完成）</remarks>
    Info = 0,

    /// <summary>警告（可继续）。</summary>
    /// <remarks>消费者：更新（镜像失败改用直连）、配置（字段冲突提示）</remarks>
    Warn = 1,

    /// <summary>错误（需要人介入）。</summary>
    /// <remarks>消费者：更新（下载失败）、诊断（导出失败）</remarks>
    Error = 2,

    /// <summary>上课提醒（语义区别于普通信息：可被按科目静音规则影响）。</summary>
    /// <remarks>消费者：提醒（全部提醒类型）、自动化（听力节次提示）</remarks>
    Reminder = 3,
}

/// <summary>通知的呈现方式。</summary>
public enum NotificationStyle
{
    /// <summary>主窗胶囊弹窗（v3.3.0 默认）。</summary>
    /// <remarks>消费者：提醒（ReminderStyle=0）</remarks>
    Capsule = 0,

    /// <summary>Windows 系统通知（托盘气泡）。</summary>
    /// <remarks>消费者：提醒（ReminderStyle=1）</remarks>
    System = 1,
}

/// <summary>
/// 一条通知（CORE_ARCHITECTURE §2.8）。
///
/// ⚠ <see cref="Silent"/> 只静音、**弹窗照常**（PITFALLS §A）。
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>标题（"上课了"/"即将下课"/"更新已就绪"）。</summary>
    /// <remarks>消费者：提醒 / 自动化 / 更新 / 诊断</remarks>
    public required string Title { get; init; }

    /// <summary>正文（"数学 开始上课"）。</summary>
    /// <remarks>消费者：与 <see cref="Title"/> 相同的四处</remarks>
    public required string Body { get; init; }

    /// <summary>级别。</summary>
    /// <remarks>消费者：NotificationService（图标/配色/是否受静音规则影响）</remarks>
    public required NotificationLevel Level { get; init; }

    /// <summary>呈现方式（胶囊 / 系统通知）。</summary>
    /// <remarks>消费者：NotificationService（路由到哪个渲染器）</remarks>
    public required NotificationStyle PreferredStyle { get; init; }

    /// <summary>是否静音（只影响声音，不影响显示）。</summary>
    /// <remarks>消费者：NotificationService（播放提示音前判断）、提醒（按科目静音）</remarks>
    public bool Silent { get; init; }

    /// <summary>自定义提示音文件路径；<c>null</c> 或空 = 系统默认提示音。</summary>
    /// <remarks>消费者：NotificationService（播声音）</remarks>
    public string? SoundPath { get; init; }

    /// <summary>自动消失时间（毫秒）。</summary>
    /// <remarks>消费者：NotificationService（胶囊停留时长）</remarks>
    public int TimeoutMs { get; init; } = 6000;
}

/// <summary>
/// 通知句柄（可撤回）。由 <see cref="INotificationService.ShowAsync"/> 返回。
/// 不透明标识：调用方只用来撤回，不解析其内容。
/// </summary>
public sealed record NotificationHandle
{
    /// <summary>内部标识。</summary>
    /// <remarks>消费者：NotificationService（按此撤回）、更新（进度→完成替换）</remarks>
    public required Guid Value { get; init; }
}
