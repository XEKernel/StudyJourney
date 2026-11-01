namespace StudyJourney.Sdk.Dto;

/// <summary>主菜单项描述。<see cref="IShellService.RegisterMenuItem"/> 的参数。</summary>
public sealed record MenuItemDescriptor
{
    /// <summary>唯一标识（重复 → 抛 ArgumentException，属编程错误）。</summary>
    /// <remarks>消费者：ShellHost（去重）、插件自身（StopAsync 释放）</remarks>
    public required string Id { get; init; }

    /// <summary>显示文字（"设置…"/"成绩分析"）。</summary>
    /// <remarks>消费者：ShellHost（菜单渲染）</remarks>
    public required string Header { get; init; }

    /// <summary>排序权重，数值小的在上。</summary>
    /// <remarks>消费者：ShellHost（菜单排序）</remarks>
    public required int Order { get; init; }

    /// <summary>点击回调（在 UI 线程执行）。</summary>
    /// <remarks>消费者：ShellHost（点击派发）</remarks>
    public required Action Invoke { get; init; }
}

/// <summary>托盘菜单项描述。<see cref="IShellService.RegisterTrayItem"/> 的参数。</summary>
public sealed record TrayItemDescriptor
{
    /// <summary>唯一标识（重复 → 抛 ArgumentException）。</summary>
    /// <remarks>消费者：ShellHost（去重）</remarks>
    public required string Id { get; init; }

    /// <summary>显示文字（"显示/隐藏"/"生成诊断包"）。</summary>
    /// <remarks>消费者：ShellHost（托盘菜单渲染）</remarks>
    public required string Header { get; init; }

    /// <summary>排序权重。</summary>
    /// <remarks>消费者：ShellHost（托盘菜单排序）</remarks>
    public required int Order { get; init; }

    /// <summary>点击回调（在 UI 线程执行）。</summary>
    /// <remarks>消费者：ShellHost（点击派发）</remarks>
    public required Action Invoke { get; init; }
}
