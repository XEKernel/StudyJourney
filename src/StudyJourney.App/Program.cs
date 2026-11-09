using Avalonia;
using StudyJourney.Sdk;

namespace StudyJourney.App;

/// <summary>
/// 进程入口（CORE_ARCHITECTURE §3.1 第 1 步 + §3.5 退出）。
///
/// 三条命令：
/// <list type="bullet">
/// <item>无参数 → 单实例 Mutex + 组合根 + Avalonia 空主窗（GUI）；</item>
/// <item><c>--smoke</c> → 无窗跑组合根，打印插件与注册服务，正常退出（CI 可断言）；</item>
/// <item><c>--plugins &lt;dir&gt;</c> / <c>--data &lt;dir&gt;</c> / <c>--config &lt;path&gt;</c> → 覆盖默认路径。</item>
/// </list>
///
/// ⚠ **AssemblyName 必须保持 StudyJourneyAvalonia**（§1.4.1 唯一例外）：改 exe 名会同时牵动
/// 更新器的 AOT 形态判定、快捷方式、注册表启动项与 CI 资产名，延后到 v4.0.0 单独立项。
///
/// ⚠ <b>M0 的加载时序</b>：插件在 Avalonia 平台初始化**之前**加载完（等价于 §3.1 的
/// "先建组合根、再开窗"）。M0 插件不碰 UI 线程，所以这是安全的；M1 起要挪到
/// <c>OnFrameworkInitializationCompleted</c> 里（壳先建好、窗口最后 Show）。
/// </summary>
public static class Program
{
    /// <summary>单实例 Mutex 名（与 v3.3.0 相同：升级期间新旧版本互斥，避免两套托盘同时存在）。</summary>
    private const string MutexName = "GaokaoCountdown_SingleInstance_XEKernel";

    private static Mutex? _mutex;

    [STAThread]
    public static int Main(string[] args)
    {
        var options = ParseArguments(args);
        if (options.ArgumentError is not null)
        {
            Console.Error.WriteLine(options.ArgumentError);
            Console.Error.WriteLine("用法：StudyJourneyAvalonia [--smoke] [--plugins <目录>] [--data <目录>] [--config <settings.json>]");
            return 2;
        }

        if (options.Smoke)
            return RunSmoke(options);

        // ── GUI 路径：单实例 → 组合根 → 加载插件 → 空主窗 ──────────────
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            // 前一个实例异常退出：互斥体已被放弃，本进程重新持有一个
            _mutex = new Mutex(true, MutexName, out createdNew);
            createdNew = true;
        }

        if (!createdNew)
        {
            Console.WriteLine("学程已在运行（单实例保护），本次启动退出。");
            return 0;
        }

        try
        {
            // M1 起日志落文件（WinExe 没有控制台，Console.WriteLine 等于丢弃）。
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "core.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var host = HostComposition.Build(
                options.PluginsDirectory,
                options.DataDirectory,
                options.ConfigFile,
                logSink: line => File.AppendAllText(logPath, line + Environment.NewLine));
            try
            {
                // 阻塞等待加载完成：Avalonia 尚未启动，没有同步上下文，不会死锁。
                var report = host.Loader.DiscoverAndLoadAsync(options.PluginsDirectory, options.DataDirectory)
                    .GetAwaiter().GetResult();
                Console.WriteLine($"插件：{report.RunningCount}/{report.Plugins.Count} 运行中");
                foreach (var registration in host.Registry.Registrations)
                    Console.WriteLine($"  - {registration.ServiceType.Name}  owner: {registration.Owner}");

                BuildAvaloniaApp(host).StartWithClassicDesktopLifetime(args);
                return 0;
            }
            finally
            {
                // 正常退出：逆拓扑序 StopAsync + Unload（§3.5）
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            try { _mutex.ReleaseMutex(); } catch { /* 已释放/未持有 */ }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    /// <summary>
    /// Avalonia 引导（与 v3.3.0 相同的 UsePlatformDetect，仅 Windows 目标）。
    /// 传入组合根：<see cref="App"/> 在 <c>OnFrameworkInitializationCompleted</c> 里把壳挂上并重放插件登记。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp(HostServices host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return AppBuilder.Configure(() => new App(host))
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static int RunSmoke(CommandLineOptions options)
    {
        Console.WriteLine("=== StudyJourney M0 smoke（无窗）===");
        Console.WriteLine($"核心契约版本 : {CoreInfo.ApiVersion}");
        Console.WriteLine($"插件目录     : {options.PluginsDirectory}");
        Console.WriteLine($"数据目录     : {options.DataDirectory}");
        Console.WriteLine($"配置文件     : {options.ConfigFile ?? "(无，用内存空配置)"}");

        var result = SmokeRunner.Run(options.PluginsDirectory, options.DataDirectory, options.ConfigFile);

        Console.WriteLine($"插件数量     : {result.Report.Plugins.Count}（运行中 {result.Report.RunningCount}）");
        foreach (var plugin in result.Report.Plugins)
        {
            var detail = plugin.Code is null ? string.Empty : $" [{plugin.Code}] {plugin.Message}";
            Console.WriteLine($"  - {plugin.PluginId,-16} {plugin.State}{detail}");
        }

        Console.WriteLine("已注册服务：");
        foreach (var registration in result.Services)
            Console.WriteLine($"  - {registration.ServiceType.Name,-24} owner: {registration.Owner}");

        Console.WriteLine("=== smoke 结束（插件已按逆拓扑序停掉并卸载）===");
        return 0;
    }

    private sealed record CommandLineOptions(
        bool Smoke,
        string PluginsDirectory,
        string DataDirectory,
        string? ConfigFile,
        string? ArgumentError);

    private static CommandLineOptions ParseArguments(string[] args)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var plugins = Path.Combine(baseDirectory, "plugins");
        var data = Path.Combine(baseDirectory, "data");
        string? config = Path.Combine(baseDirectory, "settings.json");
        var smoke = false;
        string? error = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--smoke":
                    smoke = true;
                    break;
                case "--plugins" when i + 1 < args.Length:
                    plugins = args[++i];
                    break;
                case "--data" when i + 1 < args.Length:
                    data = args[++i];
                    break;
                case "--config" when i + 1 < args.Length:
                    config = args[++i];
                    break;
                case "--plugins" or "--data" or "--config":
                    error = $"参数 {args[i]} 缺少取值";
                    break;
                default:
                    // GUI 路径下不认识的参数交给 Avalonia 自己消化；smoke 路径下未知参数无害。
                    break;
            }
        }

        if (!File.Exists(config))
            config = null;

        return new CommandLineOptions(smoke, plugins, data, config, error);
    }
}
