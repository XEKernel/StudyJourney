using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace GaokaoCountdown
{
    public partial class App : Application
    {
        // ── 防多开：命名 Mutex（每个用户独立） ────────────────
        private static Mutex? _mutex;
        private const string MutexName = "GaokaoCountdown_SingleInstance_XEKernel";

        // ── Win32：激活已有实例窗口 ──────────────────────────
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        private const int SW_RESTORE = 9;

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

            // ── 手动创建主窗口 ────────────────────────────────
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
