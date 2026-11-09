using System.Runtime.InteropServices;

namespace StudyJourney.App;

/// <summary>
/// Windows 系统通知（托盘气泡）：基于 <c>Shell_NotifyIcon</c>。
/// Avalonia 12 移除了 <c>TrayIcon.ShowNotification</c>，用 Win32 补回系统级通知能力。
///
/// 通过独立的 message-only 窗口 + 独立条目发气泡，气泡显示数秒后自动移除条目，
/// 避免托盘区常驻第二个图标。
///
/// ⚠ 迁自 v3.3.0 <c>Helpers/SystemToast.cs</c>（行为逐字保留，含 #17 代际计数修复：
/// 两条提醒间隔 &lt;6 秒时，旧任务的延迟删除不能把新气泡一起删掉）。
/// </summary>
internal static class SystemToast
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint IMAGE_ICON = 1;
    private const uint LR_DEFAULTCOLOR = 0x0000;

    private static IntPtr _hwnd;
    private static IntPtr _icon;
    private static bool _added;
    private static int _generation;   // 通知代际计数（防旧延迟删除任务误删新气泡）

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconDataW
    {
        public int CbSize;
        public IntPtr HWnd;
        public uint Uid;
        public uint UFlags;
        public uint UCallbackMessage;
        public IntPtr HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;
        public int DwState;
        public int DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;
        public uint DwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconDataW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string? lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    /// <summary>显示托盘气泡通知（标题 + 内容），数秒后自动移除。</summary>
    public static void Show(string title, string message)
    {
        try
        {
            if (_hwnd == IntPtr.Zero)
            {
                // message-only 窗口：仅作 Shell_NotifyIcon 的归属窗口，不显示、无回调
                _hwnd = CreateWindowExW(0, "Message", null, 0, 0, 0, 0, 0,
                    new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }

            if (_hwnd == IntPtr.Zero)
                return;

            if (_icon == IntPtr.Zero)
            {
                // 从可执行文件提取应用图标（#1 = 第一个图标资源）
                _icon = LoadImageW(GetModuleHandleW(null), "#1", IMAGE_ICON, 32, 32, LR_DEFAULTCOLOR);
            }

            var data = new NotifyIconDataW
            {
                CbSize = Marshal.SizeOf<NotifyIconDataW>(),
                HWnd = _hwnd,
                Uid = 1,
                HIcon = _icon,
                SzTip = "学程",
                SzInfoTitle = title ?? string.Empty,
                SzInfo = message ?? string.Empty,
                DwInfoFlags = 0,   // NIIF_NONE
                UFlags = NIF_ICON | NIF_TIP | NIF_INFO,
            };

            if (!_added)
                _added = Shell_NotifyIconW(NIM_ADD, ref data);

            Shell_NotifyIconW(NIM_MODIFY, ref data);

            // 代际计数：两条提醒间隔 <6 秒时，第一条的延迟删除任务
            // 会把第二条气泡的托盘条目一并删掉。现仅当代际仍是最新时才执行删除，
            // 旧任务的删除责任移交给最新一条。
            var generation = ++_generation;

            _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ =>
            {
                if (!_added || generation != _generation)
                    return;   // 已有更新的通知，由它负责生命周期

                _added = false;

                var delete = new NotifyIconDataW
                {
                    CbSize = Marshal.SizeOf<NotifyIconDataW>(),
                    HWnd = _hwnd,
                    Uid = 1,
                };

                Shell_NotifyIconW(NIM_DELETE, ref delete);
            });
        }
        catch
        {
            // 系统通知失败静默（如托盘不可用）：通知不是关键路径。
        }
    }
}
