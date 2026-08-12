using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // 原型验证：启动后自动弹出 WinUI 3 风格设置窗口（方便直接查看设置页效果）
            desktop.MainWindow.Opened += (_, _) => new SettingsWindow().Show(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
