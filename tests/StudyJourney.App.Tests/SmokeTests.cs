using StudyJourney.App;
using StudyJourney.Core.Plugins;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.App.Tests;

/// <summary>
/// S10：<c>--smoke</c> 无窗模式的端到端断言。
///
/// 这是"组合根 + 加载器 + 插件 + 服务图 + 退出路径"唯一能自动化的证据：
/// 起 GUI 无法在 CI 断言（§10 未决事项 2），但"能不能装起来、插件能不能跑、能不能干净退出"可以。
/// </summary>
public sealed class SmokeTests : IDisposable
{
    private const string ProbeId = "a015-probe";
    private const string ProbeDll = "A0_15_Probe.dll";

    private readonly string _root;
    private readonly List<string> _logs = [];

    public SmokeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sj-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 加载过的 dll 可能仍被占用 */ }
    }

    private string CreateProbePluginDirectory()
    {
        var dir = Path.Combine(_root, "plugins", ProbeId);
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, ProbeDll), Path.Combine(dir, ProbeDll), overwrite: true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "plugin.json"), Path.Combine(dir, "plugin.json"), overwrite: true);
        return dir;
    }

    [Fact(DisplayName = "S10-06 smoke：加载真实插件 → Started → IScheduleProvider 归该插件所有 → 数据目录已建")]
    public void Smoke_LoadsProbePlugin_AndRegistersItsService()
    {
        CreateProbePluginDirectory();

        var result = SmokeRunner.Run(
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"),
            logSink: line => { lock (_logs) _logs.Add(line); });

        var plugin = Assert.Single(result.Report.Plugins);
        Assert.Equal(ProbeId, plugin.PluginId);
        Assert.Equal(PluginState.Started, plugin.State);
        Assert.Equal(1, result.Report.RunningCount);

        // 插件注册的服务 owner 必须是插件 id（门面注入的结果），不是 core。
        Assert.Contains(result.Services, s => s.ServiceType == typeof(IScheduleProvider) && s.Owner == ProbeId);
        Assert.Contains(result.Services, s => s.ServiceType == typeof(IServiceRegistry) && s.Owner == "core");

        // 存储句柄已确保 data/<id>/ 存在（D5）
        Assert.True(Directory.Exists(Path.Combine(_root, "data", ProbeId)));

        // 退出路径：Stop + Unload 走完，日志里不允许出现"内存未释放"
        Assert.Contains(_logs, l => l.Contains(ProbeId, StringComparison.Ordinal) && l.Contains("已启动", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs, l => l.Contains("内存未释放", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "S10-07 smoke：插件目录不存在 → 降级启动 rc=0，且不自动创建 plugins/（§3.7）")]
    public void Smoke_WithoutPluginsDirectory_DegradesGracefully()
    {
        var plugins = Path.Combine(_root, "plugins");
        Assert.False(Directory.Exists(plugins));

        var result = SmokeRunner.Run(plugins, Path.Combine(_root, "data"), logSink: line => { lock (_logs) _logs.Add(line); });

        Assert.Empty(result.Report.Plugins);
        Assert.Equal(0, result.Report.RunningCount);
        Assert.False(Directory.Exists(plugins));   // 不创建
        Assert.Contains(_logs, l => l.Contains("未找到插件目录", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "S10-08 Program.Main --smoke 返回 0（进程能正常退出，非超时强杀）")]
    public void Program_Main_Smoke_ReturnsZero()
    {
        CreateProbePluginDirectory();

        var code = Program.Main(
        [
            "--smoke",
            "--plugins", Path.Combine(_root, "plugins"),
            "--data", Path.Combine(_root, "data"),
        ]);

        Assert.Equal(0, code);
    }
}
