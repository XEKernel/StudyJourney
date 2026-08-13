using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 窗口层级辅助：桌面小组件「桌面同一层」行为。
/// 提供前台窗口检测（是否桌面 / 是否最大化），供桌面小组件判断显隐。
/// </summary>
public static class WindowLayerHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public int ptMinPositionX;
        public int ptMinPositionY;
        public int ptMaxPositionX;
        public int ptMaxPositionY;
        public int rcNormalLeft;
        public int rcNormalTop;
        public int rcNormalRight;
        public int rcNormalBottom;
    }

    private const int SW_SHOWMAXIMIZED = 3;
    private const int SW_SHOWMINIMIZED = 2;

    /// <summary>当前前台窗口句柄（无则 IntPtr.Zero）</summary>
    public static IntPtr ForegroundWindow => GetForegroundWindow();

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>判断句柄是否为桌面/系统外壳窗口（Progman / WorkerW / 任务栏 Shell_TrayWnd）</summary>
    public static bool IsDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        var cls = GetWindowClass(hwnd);
        return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd";
    }

    /// <summary>判断句柄是否为系统外壳窗口（桌面/任务栏/开始菜单等不遮挡小组件的窗口）</summary>
    public static bool IsSystemShell(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        var cls = GetWindowClass(hwnd);
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>判断窗口是否处于最小化状态（所有窗口最小化时，前台可能是最后一个最小化窗口）</summary>
    public static bool IsMinimized(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return false;
        return placement.showCmd == SW_SHOWMINIMIZED;
    }

    /// <summary>判断前台窗口是否最大化</summary>
    public static bool IsForegroundMaximized()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return false;
        return placement.showCmd == SW_SHOWMAXIMIZED;
    }
}
