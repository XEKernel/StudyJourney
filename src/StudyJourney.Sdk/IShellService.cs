using Avalonia.Controls;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Sdk;

/// <summary>
/// 主窗壳对插件暴露的能力（CORE_ARCHITECTURE §2.5）。
///
/// "壳不知道插件存在"是微内核的前提——插件只能通过本接口往壳上"挂东西"，
/// 不能直接触碰 <c>MainWindow</c> 之类。
///
/// 失败语义的区分：
/// <list type="bullet">
/// <item>可失败（<see cref="Result"/>）：热键冲突、槽位已占用——**运行期可预期**，必须可恢复。</item>
/// <item>抛异常：菜单/托盘 Id 重复——**编程错误**，应当立刻在开发期炸掉。</item>
/// </list>
/// </summary>
public interface IShellService
{
    /// <summary>挂一个主菜单项。返回的 IDisposable 释放即移除。</summary>
    /// <exception cref="ArgumentException">Id 重复（编程错误）。</exception>
    /// <remarks>消费者：倒计时（"设置"入口）、成绩（"成绩分析"入口）</remarks>
    IDisposable RegisterMenuItem(MenuItemDescriptor item);

    /// <summary>挂一个托盘菜单项。</summary>
    /// <exception cref="ArgumentException">Id 重复。</exception>
    /// <remarks>消费者：倒计时（显示/隐藏）、诊断（生成诊断包）</remarks>
    IDisposable RegisterTrayItem(TrayItemDescriptor item);

    /// <summary>注册全局热键。组合已被占用 → hotkey-taken；编号超限 → hotkey-limit。</summary>
    /// <remarks>消费者：倒计时（Ctrl+Shift+H）、白板、考试模式（M1 起逐个接）</remarks>
    Result<IDisposable> RegisterHotKey(HotKeyDescriptor descriptor);

    /// <summary>
    /// 把内容挂进主窗的**固定槽位**（§1.2.9，一槽一内容）。
    /// 槽位已占用 → slot-occupied（不覆盖）；未知 ID → slot-unknown。
    /// </summary>
    /// <remarks>消费者：倒计时（main.primary）、课表（main.secondary）、自动化（main.corner）</remarks>
    Result<IDisposable> RegisterSlotContent(string slotId, Control content);

    /// <summary>显示一个窗口（内部走窗口外框：暗色标题栏、圆角、无边框）。</summary>
    /// <remarks>消费者：成绩分析、PDF 阅读器、课表编辑器</remarks>
    void ShowWindow(Window window);

    /// <summary>模态显示一个窗口，返回其结果。</summary>
    /// <remarks>消费者：设置窗、颜色选择、危险操作确认以外的自定义对话框</remarks>
    Task<TResult?> ShowDialogAsync<TResult>(Window dialog);

    /// <summary>把动作投递到 UI 线程。后台线程要碰 UI 时的唯一正确方式。</summary>
    /// <remarks>消费者：提醒（Tick 回调）、自动化（后台触发）、远程（HTTP 线程）</remarks>
    void PostToUi(Action action);

    /// <summary>当前主题色板（插件取色用，不许自己存 <c>#FF...</c>）。</summary>
    /// <remarks>消费者：全部 UI 插件</remarks>
    ThemeTokens CurrentTheme { get; }
}
