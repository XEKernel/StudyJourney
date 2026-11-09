using Avalonia.Controls;
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

    [Fact(DisplayName = "M1-06 M1 装配了壳与通知：IShellService（owner=core）与 INotificationService 均已注册")]
    public async Task Build_RegistersShellAndNotifications()
    {
        var host = HostComposition.Build(
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"));

        try
        {
            Assert.True(host.Registry.IsRegistered<IShellService>());
            Assert.True(host.Registry.IsRegistered<INotificationService>());

            // 壳登记在核心名下（插件不能事后顶掉它：reserved-service）
            Assert.Equal(
                Core.Services.ServiceRegistry.CoreOwner,
                host.Registry.Registrations.Single(r => r.ServiceType == typeof(IShellService)).Owner);

            // 取出来的壳可用：未知槽位报 slot-unknown，而不是抛
            var shell = host.Registry.GetRequired<IShellService>();
            Assert.True(shell.IsOk);

            var unknown = shell.Value!.RegisterSlotContent("main.nope", new Border());
            Assert.True(unknown.IsFailure);
            Assert.Equal("slot-unknown", unknown.Code);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact(DisplayName = "M1-07 插件抢先注册核心保留的 IShellService → reserved-service（不覆盖核心的）")]
    public async Task Plugin_CannotReplaceShell()
    {
        var host = HostComposition.Build(
            Path.Combine(_root, "plugins"),
            Path.Combine(_root, "data"));

        try
        {
            // 模拟插件用带 owner 的注册入口去抢保留服务
            var attempt = host.Registry.RegisterOwned<IShellService>("rogue", host.Shell.ForPlugin("rogue"));

            Assert.True(attempt.IsFailure);
            Assert.Equal("reserved-service", attempt.Code);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
