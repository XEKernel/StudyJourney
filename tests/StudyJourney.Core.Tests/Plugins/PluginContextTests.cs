using StudyJourney.Core.Config;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Core.Tests.TestSupport;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using Xunit;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// S9/S10 交界：<see cref="PluginContext"/> 的两个"门面"（注册表 / 总线）。
///
/// 这两条是卸载能否干净的全部依据（§4.2 约束 4 / §4.4 约束 6）：
/// 插件注册的服务必须记在**插件 id**名下，插件订阅的 owner 必须是**上下文**，
/// 否则 <c>RemoveAllOf</c> / <c>DisposeOwner</c> 清的是一批不存在的名字，卸载后回调
/// 打进已卸载 ALC → <c>AccessViolationException</c>，且现场在用户的机器上。
/// </summary>
public sealed class PluginContextTests : IDisposable
{
    private interface IFakeService;

    private sealed class FakeEvent;

    private sealed class FakeService : IFakeService;

    private readonly string _root;
    private readonly RecordingLogger _log = new();

    public PluginContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sj-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 忽略 */ }
    }

    private PluginContext CreateContext(
        out ServiceRegistry registry,
        out EventBus bus,
        string pluginId = "demo",
        IShellService? shell = null)
    {
        registry = new ServiceRegistry(_log);
        bus = new EventBus(_log);

        var config = ConfigStore.CreateInMemory("{\"OnlyMine\": 1, \"NotMine\": 2}", _log)
            .CreateHandle(pluginId, new[] { "OnlyMine" });
        var storage = new PluginStorage(
            pluginId,
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"));

        return new PluginContext(pluginId, registry, bus, config, storage, _log, shell);
    }

    [Fact(DisplayName = "S9-18 插件拿到的是注册表门面：注册记在插件 id 名下，RemoveAllOf 能一次清干净")]
    public void RegistryFacade_AttributesOwnerToPlugin()
    {
        var context = CreateContext(out var registry, out _);
        var pluginRegistry = context.GetService<IServiceRegistry>();

        Assert.NotNull(pluginRegistry);
        Assert.NotSame(registry, pluginRegistry);   // 拿到的是门面，不是核心真身

        Assert.True(pluginRegistry!.Register<IFakeService>(new FakeService()).IsOk);
        Assert.Contains(registry.Registrations, r => r.ServiceType == typeof(IFakeService) && r.Owner == "demo");

        registry.RemoveAllOf("demo");
        Assert.Null(registry.Get<IFakeService>());
        Assert.True(registry.IsRegistered<IServiceRegistry>());   // 核心登记不受影响
    }

    [Fact(DisplayName = "S9-19 插件不能通过门面封板（封板是加载器专属动作）")]
    public void RegistryFacade_SealIsNoOp()
    {
        var context = CreateContext(out var registry, out _);

        context.GetService<IServiceRegistry>()!.Seal();

        Assert.False(registry.IsSealed);
        Assert.True(registry.Register<IFakeService>(new FakeService()).IsOk);
    }

    [Fact(DisplayName = "S9-20 插件订阅走总线门面：owner 是上下文，DisposeOwner(context) 一定清得到")]
    public void EventBusFacade_BindsOwnerToContext()
    {
        var context = CreateContext(out _, out var bus);
        var received = 0;

        context.GetEventBus().Subscribe<FakeEvent>(new object(), _ => received++);   // 插件传了个"错"的 owner
        bus.Publish(new FakeEvent());
        Assert.Equal(1, received);

        bus.DisposeOwner(context.EventOwner);

        bus.Publish(new FakeEvent());
        Assert.Equal(1, received);   // 已清：不再收到
    }

    [Fact(DisplayName = "S9-21 配置句柄：只读得到自己认领的字段")]
    public void ConfigHandle_OnlySeesOwnFields()
    {
        var context = CreateContext(out _, out _);

        var section = context.GetConfig().ReadSection();

        Assert.NotNull(section);
        Assert.Equal(new[] { "OnlyMine" }, section!.AsObject().Select(p => p.Key).ToArray());
    }

    [Fact(DisplayName = "S9-22 存储句柄：data/<id>/ 自动创建，越界路径抛 ArgumentException")]
    public void Storage_CreatesDataDirectory_AndRejectsEscape()
    {
        var context = CreateContext(out _, out _);
        var storage = context.GetStorage();

        Assert.True(Directory.Exists(storage.DataDirectory));
        Assert.EndsWith(Path.Combine("data", "demo"), storage.DataDirectory, StringComparison.Ordinal);

        var inside = storage.ResolvePath("sub/file.txt");
        Assert.StartsWith(storage.DataDirectory, inside, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => storage.ResolvePath("../escape.txt"));
        Assert.Throws<ArgumentException>(() => storage.ResolvePath("..\\escape.txt"));
    }

    [Fact(DisplayName = "S9-23 M0 无壳：GetUI() 抛 NotSupportedException（不做假壳）")]
    public void GetUI_WithoutShell_Throws()
    {
        var context = CreateContext(out _, out _);

        var exception = Assert.Throws<NotSupportedException>(() => context.GetUI());
        Assert.Contains("M1", exception.Message, StringComparison.Ordinal);
    }
}
