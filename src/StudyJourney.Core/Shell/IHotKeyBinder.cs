using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 全局热键的**绑定层**抽象：把 <see cref="HotKeyDescriptor"/> 变成一根真实的系统热键。
///
/// 拆出这一层是为了让 <see cref="ShellService"/> 的冲突判定（<c>hotkey-taken</c> /
/// <c>hotkey-limit</c>）**能在无 GUI 的测试里断言**——真实实现见 <see cref="Win32HotKeyBinder"/>，
/// 它需要 Win32 隐藏消息窗，CI 里既建不出来也收不到 <c>WM_HOTKEY</c>。
/// </summary>
public interface IHotKeyBinder
{
    /// <summary>
    /// 绑定一根热键。成功返回"解除绑定"句柄。
    /// 组合已被**系统或其它进程**占用 → <c>hotkey-taken</c>（不得静默失败，A1-4）。
    /// </summary>
    Result<IDisposable> Bind(HotKeyDescriptor descriptor);

    /// <summary>解除全部绑定（退出时调用，幂等）。</summary>
    void UnbindAll();
}
