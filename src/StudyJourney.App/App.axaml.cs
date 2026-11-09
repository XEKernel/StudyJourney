using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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

                desktop.Exit += (_, _) => _shellHost?.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
