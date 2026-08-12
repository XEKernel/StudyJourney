using Avalonia;

namespace StudyJourney.Avalonia;

internal static class Program
{
    // Avalonia 入口（与 WPF 的 App.xaml 不同，Avalonia 从 Main 启动）
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
