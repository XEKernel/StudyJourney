using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia;

public partial class App : Application
{
    /// <summary>全局设置（从 settings.json 加载，与 WPF 版共用同一配置）</summary>
    public static AppSettings Settings { get; set; } = new();

    /// <summary>全局课表管理器（从 schedule.json 加载）</summary>
    public static ScheduleManager Schedule { get; private set; } = new();

    /// <summary>全局提醒服务（上课/下课/60 秒倒计时）</summary>
    public static ReminderService? Reminders { get; private set; }

    /// <summary>设置被保存后触发（主窗口/悬浮栏等订阅并刷新）</summary>
    public static event Action? SettingsChanged;

    /// <summary>保存设置并广播变更</summary>
    public static void SaveSettings()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayScheduleItem;
    private Window? _mainWindow;

    /// <summary>应用图标（各窗口标题栏/任务栏共用，从 avares 加载）</summary>
    public static WindowIcon? AppIcon { get; private set; }

    // ── 全局快捷键 ID（与 WPF 版一致）────────────────────────
    private const int HotKeyToggleMain = 1;   // Ctrl+Shift+H
    private const int HotKeyToggleBar  = 2;   // Ctrl+Shift+B
    private const int HotKeyExamMode   = 3;   // Ctrl+Shift+E
    private const uint VK_H = 0x48, VK_B = 0x42, VK_E = 0x45;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>从 avares 资源加载窗口图标</summary>
    public static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn("加载窗口图标失败: " + ex.Message);
            return null;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Helpers.AppLogger.EnableFileLogging();
        Helpers.AppLogger.Info("学程 Avalonia 启动");

        Settings = AppSettings.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppIcon = LoadAppIcon();
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownRequested += (_, _) => Cleanup();

            // 提醒服务：课表/考试关键节点触发（声音 + 事件）
            Reminders = new ReminderService(Schedule, Settings);
            Reminders.Start();

            SetupTrayIcon();
            SetupGlobalHotKeys();

            // 自动检查更新（延迟 5 秒，不阻塞启动）
            if (Settings.AutoCheckUpdate)
                _ = CheckUpdateDelayedAsync();

            _mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async System.Threading.Tasks.Task CheckUpdateDelayedAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(5000);
            var info = await UpdateService.CheckAsync("XEKernel", "StudyJourney");
            if (info.HasUpdate && _mainWindow is MainWindow mw)
            {
                var mode = info.IsSelfContained ? "自包含版" : "框架依赖版";
                var msg = $"新版本 v{info.LatestVersion} 可用！（当前 v{UpdateService.CurrentVersion}）\n" +
                          $"将自动下载 {mode}\n\n是否立即更新？";
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var ok = await ShowConfirmAsync("学程 — 发现新版本", msg);
                    if (ok)
                    {
                        var result = await UpdateService.StartUpdateAsync(info.DownloadUrl,
                            Environment.ProcessId);
                        if (result) Environment.Exit(0);
                    }
                });
            }
        }
        catch { /* 网络不可用，静默 */ }
    }

    /// <summary>简易确认弹窗（Avalonia 无内置 MessageBox，用系统消息框）</summary>
    private static async System.Threading.Tasks.Task<bool> ShowConfirmAsync(string title, string message)
    {
        var win = (Current as App)?._mainWindow;
        if (win == null) return false;
        var box = new Window
        {
            Title = title,
            Icon = AppIcon,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            WindowDecorations = WindowDecorations.Full,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "取消", MinWidth = 80 },
                            new Button { Content = "立即更新", Classes = { "accent" }, MinWidth = 80 }
                        }
                    }
                }
            }
        };
        bool result = false;
        if (box.Content is StackPanel root)
        {
            var cancelBtn = (Button)((StackPanel)root.Children[1]).Children[0];
            var okBtn = (Button)((StackPanel)root.Children[1]).Children[1];
            cancelBtn.Click += (_, _) => box.Close();
            okBtn.Click += (_, _) => { result = true; box.Close(); };
        }
        await box.ShowDialog(win);
        return result;
    }

    private void SetupGlobalHotKeys()
    {
        // Ctrl+Shift+H 显示/隐藏主窗口
        if (!GlobalHotKeyManager.Register(HotKeyToggleMain, VK_H, true, true, false,
                () => Dispatcher.UIThread.Post(() => ToggleMainWindow())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+H 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+B 切换课表栏
        if (!GlobalHotKeyManager.Register(HotKeyToggleBar, VK_B, true, true, false,
                () => Dispatcher.UIThread.Post(() => ToggleScheduleBarViaHotkey())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+B 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+E 进入考试模式
        if (!GlobalHotKeyManager.Register(HotKeyExamMode, VK_E, true, true, false,
                () => Dispatcher.UIThread.Post(() => EnterExamMode())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+E 注册失败（可能被其他程序占用）");
    }

    /// <summary>统一入口：进入考试模式（托盘/快捷键/设置页共用，含课表栏互斥）</summary>
    public static void EnterExamModeGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.EnterExamMode();
    }

    /// <summary>统一入口：打开设置（单例）</summary>
    public static void OpenSettingsGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.OpenSettings();
        else new SettingsWindow().Show();
    }

    private void EnterExamMode()
    {
        EnterExamModeGlobal();
    }

    private void ToggleScheduleBarViaHotkey()
    {
        if (_mainWindow is MainWindow mw) mw.ToggleScheduleBarViaHotkey();
        SyncTrayScheduleItem();
    }

    /// <summary>系统托盘图标（替代 WPF Hardcodet.NotifyIcon；Avalonia 内置 TrayIcon + NativeMenu）</summary>
    private void SetupTrayIcon()
    {
        try
        {
            // WindowIcon 从 avares 资源流加载（支持 .ico；Bitmap 不支持 ico 会抛异常）
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.ico"));
            var icon = new WindowIcon(stream);

            var showItem = new NativeMenuItem("显示 / 隐藏窗口");
            showItem.Click += (_, _) => ToggleMainWindow();

            var scheduleItem = new NativeMenuItem("课表栏");
            _trayScheduleItem = scheduleItem;
            scheduleItem.Click += (_, _) => ToggleScheduleBarViaHotkey();

            var examItem = new NativeMenuItem("进入考试模式");
            examItem.Click += (_, _) => EnterExamMode();

            var settingsItem = new NativeMenuItem("打开设置");
            settingsItem.Click += (_, _) => OpenSettingsGlobal();

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) => ExitApplication();

            var menu = new NativeMenu();
            menu.Add(showItem);
            menu.Add(scheduleItem);
            menu.Add(examItem);
            menu.Add(settingsItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "学程",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ToggleMainWindow();
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("托盘图标初始化失败", ex);
        }
    }

    private void SyncTrayScheduleItem()
    {
        if (_trayScheduleItem == null || _mainWindow is not MainWindow mw) return;
        _trayScheduleItem.Header = mw.IsScheduleBarVisible ? "课表栏 ✓" : "课表栏";
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.IsVisible) _mainWindow.Hide();
        else
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        Cleanup();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime l) l.Shutdown();
    }

    private void Cleanup()
    {
        GlobalHotKeyManager.UnregisterAll();
        Reminders?.Dispose();
        Reminders = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
