using Avalonia.Controls;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// **宿主反向接口**：核心的 <see cref="ShellService"/> 通过它把"需要窗口系统才能做的事"交给 App。
///
/// 为什么这个接口在 <c>Core</c> 而不是 <c>Sdk</c>：插件只引用 <c>Sdk</c>，看不见 <c>Core</c>，
/// 因此它**不构成"第 9 个能力接口"**。若放在 <c>Sdk</c>，插件就能拿到"往壳里塞任意内容"的后门，
/// E1「8 接口一个不多」当场失效。
///
/// 职责边界（判据来自 CORE_ARCHITECTURE §1.3）：
/// <list type="bullet">
/// <item><b>归本接口（App）</b>：窗口创建与销毁、托盘图标、对话框、窗口外框、主题资源字典——
///       这些都要"知道窗口在哪"，属生命周期而非能力。</item>
/// <item><b>归 <see cref="ShellService"/>（Core）</b>：菜单/托盘/槽位/热键的**注册表与失败语义**——
///       这些是纯数据 + 冲突判定，不需要窗口。</item>
/// </list>
///
/// ⚠ <b>全部方法都在 UI 线程调用</b>：<see cref="ShellService"/> 自己保证这一点（<c>PostToUi</c> 转发）。
/// </summary>
public interface IUiHost
{
    /// <summary>菜单项集合变了（新增/移除）。宿主按 <c>Order</c> 重排后重绘。空列表 = 全部清空。</summary>
    void OnMenuItemsChanged(IReadOnlyList<MenuItemDescriptor> items);

    /// <summary>托盘项集合变了。宿主按 <c>Order</c> 重排后重绘。</summary>
    void OnTrayItemsChanged(IReadOnlyList<TrayItemDescriptor> items);

    /// <summary>某个槽位的内容变了；<paramref name="content"/> 为 <c>null</c> 表示清空（宿主应隐藏该块）。</summary>
    void OnSlotContentChanged(string slotId, Control? content);

    /// <summary>显示一个窗口（走窗口外框：暗色标题栏、圆角）。</summary>
    void ShowWindow(Window window);

    /// <summary>模态显示一个窗口并返回结果。</summary>
    Task<TResult?> ShowDialogAsync<TResult>(Window dialog);

    /// <summary>把动作投递到 UI 线程。</summary>
    void PostToUi(Action action);

    /// <summary>当前主题色板。</summary>
    ThemeTokens CurrentTheme { get; }
}
