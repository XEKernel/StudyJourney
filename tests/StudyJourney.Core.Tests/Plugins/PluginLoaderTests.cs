using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// S10：插件加载器（编排器）—— CORE_ARCHITECTURE §3.1 / §3.2 的异常分支。
///
/// 这里守的是"一个坏插件不能拖垮整个程序"：manifest 非法、coreApi 不匹配、字段冲突、
/// 依赖缺失/成环、入口缺失、入口类型不唯一、Initialize/Start 失败 —— **每一种都只影响它自己**，
/// 而且失败后注册表里不许留下它的服务（否则下一次 GetRequired 会拿到一个已卸载 ALC 的实例）。
/// </summary>
public sealed class PluginLoaderTests : IDisposable
{
    private const string FailingDll = "FailingPlugin.dll";
    private const string NoPluginDll = "xunit.abstractions.dll";

    private readonly string _root;
    private readonly string _pluginsDirectory;
    private readonly string _dataDirectory;
    private readonly List<string> _lines = [];
    private readonly AppLogger _logger;

    public PluginLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sj-loader-" + Guid.NewGuid().ToString("N"));
        _pluginsDirectory = Path.Combine(_root, "plugins");
        _dataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(_pluginsDirectory);
        _logger = new AppLogger(line => { lock (_lines) _lines.Add(line); });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 加载过的 dll 可能仍被占用 */ }
    }

    private static string Manifest(
        string id,
        string entry = "Missing.dll",
        string coreApi = "1.x",
        string dependencies = "[]",
        string configFields = "[]")
        => $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"version\": \"1.0.0\", \"entry\": \"{entry}\", " +
           $"\"coreApi\": \"{coreApi}\", \"dependencies\": {dependencies}, \"configFields\": {configFields} }}";

    private string AddPlugin(string directoryName, string manifestJson, params string[] filesToCopy)
    {
        var dir = Path.Combine(_pluginsDirectory, directoryName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifestJson);

        foreach (var file in filesToCopy)
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(dir, file), overwrite: true);

        return dir;
    }

    private async Task<(PluginLoadReport Report, PluginLoader Loader, ServiceRegistry Registry)> LoadAsync()
    {
        var registry = new ServiceRegistry(_logger);
        var bus = new EventBus(_logger);
        registry.Register<IEventBus>(bus);
        var config = ConfigStore.CreateInMemory(logger: _logger);
        var loader = new PluginLoader(registry, bus, config, new ShellService(), _logger);
        var report = await loader.DiscoverAndLoadAsync(_pluginsDirectory, _dataDirectory);
        return (report, loader, registry);
    }

    // ── 降级形态（§3.7）──────────────────────────────────────────────────

    [Fact(DisplayName = "S10-09 插件目录不存在：空报告 + Warn，且不自动创建目录")]
    public async Task MissingPluginsDirectory_Degrades()
    {
        Directory.Delete(_pluginsDirectory, recursive: true);

        var (report, loader, _) = await LoadAsync();

        Assert.Empty(report.Plugins);
        Assert.False(Directory.Exists(_pluginsDirectory));
        Assert.Contains(_lines, l => l.Contains("未找到插件目录", StringComparison.Ordinal));
        await loader.DisposeAsync();
    }

    // ── manifest 分支（E1–E5）───────────────────────────────────────────

    [Fact(DisplayName = "S10-10 没有 plugin.json 的目录直接跳过（不报错）")]
    public async Task DirectoryWithoutManifest_IsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(_pluginsDirectory, "empty-dir"));

        var (report, loader, _) = await LoadAsync();

        Assert.Empty(report.Plugins);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-11 plugin.json 不是合法 JSON → ManifestInvalid（其它插件不受影响）")]
    public async Task BrokenManifest_IsRejected()
    {
        AddPlugin("broken", "{ \"id\": ");
        AddPlugin("ok-plugin", Manifest("ok-plugin"), FailingDll);   // 这个也会因 id-mismatch 失败，但证明扫描继续

        var (report, loader, _) = await LoadAsync();

        var broken = report.Plugins.Single(p => p.PluginId == "broken");
        Assert.Equal(PluginState.ManifestInvalid, broken.State);
        Assert.Equal(ErrorCodes.ManifestInvalid, broken.Code);
        Assert.Equal(2, report.Plugins.Count);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-12 id 与目录名不一致 → ManifestInvalid（防卸载时删错目录）")]
    public async Task IdNotMatchingDirectory_IsRejected()
    {
        AddPlugin("dir-name", Manifest("other-id"));

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.ManifestInvalid, result.State);
        Assert.Contains("不一致", result.Message, StringComparison.Ordinal);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-13 缺 coreApi → ManifestInvalid")]
    public async Task MissingCoreApi_IsRejected()
    {
        AddPlugin("no-api", "{ \"id\": \"no-api\", \"entry\": \"X.dll\" }");

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.ManifestInvalid, result.State);
        Assert.Contains("coreApi", result.Message, StringComparison.Ordinal);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-14 E4 强于 E5：id 必须等于目录名，因此正常扫描下不可能出现同 id 两个插件")]
    public async Task DuplicateId_IsUnreachableAfterDirectoryNameCheck()
    {
        // 想造"两个目录同一个 id"只能让其中一个的 id 与目录名不符 → 先被 E4 拒掉。
        // 所以 E5 的重复 id 检测在正常目录扫描下是**防御性不可达**（仅目录大小写/链接等极端情况能触发）。
        AddPlugin("dup-a", Manifest("dup-a"));
        AddPlugin("dup-a2", Manifest("dup-a"));   // 目录名 dup-a2，但 id 写成 dup-a

        var (report, loader, _) = await LoadAsync();

        Assert.Equal(2, report.Plugins.Count);

        // 目录名与 id 一致的那个进入加载阶段（这里因缺入口文件失败），证明它没被 E5 误伤；
        // id 与目录名不符的那个被 E4 拒掉 —— 所以"同 id 两插件"根本走不到 E5。
        var valid = report.Plugins.Single(p => p.PluginId == "dup-a");
        var mismatch = report.Plugins.Single(p => p.PluginId == "dup-a2");
        Assert.Equal(ErrorCodes.EntryMissing, valid.Code);
        Assert.Equal(PluginState.ManifestInvalid, mismatch.State);
        Assert.Contains("不一致", mismatch.Message!, StringComparison.Ordinal);
        await loader.DisposeAsync();
    }

    // ── coreApi（E7）─────────────────────────────────────────────────────

    [Fact(DisplayName = "S10-15 coreApi 主版本不匹配 → LoadFailed(core-api-mismatch)")]
    public async Task CoreApiMismatch_IsRejected()
    {
        AddPlugin("future", Manifest("future", coreApi: "2.x"));

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.LoadFailed, result.State);
        Assert.Equal(ErrorCodes.CoreApiMismatch, result.Code);
        await loader.DisposeAsync();
    }

    // ── 字段冲突（§5.0.1 / A0-5c）────────────────────────────────────────

    [Fact(DisplayName = "S10-16 两个插件都认领 FontSize → 字典序后者 field-conflict，提示点名双方")]
    public async Task FieldConflict_RejectsLaterPlugin()
    {
        AddPlugin("alpha-plugin", Manifest("alpha-plugin", configFields: "[\"FontSize\"]"));
        AddPlugin("beta-plugin", Manifest("beta-plugin", configFields: "[\"FontSize\"]"));

        var (report, loader, _) = await LoadAsync();

        var alpha = report.Plugins.Single(p => p.PluginId == "alpha-plugin");
        var beta = report.Plugins.Single(p => p.PluginId == "beta-plugin");

        Assert.Equal(ErrorCodes.FieldConflict, beta.Code);
        Assert.Contains("alpha-plugin", beta.Message, StringComparison.Ordinal);
        Assert.Contains("beta-plugin", beta.Message, StringComparison.Ordinal);
        Assert.NotEqual(ErrorCodes.FieldConflict, alpha.Code);
        await loader.DisposeAsync();
    }

    // ── 依赖（E8 / E9）──────────────────────────────────────────────────

    [Fact(DisplayName = "S10-17 依赖不存在的插件 → LoadFailed(dependency-missing)")]
    public async Task MissingDependency_IsRejected()
    {
        AddPlugin("needs-ghost", Manifest("needs-ghost", dependencies: "[\"ghost\"]"));

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(ErrorCodes.DependencyMissing, result.Code);
        Assert.Contains("ghost", result.Message, StringComparison.Ordinal);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-18 两个插件互相依赖 → 两者都 dependency-cycle")]
    public async Task DependencyCycle_RejectsBoth()
    {
        AddPlugin("cyc-a", Manifest("cyc-a", dependencies: "[\"cyc-b\"]"));
        AddPlugin("cyc-b", Manifest("cyc-b", dependencies: "[\"cyc-a\"]"));

        var (report, loader, _) = await LoadAsync();

        Assert.Equal(2, report.Plugins.Count);
        Assert.All(report.Plugins, p => Assert.Equal(ErrorCodes.DependencyCycle, p.Code));
        await loader.DisposeAsync();
    }

    // ── 入口（E6 / E11）────────────────────────────────────────────────

    [Fact(DisplayName = "S10-19 entry 指向的文件不存在 → LoadFailed(entry-missing)")]
    public async Task EntryMissing_IsRejected()
    {
        AddPlugin("no-entry", Manifest("no-entry", entry: "NotThere.dll"));

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(ErrorCodes.EntryMissing, result.Code);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-20 程序集里没有 IPlugin 实现 → plugin-type-ambiguous")]
    public async Task NoPluginType_IsRejected()
    {
        AddPlugin("not-a-plugin", Manifest("not-a-plugin", entry: NoPluginDll), NoPluginDll);

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(ErrorCodes.PluginTypeAmbiguous, result.Code);
        Assert.Contains("0 个", result.Message, StringComparison.Ordinal);
        await loader.DisposeAsync();
    }

    // ── 生命周期（E14 / E15 / E16）与回滚 ───────────────────────────────

    [Fact(DisplayName = "S10-21 正常夹具：Initialize + Start 成功 → Started，服务归属插件 id")]
    public async Task HealthyPlugin_Starts()
    {
        AddPlugin("failing-plugin", Manifest("failing-plugin", entry: FailingDll), FailingDll);

        var (report, loader, registry) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.Started, result.State);
        Assert.True(registry.IsRegistered<IScheduleProvider>());
        Assert.Equal("failing-plugin",
            registry.Registrations.Single(r => r.ServiceType == typeof(IScheduleProvider)).Owner);
        Assert.True(registry.IsSealed, "§3.1 第 12 步：Start 之前必须已封板，否则运行期还能改服务图");
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-22 Initialize 返回失败 → InitFailed，且它注册的服务被回滚")]
    public async Task InitializeFailure_RollsBackRegistrations()
    {
        var dir = AddPlugin("failing-plugin", Manifest("failing-plugin", entry: FailingDll), FailingDll);
        File.WriteAllText(Path.Combine(dir, "fail-mode.txt"), "init");

        var (report, loader, registry) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.InitFailed, result.State);
        Assert.Equal("fixture-init-failed", result.Code);
        Assert.False(registry.IsRegistered<IScheduleProvider>(), "失败的插件不许在注册表里留下服务");
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-23 Initialize 抛异常 → InitFailed(plugin-threw)")]
    public async Task InitializeThrow_IsCaptured()
    {
        var dir = AddPlugin("failing-plugin", Manifest("failing-plugin", entry: FailingDll), FailingDll);
        File.WriteAllText(Path.Combine(dir, "fail-mode.txt"), "throw");

        var (report, loader, _) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.InitFailed, result.State);
        Assert.Equal(ErrorCodes.PluginThrew, result.Code);
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-24 Start 返回失败 → StartFailed，且服务回滚、其它插件继续")]
    public async Task StartFailure_RollsBackAndDoesNotBlockOthers()
    {
        var dir = AddPlugin("failing-plugin", Manifest("failing-plugin", entry: FailingDll), FailingDll);
        File.WriteAllText(Path.Combine(dir, "fail-mode.txt"), "start");

        var (report, loader, registry) = await LoadAsync();

        var result = Assert.Single(report.Plugins);
        Assert.Equal(PluginState.StartFailed, result.State);
        Assert.Equal("fixture-start-failed", result.Code);
        Assert.False(registry.IsRegistered<IScheduleProvider>());
        await loader.DisposeAsync();
    }

    [Fact(DisplayName = "S10-25 卸载：StopAllAsync 后状态 Unloaded，插件服务从注册表移除")]
    public async Task StopAll_UnloadsAndCleansRegistry()
    {
        AddPlugin("failing-plugin", Manifest("failing-plugin", entry: FailingDll), FailingDll);

        var (report, loader, registry) = await LoadAsync();
        Assert.Equal(PluginState.Started, Assert.Single(report.Plugins).State);

        await loader.DisposeAsync();

        Assert.Equal(PluginState.Unloaded, Assert.Single(loader.Results).State);
        Assert.False(registry.IsRegistered<IScheduleProvider>());
        Assert.True(registry.IsRegistered<IServiceRegistry>());   // 核心登记不受影响
        Assert.DoesNotContain(_lines, l => l.Contains("任意事件" , StringComparison.Ordinal));
    }
}
