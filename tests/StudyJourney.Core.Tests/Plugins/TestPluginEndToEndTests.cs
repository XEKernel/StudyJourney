using System.Text.Json;
using StudyJourney.Core.Config;
using StudyJourney.Core.Logging;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// S11：<c>plugins/Test</c>（M0 的最小插件）的端到端验收。
///
/// 这一组是"微内核能不能跑通"的最终证据：**真实插件目录** → 真实 ALC → 唯一 IPlugin →
/// Initialize → 注册 IScheduleProvider → 读配置 → Start → 卸载。
/// 与 A0-15 的探针不同，这里用的是**会随产品发布形态存在**的那个插件（有 plugin.json、有 UI 类型声明）。
/// </summary>
public sealed class TestPluginEndToEndTests : IDisposable
{
    private const string PluginId = "test";
    private const string PluginDll = "StudyJourney.Plugin.Test.dll";
    private const string ManifestFile = "plugin.json";

    private readonly string _root;
    private readonly string _pluginsDirectory;
    private readonly string _dataDirectory;
    private readonly List<string> _lines = [];
    private readonly AppLogger _logger;

    public TestPluginEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sj-testplugin-" + Guid.NewGuid().ToString("N"));
        _pluginsDirectory = Path.Combine(_root, "plugins");
        _dataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(_pluginsDirectory);
        _logger = new AppLogger(line => { lock (_lines) _lines.Add(line); });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 加载过的 dll 可能仍被占用 */ }
    }

    /// <summary>把构建产物里的插件（dll + plugin.json）复制成一个"真实插件目录"。</summary>
    private void InstallPlugin(string? configJson = null)
    {
        var directory = Path.Combine(_pluginsDirectory, PluginId);
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, PluginDll), Path.Combine(directory, PluginDll), overwrite: true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, ManifestFile), Path.Combine(directory, ManifestFile), overwrite: true);

        if (configJson is not null)
            File.WriteAllText(Path.Combine(_root, "settings.json"), configJson);
    }

    private sealed record Loaded(PluginLoadReport Report, PluginLoader Loader, ServiceRegistry Registry, ConfigStore Config);

    private async Task<Loaded> LoadAsync()
    {
        var configPath = Path.Combine(_root, "settings.json");
        var config = File.Exists(configPath)
            ? ConfigStore.Load(configPath, _logger)
            : ConfigStore.CreateInMemory(logger: _logger);

        var registry = new ServiceRegistry(_logger);
        var bus = new EventBus(_logger);
        registry.Register<IEventBus>(bus);
        var loader = new PluginLoader(registry, bus, config, _logger);
        var report = await loader.DiscoverAndLoadAsync(_pluginsDirectory, _dataDirectory);
        return new Loaded(report, loader, registry, config);
    }

    // ── manifest 契约 ────────────────────────────────────────────────────

    [Fact(DisplayName = "S11-01 plugins/Test/plugin.json：字段正确且 coreApi 主版本兼容")]
    public void Manifest_IsValid()
    {
        var path = RepoRoot.Combine("plugins", "Test", ManifestFile);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path));

        Assert.NotNull(manifest);
        Assert.Equal(PluginId, manifest!.Id);
        Assert.Equal("StudyJourney.Plugin.Test.dll", manifest.Entry);
        Assert.True(CoreInfo.IsApiCompatible(manifest.CoreApi));
        Assert.Empty(manifest.Dependencies);
        Assert.Contains("TestField", manifest.ConfigFields);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Version));
    }

    // ── 端到端加载 ───────────────────────────────────────────────────────

    [Fact(DisplayName = "S11-02 端到端：插件 Started、IScheduleProvider 归 test 所有、返回数学+英语两节课")]
    public async Task Plugin_LoadsAndProvidesTwoPeriods()
    {
        InstallPlugin();
        var loaded = await LoadAsync();

        var result = Assert.Single(loaded.Report.Plugins);
        Assert.Equal(PluginId, result.PluginId);
        Assert.Equal(PluginState.Started, result.State);
        Assert.Null(result.Code);

        var provider = loaded.Registry.Get<IScheduleProvider>();
        Assert.NotNull(provider);
        Assert.Equal(PluginId, loaded.Registry.Registrations.Single(r => r.ServiceType == typeof(IScheduleProvider)).Owner);

        var today = await provider!.GetTodayAsync();
        Assert.True(today.IsOk);
        var schedule = today.Value!;
        Assert.Equal(2, schedule.Periods.Count);
        Assert.Equal("数学", schedule.Periods[0].Subject);
        Assert.Equal("英语", schedule.Periods[1].Subject);
        Assert.Equal("08:00", schedule.Periods[0].StartTimeStr);
        Assert.Equal("08:45", schedule.Periods[0].EndTimeStr);
        Assert.Equal("08:55", schedule.Periods[1].StartTimeStr);
        Assert.Equal(PeriodType.Normal, schedule.Periods[0].Type);

        await loaded.Loader.DisposeAsync();
    }

    [Fact(DisplayName = "S11-03 ResolveEffectiveDayOfWeek：周日归一化为 7（纯函数可断言）")]
    public async Task ResolveEffectiveDayOfWeek_SundayIsSeven()
    {
        InstallPlugin();
        var loaded = await LoadAsync();
        var provider = loaded.Registry.Get<IScheduleProvider>()!;

        var monday = new DateOnly(2026, 11, 9);
        Assert.Equal(1, provider.ResolveEffectiveDayOfWeek(monday));

        var sunday = new DateOnly(2026, 11, 8);
        Assert.Equal(7, provider.ResolveEffectiveDayOfWeek(sunday));

        var schedule = await provider.GetDayAsync(sunday);
        Assert.Equal(7, schedule.Value!.EffectiveDayOfWeek);
        Assert.Equal(sunday, schedule.Value.Date);

        await loaded.Loader.DisposeAsync();
    }

    // ── ALC 类型身份（真实插件版）───────────────────────────────────────

    [Fact(DisplayName = "S11-04 插件声明的 TestControl 继承的是核心侧同一个 UserControl（ALC 白名单生效）")]
    public async Task PluginControlType_IsSharedWithCore()
    {
        InstallPlugin();
        var loaded = await LoadAsync();

        var provider = loaded.Registry.Get<IScheduleProvider>()!;
        var pluginAssembly = provider.GetType().Assembly;

        var controlType = pluginAssembly.GetType("StudyJourney.Plugin.Test.TestControl", throwOnError: true)!;
        Assert.Same(typeof(Avalonia.Controls.UserControl), controlType.BaseType);
        Assert.Equal(typeof(Avalonia.Controls.UserControl).Assembly.Location, controlType.BaseType!.Assembly.Location);

        await loaded.Loader.DisposeAsync();
    }

    // ── 配置分发端到端 ──────────────────────────────────────────────────

    [Fact(DisplayName = "S11-05 配置分发端到端：插件读到自己认领的 TestField，未认领字段原样保留")]
    public async Task Config_DistributedToPlugin()
    {
        const string settings = "{\r\n  \"TestField\": 42,\r\n  \"OtherPluginField\": \"keep\"\r\n}";
        InstallPlugin(settings);

        var loaded = await LoadAsync();
        Assert.Equal(PluginState.Started, Assert.Single(loaded.Report.Plugins).State);

        // 插件把读到的值打进日志 → 端到端证明"按 configFields 摘取"这条路通
        Assert.Contains(_lines, line => line.Contains("TestField = 42", StringComparison.Ordinal));

        // 插件没写回任何东西：未认领字段原样保留、文件没被重写
        Assert.Equal("keep", loaded.Config.Root["OtherPluginField"]!.GetValue<string>());
        Assert.False(loaded.Config.IsDirty);

        await loaded.Loader.DisposeAsync();
    }

    // ── 卸载 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "S11-06 卸载：状态 Unloaded、服务移除、ALC 可回收（无「内存未释放」告警）")]
    public async Task Dispose_UnloadsCleanly()
    {
        InstallPlugin();
        var loaded = await LoadAsync();

        await loaded.Loader.DisposeAsync();

        Assert.Equal(PluginState.Unloaded, Assert.Single(loaded.Loader.Results).State);
        Assert.False(loaded.Registry.IsRegistered<IScheduleProvider>());
        Assert.True(loaded.Registry.IsRegistered<IServiceRegistry>());
        Assert.DoesNotContain(_lines, line => line.Contains("内存未释放", StringComparison.Ordinal));
    }
}
