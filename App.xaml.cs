using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using GaokaoCountdown.Views;

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
            // 启动日志（文件 + Debug）
            Helpers.AppLogger.EnableFileLogging();
            Helpers.AppLogger.Info("学程启动");

            // ── 尝试获取 Mutex（前实例异常退出时会抛 AbandonedMutexException）──
            bool createdNew = false;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // 前一个实例异常退出：互斥体已被放弃，重新获取
                createdNew = true;
                _mutex = new Mutex(true, MutexName, out _);
            }

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
                if (hwnd == IntPtr.Zero) return;

                // 检查注册结果：快捷键被其他程序占用时给出提示，避免静默失败
                if (!RegisterHotKey(hwnd, HOTKEY_TOGGLE_MAIN, MOD_CTRL_SHIFT, 0x48)) // H
                    Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+H 注册失败（可能被其他程序占用）");
                if (!RegisterHotKey(hwnd, HOTKEY_TOGGLE_BAR,  MOD_CTRL_SHIFT, 0x42)) // B
                    Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+B 注册失败（可能被其他程序占用）");
                if (!RegisterHotKey(hwnd, HOTKEY_EXAM_MODE,   MOD_CTRL_SHIFT, 0x45)) // E
                    Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+E 注册失败（可能被其他程序占用）");

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
            Helpers.AppLogger.Info("学程退出");
            // Mutex 所有权与创建线程绑定；若 OnExit 在非创建线程执行，ReleaseMutex 会抛
            // ApplicationException（进程退出时系统会自动释放，此处仅优雅收尾）
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch { }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
