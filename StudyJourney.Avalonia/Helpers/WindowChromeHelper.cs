using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 窗口外框（标题栏）配色修正 —— 专治 **Windows 10 下子窗口顶部的亮色白条**。
///
/// <para>
/// 背景（2026-10-12 用户反馈："除了主窗口，其它窗口的标题栏都是 win32 样式的，顶部一条白条
/// 和暗色内容冲突；Windows 11 下不受影响"）：
/// 主窗口是 <c>WindowDecorations="None"</c>（自绘标题栏）所以一直是对的；
/// 课表编辑器 / 成绩分析 / PDF / 听力 / 各对话框走**系统标题栏**，颜色由 DWM 决定。
/// </para>
///
/// <para>
/// **根因（外部权威：Avalonia 源码）** —— <c>Avalonia.Win32/WindowImpl.SetFrameThemeVariant</c>：
/// <code>
/// if (Win32Platform.WindowsVersion.Build &gt;= 22000)   // ← 只在 Windows 11 才设
///     DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0, ...);
/// </code>
/// 也就是说 **Windows 10（build &lt; 22000）Avalonia 从不设这一项** → 标题栏跟随系统配色 → 白条；
/// Windows 11 由 Avalonia 自己设 → 所以"Win11 不受影响"。本应用主题恒为 Dark
/// （<c>App.axaml</c> 的 <c>RequestedThemeVariant="Dark"</c>，无运行时切换），因此这里可以**无条件**补设。
/// </para>
///
/// <para>
/// ⚠ **属性号在 Win10 上有两个版本**，用错不会抛异常、只会静默无效（返回 E_INVALIDARG）：
/// <list type="bullet">
///   <item>build ≥ 18985（20H1 及以后，含 22H2）→ <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> = <b>20</b></item>
///   <item>build 17763 ~ 18363（1809 / 1903）→ 同名的常量取值是 <b>19</b></item>
/// </list>
/// 所以这里"先试 20、失败再试 19"，并**用返回值判断**；都不行就静默放弃
/// （老系统上宁可保持原本的亮色标题栏，也绝不能因为画个边框把应用搞崩）。
/// </para>
///
/// <para>
/// 为什么不改成"把子窗口全部换成自绘标题栏"：那样拖拽、最小化/最大化/关闭、贴边吸附、
/// 双击最大化、系统菜单全得自己实现（还要处理多屏 DPI），改动面远大于一条白条本身的价值。
/// </para>
/// </summary>
public static class WindowChromeHelper
{
    /// <summary>20H1 及以后的属性号</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;
    /// <summary>Win10 1809 / 1903 的同名属性号（取值不同）</summary>
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static bool _hooked;

    /// <summary>
    /// 成功套上暗色标题栏的次数。
    /// 自检据此确认**自动挂钩真的触发了**（只断言"手工调用能成功"抓不到"钩子根本没接上"）。
    /// </summary>
    public static int AppliedCount { get; private set; }

    /// <summary>
    /// 给窗口套上暗色标题栏。重复调用无害（同一个值写两次）。
    /// </summary>
    /// <returns>
    /// 系统是否接受了该属性 —— 非 Windows / 拿不到窗口句柄（窗口还没创建平台对象）/
    /// 系统太老（两个属性号都不认）→ false。
    /// </returns>
    public static bool ApplyDarkTitleBar(TopLevel topLevel)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var handle = topLevel.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero) return false;

            int dark = 1;   // BOOL：1 = 用暗色，0 = 用亮色
            int hr = DwmSetWindowAttribute(handle.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            if (hr != 0)    // 老的 Win10（1809 / 1903）用属性号 19，不重试就永远是白条
                hr = DwmSetWindowAttribute(handle.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));

            if (hr == 0) AppliedCount++;
            else AppLogger.Warn($"暗色标题栏未能应用（DWM 返回 0x{hr:X8}），该窗口标题栏将保持系统配色");
            return hr == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>
    /// 注册全局钩子：**每个窗口一打开**就套暗色标题栏。
    /// 启动时调一次即可（重复调用只生效一次）。
    ///
    /// 用 <c>WindowOpenedEvent</c> 的类处理器而不是"逐个窗口在构造函数里调"：
    /// 以后新增窗口不可能漏（漏一个就是老师看到又一条白条，而且很难被自检发现）。
    /// </summary>
    public static void HookAllWindows()
    {
        if (_hooked) return;
        _hooked = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => ApplyDarkTitleBar(window));
    }
}
