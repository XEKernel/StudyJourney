using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace StudyJourney.Avalonia;

internal static class Program
{
    private const string MutexName = "GaokaoCountdown_SingleInstance_XEKernel";

    // Avalonia 入口（与 WPF 的 App.xaml 不同，Avalonia 从 Main 启动）
    [STAThread]
    public static void Main(string[] args)
    {
        // ── 单实例 Mutex（对齐 WPF App.xaml.cs）────────────────
        bool createdNew = false;
        try { _ = new Mutex(true, MutexName, out createdNew); }
        catch (AbandonedMutexException) { createdNew = true; }

        if (!createdNew)
        {
            // 已有实例在运行：激活其主窗口并退出
            ActivateExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>激活已有实例（FindWindow 按标题"学程"查找，兼容托盘隐藏状态）</summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var hwnd = FindWindow(null, "学程");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, 9);          // SW_RESTORE
                SetForegroundWindow(hwnd);
            }
        }
        catch { /* 激活失败静默 */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
