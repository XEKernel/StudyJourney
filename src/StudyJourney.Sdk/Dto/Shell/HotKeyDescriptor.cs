using Avalonia.Input;

namespace StudyJourney.Sdk.Dto;

/// <summary>热键修饰键（与 <c>RegisterHotKey</c> 的 flags 语义一致）。</summary>
[Flags]
public enum HotKeyModifiers
{
    /// <summary>无修饰键。</summary>
    None = 0,

    /// <summary>Alt。</summary>
    Alt = 1,

    /// <summary>Ctrl。</summary>
    Control = 2,

    /// <summary>Shift。</summary>
    Shift = 4,

    /// <summary>Win 键。</summary>
    Win = 8,
}

/// <summary>
/// 全局热键描述。<see cref="IShellService.RegisterHotKey"/> 的参数。
///
/// 注册失败返回 hotkey-taken / hotkey-limit（运行期可预期，必须可恢复），不抛异常。
/// </summary>
public sealed record HotKeyDescriptor
{
    /// <summary>热键标识（用于冲突仲裁与注销；同一 Id 重复注册视为占用）。</summary>
    /// <remarks>消费者：HotKeyService（字典键 / 取消注册）、插件自身（StopAsync 释放）</remarks>
    public required string Id { get; init; }

    /// <summary>修饰键组合。</summary>
    /// <remarks>消费者：HotKeyService（RegisterHotKey 的 fsModifiers）</remarks>
    public required HotKeyModifiers Modifiers { get; init; }

    /// <summary>主键。</summary>
    /// <remarks>消费者：HotKeyService（RegisterHotKey 的 vk）</remarks>
    public required Key Key { get; init; }

    /// <summary>按下时的回调（在 UI 线程执行）。</summary>
    /// <remarks>消费者：HotKeyService（WM_HOTKEY 派发）</remarks>
    public required Action Handler { get; init; }
}
