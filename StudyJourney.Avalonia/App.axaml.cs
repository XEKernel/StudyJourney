using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia;

public partial class App : Application
{
    /// <summary>全局设置（从 settings.json 加载，与 WPF 版共用同一配置）</summary>
    public static AppSettings Settings { get; private set; } = new();

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
    private Window? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = AppSettings.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            // 提醒服务：课表/考试关键节点触发（声音 + 事件）
            Reminders = new ReminderService(Schedule, Settings);
            Reminders.Start();

            SetupTrayIcon();

            // 原型验证：启动后自动弹出 WinUI 3 风格设置窗口（方便直接查看设置页效果）
            _mainWindow.Opened += (_, _) => new SettingsWindow().Show(_mainWindow!);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>系统托盘图标（替代 WPF Hardcodet.NotifyIcon；Avalonia 内置 TrayIcon + NativeMenu）</summary>
    private void SetupTrayIcon()
    {
        try
        {
            var icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://StudyJourney.Avalonia/Assets/icon.ico"))));

            var showItem = new NativeMenuItem("显示 / 隐藏窗口");
            showItem.Click += (_, _) => ToggleMainWindow();

            var examItem = new NativeMenuItem("进入考试模式");
            examItem.Click += (_, _) => new ExamModeWindow().Show();

            var settingsItem = new NativeMenuItem("打开设置");
            settingsItem.Click += (_, _) => new SettingsWindow().Show(_mainWindow!);

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) =>
            {
                _trayIcon?.Dispose();
                _mainWindow?.Close();
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime l) l.Shutdown();
            };

            var menu = new NativeMenu();
            menu.Add(showItem);
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
}
