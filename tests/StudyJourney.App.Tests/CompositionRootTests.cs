using StudyJourney.App;
using StudyJourney.Core.Plugins;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.App.Tests;

/// <summary>
/// S10：组合根装配（CORE_ARCHITECTURE §3.1 第 2–7 步）。
/// 断言的是"装配出了哪些东西 + 什么东西 M0 刻意没有"（无壳、无通知服务）。
/// </summary>
public sealed class CompositionRootTests : IDisposable
{
    private readonly string _root;

    public CompositionRootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sj-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 忽略 */ }
    }

    [Fact(DisplayName = "S10-04 组合根注册核心服务：IServiceRegistry + IEventBus（owner = core）")]
    public async Task Build_RegistersCoreServices()
    {
        var logs = new List<string>();
        var host = HostComposition.Build(
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"),
            logSink: line => { lock (logs) logs.Add(line); });

        try
        {
            Assert.True(host.Registry.IsRegistered<IServiceRegistry>());
            Assert.True(host.Registry.IsRegistered<IEventBus>());
            Assert.Same(host.Registry, host.Registry.Get<IServiceRegistry>());
            Assert.Same(host.Bus, host.Registry.Get<IEventBus>());
            Assert.NotNull(host.Config);
            Assert.NotNull(host.Loader);

            Assert.Equal(ServiceRegistry.CoreOwner,
                host.Registry.Registrations.Single(r => r.ServiceType == typeof(IEventBus)).Owner);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact(DisplayName = "S10-05 M0 刻意不装配壳与通知：IShellService / INotificationService 均未注册")]
    public async Task Build_DoesNotRegisterShellInM0()
    {
        var host = HostComposition.Build(
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"));

        try
        {
            Assert.False(host.Registry.IsRegistered<IShellService>());
            Assert.False(host.Registry.IsRegistered<INotificationService>());
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
