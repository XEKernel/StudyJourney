using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace GaokaoCountdown
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private const string MutexName = "GaokaoCountdown_SingleInstance_XEKernel";

        // ── Win32：激活已有实例 ──────────────────────────────
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        // ── Win32：全局快捷键 ──────────────────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int SW_RESTORE = 9;
        private const uint MOD_CTRL_SHIFT = 0x0002 | 0x0004; // MOD_CONTROL | MOD_SHIFT
        private const int HOTKEY_TOGGLE_MAIN  = 1;
        private const int HOTKEY_TOGGLE_BAR   = 2;
        private const int HOTKEY_EXAM_MODE    = 3;

        private HwndSource? _hwndSource;

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── 尝试获取 Mutex ────────────────────────────────
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // 已有实例在运行：激活其窗口并退出
                IntPtr hWnd = FindWindow(null, "学程");
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }
                // 即使找不到窗口（托盘隐藏状态）也直接退出
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // ── 手动创建主窗口 + 注册全局快捷键 ─────────────────
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            // 延迟注册快捷键（等待窗口 HWND 就绪）
            mainWindow.Loaded += (_, _) =>
            {
                var helper = new WindowInteropHelper(mainWindow);
                var hwnd = helper.Handle;
                RegisterHotKey(hwnd, HOTKEY_TOGGLE_MAIN, MOD_CTRL_SHIFT, 0x48); // H
                RegisterHotKey(hwnd, HOTKEY_TOGGLE_BAR,  MOD_CTRL_SHIFT, 0x42); // B
                RegisterHotKey(hwnd, HOTKEY_EXAM_MODE,   MOD_CTRL_SHIFT, 0x45); // E

                _hwndSource = HwndSource.FromHwnd(hwnd);
                _hwndSource?.AddHook(WndProc);
            };

            mainWindow.Closed += (_, _) =>
            {
                var helper = new WindowInteropHelper(mainWindow);
                UnregisterHotKey(helper.Handle, HOTKEY_TOGGLE_MAIN);
                UnregisterHotKey(helper.Handle, HOTKEY_TOGGLE_BAR);
                UnregisterHotKey(helper.Handle, HOTKEY_EXAM_MODE);
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && MainWindow is MainWindow mw)
            {
                switch (wParam.ToInt32())
                {
                    case HOTKEY_TOGGLE_MAIN: mw.ToggleVisibility(); handled = true; break;
                    case HOTKEY_TOGGLE_BAR:  mw.ToggleScheduleBarViaHotkey(); handled = true; break;
                    case HOTKEY_EXAM_MODE:   mw.EnterExamMode(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
