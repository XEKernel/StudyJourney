using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 顶层窗口枚举工具（2.5.8 顺序记忆 / 2.5.9 连堂幂等的共用地基）。
///
/// 用途：
///  1. 判断某个文件"当前是否已经被打开"——Office/WPS/PDF 阅读器的窗口标题通常含文件名，
///     这是唯一能覆盖"老师自己双击打开"的可靠信号（独占句柄检测不可用：阅读器普遍不独占）。
///  2. 供「关闭软件」动作列出当前有窗口的运行中程序（老师点选即可，不用记 exe 名字）。
///
/// 性能：EnumWindows 遍历很快（毫秒级），但 Process.GetProcesses 较重，
/// 因此运行中程序列表只在老师打开编辑器面板时按需刷新，不放进每秒轮询。
/// </summary>
public static class WindowEnumerator
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    /// <summary>一个可见的顶层窗口</summary>
    public readonly record struct WindowInfo(IntPtr Handle, uint Pid, string Title);

    /// <summary>枚举所有可见且有标题的顶层窗口（毫秒级，可安全放进周期轮询）</summary>
    public static List<WindowInfo> TopLevelWindows()
    {
        var list = new List<WindowInfo>();
        try
        {
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    int len = GetWindowTextLength(hwnd);
                    if (len <= 0) return true;
                    var sb = new StringBuilder(len + 2);
                    if (GetWindowText(hwnd, sb, sb.Capacity) <= 0) return true;
                    var title = sb.ToString().Trim();
                    if (title.Length == 0) return true;
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    list.Add(new WindowInfo(hwnd, pid, title));
                }
                catch { /* 单个窗口取信息失败不影响整体枚举 */ }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }

    /// <summary>
    /// 文件是否正被某个窗口打开（按"窗口标题含该文件名"判断）。
    /// 传完整路径或纯文件名都可；比对用"文件名+扩展名"，避免仅标题含课程名的误判。
    /// </summary>
    public static bool IsFileOpenInWindow(string filePath, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(name)) return false;

        foreach (var w in TopLevelWindows())
        {
            if (w.Title.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                handle = w.Handle;
                return true;
            }
        }
        return false;
    }

    /// <summary>在已打开窗口里找出"标题命中了给定文件集合"的那一份（用于顺序记忆捕获老师手动打开的文件）</summary>
    public static string? FindOpenFileFrom(IEnumerable<string> candidateFiles)
    {
        var candidates = candidateFiles.ToList();
        if (candidates.Count == 0) return null;
        var windows = TopLevelWindows();
        if (windows.Count == 0) return null;

        // 全部标题拼成一个大串，一次 Contains 即完成"窗口标题是否含某文件名"的判断：
        // 原实现在候选文件 × 窗口数 上做线性扫描（O(n·m)），课件目录大 + 窗口多时开销明显。
        var blob = BuildTitleBlob(windows);

        foreach (var file in candidates)
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (blob.Contains(name, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    /// <summary>把一批窗口标题拼成单个字符串（用 \n 分隔，避免跨标题误匹配），供批量 Contains 判定</summary>
    public static string BuildTitleBlob(IEnumerable<WindowInfo> windows)
    {
        var sb = new StringBuilder();
        foreach (var w in windows)
            sb.Append(w.Title).Append('\n');
        return sb.ToString();
    }

    /// <summary>批量判定：在给定标题串里，哪些文件"正被打开"（返回首个命中的文件路径）</summary>
    public static string? FindOpenFileInBlob(string titleBlob, IEnumerable<string> candidateFiles)
    {
        if (string.IsNullOrEmpty(titleBlob)) return null;
        foreach (var file in candidateFiles)
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (titleBlob.Contains(name, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    /// <summary>把某个已打开文件的窗口切到前台（连堂"已打开→激活"用）</summary>
    public static bool TryActivateWindow(string filePath)
    {
        if (!IsFileOpenInWindow(filePath, out var hwnd) || hwnd == IntPtr.Zero) return false;
        try
        {
            ShowWindow(hwnd, SW_RESTORE);
            return SetForegroundWindow(hwnd);
        }
        catch { return false; }
    }

    /// <summary>有可见窗口的运行中程序（进程名去重）。供「关闭软件」动作下拉选择与教师端"指定启动软件"，
    /// Exe 为进程名（如 POWERPNT.exe），Path 为可执行文件完整路径（取不到时为空串，受权限限制）</summary>
    public static List<(string Exe, string Title, string Path)> RunningApps()
    {
        var result = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    var title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var name = p.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // 跳过程序自身与系统外壳，避免老师误选把学程自己关掉
                    if (name.Equals("StudyJourney.Avalonia", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(name)) continue;

                    string full = "";
                    try { full = p.MainModule?.FileName ?? ""; } catch { /* 32/64 位或权限限制 → 留空 */ }
                    result.Add((name + ".exe", title, full));
                }
                catch { /* 进程已退出/无权限访问 → 跳过 */ }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }
        return result.OrderBy(r => r.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
