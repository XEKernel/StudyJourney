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

    /// <summary>当前前台窗口句柄（无则 IntPtr.Zero）</summary>
    public static IntPtr ForegroundWindow => GetForegroundWindow();

    /// <summary>判断句柄是否为桌面窗口（Progman / WorkerW，即"纯桌面"状态）</summary>
    public static bool IsDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        return cls == "Progman" || cls == "WorkerW";
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
