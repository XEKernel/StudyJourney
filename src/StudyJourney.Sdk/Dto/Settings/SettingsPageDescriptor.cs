using Avalonia.Controls;

namespace StudyJourney.Sdk.Dto;

/// <summary>
/// 一个设置页的描述（CORE_ARCHITECTURE §2.7）。
///
/// 没有 <c>IconKey</c>：v3.3.0 的设置侧栏是纯文字列表（无图标），无消费者的字段不进契约。
/// </summary>
public sealed record SettingsPageDescriptor
{
    /// <summary>页面标识（侧栏选中态的记忆键；v3.3.0 没有记忆，是重写要补的）。</summary>
    /// <remarks>消费者：设置窗（记住上次停在哪个页）</remarks>
    public required string Id { get; init; }

    /// <summary>侧栏显示文字（"倒计时"/"课表"/"远程"…）。</summary>
    /// <remarks>消费者：设置窗（侧栏）</remarks>
    public required string DisplayName { get; init; }

    /// <summary>排序权重，数值小的在上。</summary>
    /// <remarks>消费者：设置窗（侧栏排序）</remarks>
    public required int Order { get; init; }

    /// <summary>创建页面实例。**每次点击都新建**，避免 Load/Apply 状态残留。</summary>
    /// <remarks>消费者：设置窗（切换到该页时调用）</remarks>
    public required Func<Control> CreatePage { get; init; }
}
