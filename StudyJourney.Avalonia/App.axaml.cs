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

    /// <summary>全局自动化任务服务（拼图式规则：触发 + 动作，automations.json）</summary>
    public static AutomationService? Automation { get; private set; }

    /// <summary>设置被保存后触发（主窗口/悬浮栏等订阅并刷新）</summary>
    public static event Action? SettingsChanged;

    /// <summary>保存设置并广播变更（线程安全：#2 修复 —— Kestrel 后台线程调用时自动封送 UI 线程再 Invoke，订阅者无需关心来源线程）</summary>
    public static void SaveSettings()
    {
        Settings.Save();
        if (Dispatcher.UIThread.CheckAccess())
            SettingsChanged?.Invoke();
        else
            Dispatcher.UIThread.Post(() => SettingsChanged?.Invoke());
    }

    /// <summary>
    /// 统一入口：从磁盘重载课表并广播 DataChanged（线程安全）。
    /// 远程 PUT /api/schedule、恢复备份、编辑器保存一律走这里，避免各处自行 Post 造成不一致（报告第四节 A）。
    /// 内部 UI 线程封送：ScheduleManager._data 被主窗口每秒 Tick 读取。
    /// </summary>
    public static void ReloadScheduleFromDisk()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            try { Schedule.Reload(); }
            catch (Exception ex) { Helpers.AppLogger.Error("课表重载到内存失败", ex); }
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { Schedule.Reload(); }
                catch (Exception ex) { Helpers.AppLogger.Error("课表重载到内存失败", ex); }
            });
        }
    }

    /// <summary>通过系统托盘发送 Windows 通知（提醒方式=Windows 通知时使用）</summary>
    public static void ShowSystemNotification(string title, string message)
    {
        try
        {
            Helpers.SystemToast.Show(title, message);
        }
        catch { /* 系统通知失败静默 */ }
    }

    private TrayIcon? _trayIcon;
    private Window? _mainWindow;

    /// <summary>应用图标（各窗口标题栏/任务栏共用，从 avares 加载）</summary>
    public static WindowIcon? AppIcon { get; private set; }

    // ── 全局快捷键 ID（与 WPF 版一致）────────────────────────
    private const int HotKeyToggleMain = 1;   // Ctrl+Shift+H
    private const int HotKeyWhiteboard = 2;   // Ctrl+Shift+W（白板，PLANNING 2.4）
    private const int HotKeyExamMode   = 3;   // Ctrl+Shift+E
    private const int HotKeyAnnotation = 4;   // Ctrl+Alt+D（屏幕批注，PLANNING 2.3/2.7#7）
    private const uint VK_H = 0x48, VK_E = 0x45, VK_W = 0x57, VK_D = 0x44;

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

            // 提醒服务：课表/考试关键节点触发（声音 + 事件）。
            // #3 修复后不再注入 Settings 实例：ReminderService 内部动态读 App.Settings
            Reminders = new ReminderService(Schedule);
            Reminders.Start();

            // 自动化任务服务：拼图式规则（触发拼块 + 动作拼块）。总开关默认关，设置页开启才生效。
            // 注意：automations.json 独立于 settings.json（恢复默认设置不误删规则）
            Automation = new AutomationService(Schedule);
            Automation.Start();

            SetupTrayIcon();
            SetupGlobalHotKeys();

            // 自动检查更新（延迟 5 秒，不阻塞启动）
            if (Settings.AutoCheckUpdate)
                _ = CheckUpdateDelayedAsync();

            // 当天有考试且开启自动进入 → 延迟 2 秒进入考试模式（对齐 WPF）
            if (Settings.AutoEnterExamMode && Settings.EnableExamMode &&
                Schedule.GetTodayExams().Count > 0)
                _ = EnterExamModeDelayedAsync();

            _mainWindow.Show();

            // 自检钩子（仅 SJ_SELFTEST=whiteboard 时生效，用于验证白板渲染链路）——
            // 不改变正常启动行为，跑完即退出，便于自动化冒烟测试。
            if (Environment.GetEnvironmentVariable("SJ_SELFTEST") == "whiteboard")
            {
                Dispatcher.UIThread.Post(RunWhiteboardSelfTest, DispatcherPriority.Background);
                return;
            }

            // 远程 HTTP 服务：设置开启则延迟 1.5s 自动启动（不阻塞首屏；失败记日志不影响主程序）
            if (Settings.AutoStartHttpServer)
                _ = StartHttpServerDelayedAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>延迟启动远程 HTTP 服务（供老师局域网访问；#10：走 StartAsync 不阻塞任何线程）</summary>
    private static async System.Threading.Tasks.Task StartHttpServerDelayedAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(1500);
            await HttpServerService.StartAsync();
            Helpers.AppLogger.Info($"远程服务已自动启动，端口 {HttpServerService.Port}");
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error($"远程服务自动启动失败: {ex.Message}", ex);
        }
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
                    var ok = await ConfirmAsync("学程 — 发现新版本", msg, "立即更新", "取消");
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

    /// <summary>开考自动进入考试模式：延迟 2 秒（对齐 WPF MainWindow.Schedule.cs）</summary>
    private async System.Threading.Tasks.Task EnterExamModeDelayedAsync()
    {
        await System.Threading.Tasks.Task.Delay(2000);
        Dispatcher.UIThread.Post(EnterExamMode);
    }

    /// <summary>通用确认弹窗（Avalonia 无内置 MessageBox，用系统消息框）</summary>
    public static async System.Threading.Tasks.Task<bool> ConfirmAsync(string title, string message,
        string okText = "确定", string cancelText = "取消")
    {
        return await Helpers.DialogHelper.ShowConfirmAsync(
            (Current as App)?._mainWindow, title, message, okText, cancelText);
    }

    /// <summary>简易提示弹窗（单一"确定"按钮；主窗口隐藏时降级为非模态）</summary>
    public static async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
    {
        await Helpers.DialogHelper.ShowMessageAsync((Current as App)?._mainWindow, title, message);
    }

    private void SetupGlobalHotKeys()
    {
        // Ctrl+Shift+H 显示/隐藏主窗口
        if (!GlobalHotKeyManager.Register(HotKeyToggleMain, VK_H, true, true, false,
                () => Dispatcher.UIThread.Post(() => ToggleMainWindow())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+H 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+E 进入考试模式
        if (!GlobalHotKeyManager.Register(HotKeyExamMode, VK_E, true, true, false,
                () => Dispatcher.UIThread.Post(() => EnterExamMode())))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+E 注册失败（可能被其他程序占用）");

        // Ctrl+Shift+W 打开白板（PLANNING 2.4；老师板书随时唤起）
        if (!GlobalHotKeyManager.Register(HotKeyWhiteboard, VK_W, true, true, false,
                () => Dispatcher.UIThread.Post(OpenWhiteboardGlobal)))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Shift+W 注册失败（可能被其他程序占用）");

        // Ctrl+Alt+D 屏幕批注（PLANNING 2.3：在任意应用之上圈画）
        if (!GlobalHotKeyManager.Register(HotKeyAnnotation, VK_D, true, false, true,
                () => Dispatcher.UIThread.Post(ToggleScreenAnnotationGlobal)))
            Helpers.AppLogger.Warn("全局快捷键 Ctrl+Alt+D 注册失败（可能被其他程序占用）");
    }

    /// <summary>统一入口：进入考试模式（托盘/快捷键/设置页共用）</summary>
    public static void EnterExamModeGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.EnterExamMode();
    }

    /// <summary>统一入口：退出考试模式（设置页"退出考试模式"按钮）</summary>
    public static void ExitExamModeGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.ExitExamMode();
    }

    /// <summary>统一入口：打开设置（单例）</summary>
    public static void OpenSettingsGlobal()
    {
        if (Current is App app && app._mainWindow is MainWindow mw) mw.OpenSettings();
        else new SettingsWindow().Show();
    }

    // ── 白板（PLANNING 2.4）──────────────────────────────────
    // 单例：重复触发只激活已有窗口，避免老师连按快捷键开出多个白板（板书会分散在多个窗口里）

    private static WhiteboardWindow? _whiteboard;

    /// <summary>统一入口：打开白板（托盘 / 全局快捷键 / 主窗口菜单共用）</summary>
    public static void OpenWhiteboardGlobal()
    {
        if (_whiteboard is { IsVisible: true })
        {
            if (_whiteboard.WindowState == WindowState.Minimized)
                _whiteboard.WindowState = WindowState.Normal;
            _whiteboard.Activate();
            return;
        }
        _whiteboard = new WhiteboardWindow();
        _whiteboard.Closed += (_, _) => _whiteboard = null;
        _whiteboard.Show();
    }

    // ── 屏幕批注（PLANNING 2.3）──────────────────────────────

    private static ScreenAnnotationWindow? _annotation;

    /// <summary>开关屏幕批注覆盖层（快捷键/托盘共用）。已开则关闭（再按一次退出，符合"随时收起"直觉）</summary>
    public static void ToggleScreenAnnotationGlobal()
    {
        if (_annotation is { IsVisible: true })
        {
            _annotation.Close();
            return;
        }
        _annotation = new ScreenAnnotationWindow();
        _annotation.Closed += (_, _) => _annotation = null;
        _annotation.Show();
    }

    /// <summary>
    /// 白板渲染链路自检（SJ_SELFTEST=whiteboard）：真正实例化窗口 + 走一遍导出渲染，
    /// 验证 InkCanvas 几何构建与 RenderTargetBitmap 在真实 Avalonia 平台下可用。
    /// 结果写日志后退出进程（仅测试用，正常启动不进入）。
    /// </summary>
    private static void RunWhiteboardSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine("[SELFTEST] 白板渲染链路自检开始");

            // 1. 文档逻辑：增 / 撤销 / 重做 / 擦除
            var doc = new Helpers.InkDocument();
            var s1 = new Helpers.InkStroke { Color = Colors.Red, Thickness = 3, Tool = Helpers.InkTool.Pen };
            s1.Points.Add(new global::Avalonia.Point(10, 10));
            s1.Points.Add(new global::Avalonia.Point(60, 40));
            s1.Points.Add(new global::Avalonia.Point(120, 20));
            doc.Add(s1);
            sb.AppendLine($"[SELFTEST] 笔画数={doc.Strokes.Count} canUndo={doc.CanUndo}");
            sb.AppendLine($"[SELFTEST] undo={doc.Undo()} → {doc.Strokes.Count} canRedo={doc.CanRedo}");
            sb.AppendLine($"[SELFTEST] redo={doc.Redo()} → {doc.Strokes.Count}");

            // 2. 几何构建（真实平台；控制台自检跑到这里会抛 IPlatformRenderInterface 未定位）
            var geo = Helpers.InkGeometry.BuildGeometry(s1, Helpers.IdentityInkSurface.Instance);
            sb.AppendLine($"[SELFTEST] 几何构建 OK bounds={geo.Bounds}");

            // 3. 全部背景样式渲染
            foreach (Helpers.BoardBackground bg in Enum.GetValues<Helpers.BoardBackground>())
            {
                using var bmp = Helpers.BoardRenderer.RenderPage(bg, doc.Strokes, 800, 600, 1.0);
                if (bmp.PixelSize.Width <= 0) throw new Exception($"背景 {bg} 渲染尺寸为 0");
                sb.AppendLine($"[SELFTEST] 背景 {bg} 渲染 OK {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
            }

            // 4. 真实窗口实例化（会触发 InkCanvas 构造 + 背景层）
            var win = new Views.WhiteboardWindow();
            sb.AppendLine("[SELFTEST] WhiteboardWindow 实例化 OK（未 Show，避免干扰桌面）");
            win.Close();

            sb.AppendLine("[SELFTEST] 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[SELFTEST] 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        Helpers.AppLogger.Info(sb.ToString());
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt"),
            sb.ToString());
        Environment.Exit(0);
    }

    private void EnterExamMode()
    {
        EnterExamModeGlobal();
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

            var examItem = new NativeMenuItem("进入考试模式");
            examItem.Click += (_, _) => EnterExamMode();

            var boardItem = new NativeMenuItem("打开白板（板书）");
            boardItem.Click += (_, _) => OpenWhiteboardGlobal();

            var annotItem = new NativeMenuItem("屏幕批注（Ctrl+Alt+D）");
            annotItem.Click += (_, _) => ToggleScreenAnnotationGlobal();

            var settingsItem = new NativeMenuItem("打开设置");
            settingsItem.Click += (_, _) => OpenSettingsGlobal();

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) => ExitApplication();

            var menu = new NativeMenu();
            menu.Add(showItem);
            menu.Add(examItem);
            menu.Add(boardItem);
            menu.Add(annotItem);
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
        // 复用 MainWindow.ToggleVisibility（含 _suppressAutoHide 豁免 + 临时置顶，
        // 避免"显示窗口被 MaximizeCheckTimer 立即隐藏"的闪退问题）
        if (_mainWindow is MainWindow mw) mw.ToggleVisibility();
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
        Automation?.Dispose();
        Automation = null;
        // B9 修复：右键「退出」/系统关机走 Cleanup 路径，原实现不停远程服务 → 与 MainWindow.Closed
        // 路径不对称（服务残留/端口占用）。Stop 内部会 Join 后台线程（最长 8s），放后台执行避免卡退出。
        _ = System.Threading.Tasks.Task.Run(() => HttpServerService.Stop());
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
