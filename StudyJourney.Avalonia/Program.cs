using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace StudyJourney.Avalonia;

internal static class Program
{
    private const string MutexName = "GaokaoCountdown_SingleInstance_XEKernel";

    // 必须持有 Mutex 引用，防止 GC 回收后触发 finalizer 释放互斥体（导致单实例失效）
    private static Mutex? _mutex;

    // Avalonia 入口（与 WPF 的 App.xaml 不同，Avalonia 从 Main 启动）
    [STAThread]
    public static void Main(string[] args)
    {
        // ── 单实例 Mutex（对齐 WPF App.xaml.cs）────────────────
        bool createdNew = false;
        try { _mutex = new Mutex(true, MutexName, out createdNew); }
        catch (AbandonedMutexException)
        {
            // 前一个实例异常退出：互斥体已被放弃，重新获取
            createdNew = true;
            _mutex = new Mutex(true, MutexName, out _);
        }

        if (!createdNew)
        {
            // 已有实例在运行：激活其主窗口并退出
            ActivateExistingInstance();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // 进程退出时系统会自动释放，此处仅优雅收尾
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
        }
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
