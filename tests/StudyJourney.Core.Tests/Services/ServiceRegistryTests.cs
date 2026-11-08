using StudyJourney.Core.Services;
using StudyJourney.Core.Tests.TestSupport;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Core.Tests.Services;

/// <summary>
/// S9：服务注册表（CORE_ARCHITECTURE §4.1–§4.3）。
/// 四条硬约束逐条有用例：封板拒收 / 重复不覆盖 / 保留服务 / owner 批量清理。
/// </summary>
public sealed class ServiceRegistryTests
{
    private interface IFakeService
    {
    }

    private interface IOtherService
    {
    }

    private sealed class FakeService : IFakeService
    {
    }

    private sealed class AnotherFakeService : IFakeService
    {
    }

    private sealed class OtherService : IOtherService
    {
    }

    private readonly RecordingLogger _log = new();

    [Fact(DisplayName = "S9-01 构造时把自己注册进注册表（插件第一步就能拿到）")]
    public void Constructor_SelfRegisters()
    {
        var registry = new ServiceRegistry(_log);

        Assert.True(registry.IsRegistered<IServiceRegistry>());
        Assert.Same(registry, registry.Get<IServiceRegistry>());
    }

    [Fact(DisplayName = "S9-02 重复注册不覆盖：返回 service-conflict，先注册者保留")]
    public void DuplicateRegistration_DoesNotOverwrite()
    {
        var registry = new ServiceRegistry(_log);
        var first = new FakeService();
        var second = new AnotherFakeService();

        Assert.True(registry.Register<IFakeService>(first).IsOk);
        var conflict = registry.Register<IFakeService>(second);

        Assert.False(conflict.IsOk);
        Assert.Equal(ErrorCodes.ServiceConflict, conflict.Code);
        Assert.Same(first, registry.Get<IFakeService>());
    }

    [Fact(DisplayName = "S9-03 封板后拒收：registry-sealed，且二次 Seal 幂等")]
    public void Seal_RejectsFurtherRegistrations()
    {
        var registry = new ServiceRegistry(_log);
        registry.Seal();
        registry.Seal();   // 幂等：不抛

        var result = registry.Register<IFakeService>(new FakeService());

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCodes.RegistrySealed, result.Code);
        Assert.False(registry.IsRegistered<IFakeService>());
        Assert.True(registry.IsSealed);
    }

    [Fact(DisplayName = "S9-04 插件注册核心保留服务：reserved-service（核心自己不受限）")]
    public void ReservedService_RejectedForPluginsAllowedForCore()
    {
        var registry = new ServiceRegistry(_log);

        // 核心（owner = core）可以注册（例如组合根注册 IEventBus）
        Assert.True(registry.Register<IEventBus>(new EventBus()).IsOk);

        // 插件不行
        var result = registry.RegisterOwned("countdown", (IServiceRegistry)new ServiceRegistry());

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCodes.ReservedService, result.Code);
        Assert.Contains("countdown", result.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S9-05 GetRequired 未注册 → service-missing；IsRegistered 如实回答")]
    public void GetRequired_Missing_ReturnsServiceMissing()
    {
        var registry = new ServiceRegistry(_log);

        var missing = registry.GetRequired<IFakeService>();

        Assert.False(missing.IsOk);
        Assert.Equal(ErrorCodes.ServiceMissing, missing.Code);
        Assert.False(registry.IsRegistered<IFakeService>());
        Assert.Null(registry.Get<IFakeService>());
    }

    [Fact(DisplayName = "S9-06 RemoveAllOf 只清指定 owner，核心与其它插件的登记不动")]
    public void RemoveAllOf_RemovesOnlyThatOwner()
    {
        var registry = new ServiceRegistry(_log);
        var countdownService = new FakeService();
        var remoteService = new OtherService();

        Assert.True(registry.RegisterOwned<IFakeService>("countdown", countdownService).IsOk);
        Assert.True(registry.RegisterOwned<IOtherService>("remote", remoteService).IsOk);
        Assert.True(registry.Register<IEventBus>(new EventBus()).IsOk);

        var removed = registry.RemoveAllOf("countdown");

        Assert.Equal(new[] { typeof(IFakeService) }, removed.ToArray());
        Assert.Null(registry.Get<IFakeService>());
        Assert.Same(remoteService, registry.Get<IOtherService>());
        Assert.True(registry.IsRegistered<IEventBus>());
        Assert.True(registry.IsRegistered<IServiceRegistry>());
    }

    [Fact(DisplayName = "S9-07 Registrations 快照：服务类型 + owner，供 --smoke 打印")]
    public void Registrations_ExposesOwnerMap()
    {
        var registry = new ServiceRegistry(_log);
        registry.RegisterOwned<IFakeService>("countdown", new FakeService());

        var snapshot = registry.Registrations;

        Assert.Contains(snapshot, r => r.ServiceType == typeof(IServiceRegistry) && r.Owner == ServiceRegistry.CoreOwner);
        Assert.Contains(snapshot, r => r.ServiceType == typeof(IFakeService) && r.Owner == "countdown");
    }

    [Fact(DisplayName = "S9-08 注册 null 是编程错误：抛 ArgumentNullException（不当成可失败的 Result）")]
    public void RegisterNull_Throws()
    {
        var registry = new ServiceRegistry(_log);

        Assert.Throws<ArgumentNullException>(() => registry.RegisterOwned<IFakeService>("countdown", null!));
    }
}
