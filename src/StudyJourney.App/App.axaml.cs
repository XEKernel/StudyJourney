using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace StudyJourney.App;

/// <summary>
/// Avalonia 应用对象。**它只负责"显示一个空主窗"**，不组装任何服务——
/// 组合根在 <see cref="HostComposition"/>，与 UI 生命周期解耦（§3.1：宿主层职责）。
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
