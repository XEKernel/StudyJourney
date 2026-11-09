using System.Runtime.InteropServices;
using Avalonia.Input;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 全局热键的真实绑定层：一个**隐藏的 Win32 消息窗口** + <c>RegisterHotKey</c> / <c>WM_HOTKEY</c>。
///
/// 为什么不用 Avalonia 的窗口句柄：插件的热键要在主窗隐藏（灵动岛"点了就藏"）时仍然工作，
/// 隐藏窗口与 Avalonia 窗口生命周期完全解耦——这与 v3.3.0 的 <c>GlobalHotKeyManager</c> 同一做法，
/// 只是去掉了静态状态（<c>PITFALLS §E</c>：静态状态让单元测试没法并行、也没法"两个实例互不干扰"）。
///
/// ⚠ 仅 Windows：<c>DllImport</c> 直接写死 user32/kernel32（CORE_ARCHITECTURE §3 第 5 条"仅 Windows"）。
/// ⚠ <c>_wndProc</c> 必须保持**字段引用**，否则 GC 回收委托后窗口过程变成野指针（旧版注释同此）。
/// </summary>
public sealed class Win32HotKeyBinder : IHotKeyBinder
{
    private const uint WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary><c>RegisterClassW</c> 的"类已存在"错误码：不是失败，继续建窗口即可。</summary>
    private const int ErrorClassAlreadyExists = 1410;

    private readonly object _gate = new();
    private readonly Dictionary<int, Action> _actions = [];
    private WndProcDelegate? _wndProc;
    private IntPtr _hwnd;
    private int _nextId = 1;

    /// <summary>隐藏消息窗口是否已建好（诊断用；不建也允许 Bind 失败而不是崩溃）。</summary>
    public bool IsReady
    {
        get { lock (_gate) return _hwnd != IntPtr.Zero; }
    }

    /// <inheritdoc />
    public Result<IDisposable> Bind(HotKeyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var virtualKey = ToVirtualKey(descriptor.Key);
        if (virtualKey == 0)
        {
            return Result<IDisposable>.Fail(ErrorCodes.HotKeyUnsupported,
                $"热键主键 {descriptor.Key} 不受支持（只支持字母/数字/F1–F12/方向键/Space/Enter/Esc/Tab）");
        }

        lock (_gate)
        {
            if (!EnsureWindow())
                return Result<IDisposable>.Fail(ErrorCodes.HotKeyTaken, "无法创建热键消息窗口（Win32 初始化失败）");

            var id = _nextId;
            if (!RegisterHotKey(_hwnd, id, ToModifierFlags(descriptor.Modifiers), virtualKey))
            {
                // A1-4：被别的程序占用必须让用户看见，不静默失败。
                var error = Marshal.GetLastWin32Error();
                return Result<IDisposable>.Fail(ErrorCodes.HotKeyTaken,
                    error == 1409
                        ? $"热键 {descriptor.Key} 的组合已被其它程序占用（编号 {id}）"
                        : $"注册热键 {descriptor.Key} 失败（Win32 错误 {error}，编号 {id}）");
            }

            _nextId++;
            _actions[id] = descriptor.Handler;

            return Result<IDisposable>.Ok(new Registration(() => Release(id)));
        }
    }

    /// <inheritdoc />
    public void UnbindAll()
    {
        lock (_gate)
        {
            if (_hwnd == IntPtr.Zero)
                return;

            foreach (var id in _actions.Keys.ToList())
                UnregisterHotKey(_hwnd, id);

            _actions.Clear();
            _nextId = 1;
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void Release(int id)
    {
        lock (_gate)
        {
            if (!_actions.Remove(id))
                return;

            if (_hwnd != IntPtr.Zero)
                UnregisterHotKey(_hwnd, id);
        }
    }

    /// <summary>建隐藏消息窗口（幂等）。失败**不置位**，允许下次重试。</summary>
    private bool EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero)
            return true;

        try
        {
            _wndProc ??= WndProc;

            var windowClass = new WndClass
            {
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                HInstance = GetModuleHandleW(null),
                LpszClassName = "StudyJourneyHotKeyWindow",
            };

            // 上次部分失败（类已注册但窗口没建成）时重试：1410 不是失败。
            if (RegisterClassW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
                return false;

            _hwnd = CreateWindowExW(0, windowClass.LpszClassName, "StudyJourneyHotKeyWindow", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.HInstance, IntPtr.Zero);

            return _hwnd != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 非 Windows 平台：热键整体降级（返回 false，由 Bind 转成 hotkey-taken）
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmHotKey)
            return DefWindowProcW(hWnd, msg, wParam, lParam);

        Action? action;
        lock (_gate)
            _actions.TryGetValue(wParam.ToInt32(), out action);

        if (action is null)
            return IntPtr.Zero;

        // 插件回调抛异常绝不能打死消息循环（否则后续热键全部失效，且用户看不到原因）。
        try { action(); }
        catch { /* 由插件自己的日志记录；壳层不再重复上报 */ }

        return IntPtr.Zero;
    }

    /// <summary><c>HotKeyModifiers</c> → Win32 <c>fsModifiers</c>。</summary>
    private static uint ToModifierFlags(HotKeyModifiers modifiers)
    {
        var flags = 0u;

        if (modifiers.HasFlag(HotKeyModifiers.Alt))
            flags |= ModAlt;
        if (modifiers.HasFlag(HotKeyModifiers.Control))
            flags |= ModControl;
        if (modifiers.HasFlag(HotKeyModifiers.Shift))
            flags |= ModShift;
        if (modifiers.HasFlag(HotKeyModifiers.Win))
            flags |= ModWin;

        return flags;
    }

    /// <summary>
    /// Avalonia <see cref="Key"/> → Win32 虚拟键码。**只映射固定热键需要的那一批**
    /// （字母/数字/F1–F12/方向键/常用控制键）；返回 0 表示不支持。
    ///
    /// ⚠ Avalonia 的 <c>Key</c> 枚举值是平台无关的自定义序号，**不等于 VK 码**
    /// （<c>Key.A</c> 是 44，VK_A 是 0x41）—— 直接强转会让所有热键都注册错键。
    /// </summary>
    internal static uint ToVirtualKey(Key key)
    {
        var code = (int)key;

        if (code >= (int)Key.A && code <= (int)Key.Z)
            return (uint)('A' + code - (int)Key.A);

        if (code >= (int)Key.D0 && code <= (int)Key.D9)
            return (uint)('0' + code - (int)Key.D0);

        if (code >= (int)Key.F1 && code <= (int)Key.F12)
            return (uint)(0x70 + code - (int)Key.F1);

        return key switch
        {
            Key.Space => 0x20,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Tab => 0x09,
            Key.Back => 0x08,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            _ => 0,
        };
    }

    // ── Win32 ──────────────────────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string LpszClassName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WndClass lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private sealed class Registration(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
