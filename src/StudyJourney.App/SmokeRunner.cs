using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;

namespace StudyJourney.App;

/// <summary>--smoke 的结果快照（打印与断言共用同一份证据）。</summary>
/// <param name="Report">插件加载结论。</param>
/// <param name="Services">退出前一刻的注册服务快照（含 owner）。</param>
public sealed record SmokeResult(PluginLoadReport Report, IReadOnlyList<ServiceRegistration> Services);

/// <summary>
/// <c>--smoke</c>：**无窗**跑一遍组合根 —— 装配 → 加载插件 → 记录注册服务 → 逆序停掉并退出。
///
/// 为什么需要它：GUI 的"空窗显示 / 正常退出"在无头 CI 里断言不了（§10 未决事项 2）。
/// smoke 把"组合根能装配、插件能加载、服务图正确、退出路径不抛"这几件事变成可自动断言的部分；
/// 剩下的"窗口长什么样、关得掉吗"由人眼看一次，记进 UNVERIFIED.md。
/// </summary>
public static class SmokeRunner
{
    public static SmokeResult Run(
        string pluginsDirectory,
        string dataDirectory,
        string? configFilePath = null,
        Action<string>? logSink = null,
        CancellationToken cancellationToken = default)
    {
        var host = HostComposition.Build(pluginsDirectory, dataDirectory, configFilePath, logSink);
        try
        {
            var report = host.Loader.DiscoverAndLoadAsync(pluginsDirectory, dataDirectory, cancellationToken)
                .GetAwaiter().GetResult();

            // 快照必须在 Dispose（卸载 → 清注册项）之前取，否则服务图已经被清空。
            return new SmokeResult(report, host.Registry.Registrations);
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
