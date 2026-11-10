using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.App;

/// <summary>
/// Avalonia 应用对象。**它只负责"开窗并把壳挂上"**，不组装任何服务——
/// 组合根在 <see cref="HostComposition"/>，与 UI 生命周期解耦（§3.1：宿主层职责）。
///
/// ⚠ **时序**（CORE_ARCHITECTURE §3.1）：插件在 <see cref="Program"/> 里、Avalonia 启动**之前**
/// 就已加载完（它们注册的菜单/托盘/槽位记在 Core 的登记表里）。这里做的事是"补上宿主"：
/// <see cref="Core.Shell.ShellService.AttachHost"/> 一次性重放全部登记。
/// 反过来的顺序（先开窗再加载插件）会让每个插件都得自己 PostToUi，且启动期闪空窗。
/// </summary>
public partial class App : Application
{
    private readonly HostServices? _host;
    private ShellHost? _shellHost;

    /// <summary>XAML 设计器 / 兜底构造：没有组合根时只画空壳（不重复调 <see cref="Initialize"/>，框架会调）。</summary>
    public App() { }

    /// <summary>正常运行路径：由 <see cref="Program"/> 传入已装配好的组合根。</summary>
    public App(HostServices host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            if (_host is { } host)
            {
                _shellHost = new ShellHost(window, host.Logger);

                // 重放：插件在 Avalonia 启动前注册的菜单/托盘/槽位在这里一次性落到界面上。
                host.Shell.AttachHost(_shellHost);
                host.Notifications.AttachRenderer(_shellHost);

                // 壳自己的热键必须在 AttachHost 之后注册：回调要操作已经存在的窗口。
                RegisterShellHotKeys(host.Shell, _shellHost, host);

                desktop.Exit += (_, _) => _shellHost?.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 壳自身的热键（M1 只有主窗显隐）。
    ///
    /// **为什么只有 1 个**：<c>Ctrl+Shift+W/E/P/K</c> 分别属于白板 / 考试 / PDF / 课表编辑器，
    /// 那些窗口要到 M1-C（课表）与 M4（其余）才存在——**热键先于功能存在只会注册成功、按下去没反应**，
    /// 比不注册更难查。所以按下"功能落地时才加热键"来做，M1 只落这一个。
    ///
    /// <c>Ctrl+Shift+H</c> 与 <c>Ctrl+Shift+L</c>（听力）在 v3.3.0 是 7 个热键里的两个，
    /// 架构文档 §9.2 写"6 个"是漏算了听力——那个随播放器一起进 M4。
    /// </summary>
    private static void RegisterShellHotKeys(ShellService shell, ShellHost shellHost, HostServices host)
    {
        var registered = shell.ForPlugin("core").RegisterHotKey(new HotKeyDescriptor
        {
            Id = "core.toggle-main",
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift,
            Key = Key.H,
            Handler = shellHost.ToggleMainWindow,
        });

        // A1-4：冲突/超限**不许静默失败**。这里至少要在日志里点名是哪一条、什么原因；
        // 用户可见的提示（托盘气泡）等设置窗落地时补，那时才有"去哪改热键"的可操作落点。
        if (registered.IsFailure)
        {
            host.Logger.Warn(
                $"[Shell] 热键 Ctrl+Shift+H（主窗显隐）注册失败：{registered.Code} — {registered.Message}。" +
                "通常是别的程序占用了该组合；托盘菜单里仍可显示/隐藏主窗。");
        }
    }
}
