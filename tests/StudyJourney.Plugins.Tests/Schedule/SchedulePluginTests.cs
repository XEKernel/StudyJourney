using StudyJourney.Plugin.Schedule;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// <see cref="SchedulePlugin"/> 的装配断言：初始化只注册、槽位挂载走 <c>PostToUi</c>、停止幂等。
/// </summary>
public sealed class SchedulePluginTests
{
    [Fact]
    public void Id_与plugin_json一致()
    {
        Assert.Equal("schedule", new SchedulePlugin().Id);
    }

    [Fact]
    public async Task InitializeAsync_注册IScheduleProvider并挂菜单与次区槽位()
    {
        using var host = new TempHost();
        host.CopyRealSample();
        var shell = new FakeShellService();
        var context = new FakePluginContext(shell, new FakeStorage(host.Root));
        var plugin = new SchedulePlugin();

        var result = await plugin.InitializeAsync(context);

        Assert.True(result.IsOk, result.Message);
        Assert.NotNull(context.Registry.Get<IScheduleProvider>());

        var menu = Assert.Single(shell.MenuItems);
        Assert.Equal("schedule.editor", menu.Id);
        Assert.Equal("课表…", menu.Header);
        Assert.Equal(20, menu.Order);

        // 插件在 Avalonia 启动前加载，控件必须经 PostToUi 才创建/挂载。
        Assert.True(shell.PostToUiCount > 0);
        var slot = Assert.Single(shell.SlotContents);
        Assert.Equal(ShellSlots.Secondary, slot.SlotId);
        Assert.IsType<ScheduleBar>(slot.Content);
    }

    [Fact]
    public async Task InitializeAsync_课表文件缺失也不阻断启动()
    {
        using var host = new TempHost();
        var context = new FakePluginContext(new FakeShellService(), new FakeStorage(host.Root));

        var result = await new SchedulePlugin().InitializeAsync(context);

        Assert.True(result.IsOk);
        Assert.Empty(context.Logger.Errors);

        // 空课表也必须有 provider（否则主窗拿不到"今天没课"这个结论）。
        var provider = context.Registry.Get<IScheduleProvider>();
        Assert.NotNull(provider);
        var today = await provider.GetTodayAsync();
        Assert.True(today.IsOk);
        Assert.Empty(today.Value!.Periods);
    }

    [Fact]
    public async Task InitializeAsync_没有服务注册表时返回失败而不是抛()
    {
        using var host = new TempHost();
        var context = new FakePluginContext(new FakeShellService(), new FakeStorage(host.Root));
        // 用一个"什么都不返回"的上下文模拟加载器没给 IServiceRegistry。
        var broken = new BrokenContext(context);

        var result = await new SchedulePlugin().InitializeAsync(broken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ServiceMissing, result.Code);
    }

    [Fact]
    public async Task StopAsync_可重复调用且不抛()
    {
        using var host = new TempHost();
        var context = new FakePluginContext(new FakeShellService(), new FakeStorage(host.Root));
        var plugin = new SchedulePlugin();

        Assert.True((await plugin.InitializeAsync(context)).IsOk);
        Assert.True((await plugin.StartAsync()).IsOk);

        await plugin.StopAsync();
        await plugin.StopAsync();      // 幂等：加载器卸载与退出都会调
    }

    [Fact]
    public async Task StopAsync_未初始化也能安全调用()
    {
        await new SchedulePlugin().StopAsync();
    }

    /// <summary>只把 <see cref="IPluginContext.GetService{T}"/> 变成恒 null 的上下文。</summary>
    private sealed class BrokenContext(FakePluginContext inner) : IPluginContext
    {
        public T? GetService<T>() where T : class => null;

        public IEventBus GetEventBus() => inner.GetEventBus();

        public StudyJourney.Sdk.Context.IPluginConfig GetConfig() => inner.GetConfig();

        public StudyJourney.Sdk.Context.IPluginStorage GetStorage() => inner.GetStorage();

        public StudyJourney.Sdk.Context.IPluginLogger GetLogger() => inner.GetLogger();

        public IShellService GetUI() => inner.GetUI();
    }
}
