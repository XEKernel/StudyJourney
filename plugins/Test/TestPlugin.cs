using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Test;

/// <summary>
/// M0 的最小插件（plugin id = <c>test</c>）。**它没有任何业务**，唯一使命是证明加载链路：
/// 扫描 → manifest → ALC → <see cref="IPlugin"/> → Initialize → 注册服务 → Start → Stop → 卸载。
///
/// 三条刻意的写法：
/// <list type="bullet">
/// <item>只通过 <see cref="IPluginContext"/> 拿能力，不 new 任何核心对象；</item>
/// <item><c>_ = typeof(Avalonia.Controls.TextBlock)</c> 强制解析 Avalonia（ALC 白名单的端到端验证）；</item>
/// <item>借一次自己的配置节（<c>TestField</c>）把"配置分发链路"也走通（读到的值打进日志）。</item>
/// </list>
/// </summary>
public sealed class TestPlugin : IPlugin
{
    private bool _stopped;

    /// <summary>与 <c>plugin.json</c> 的 <c>id</c> 一致（不一致会被加载器 id-mismatch 拒载）。</summary>
    public string Id => "test";

    /// <inheritdoc />
    public Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        // ⚠ 只取类型、不实例化（不碰 UI 线程）。A0-15 已用探针覆盖同一件事，
        // 这里留着是为了让"真实插件目录 → 真实 ALC → 共享 Avalonia"这条链也有端到端证据。
        _ = typeof(Avalonia.Controls.TextBlock);

        var registry = context.GetService<IServiceRegistry>();
        if (registry is null)
            return Task.FromResult(Result.Fail(ErrorCodes.ServiceMissing, "上下文未提供 IServiceRegistry"));

        // 注意：走的是"绑定到本插件"的注册表门面 → owner = "test"，卸载时按 owner 清。
        var registered = registry.Register<IScheduleProvider>(new TestScheduleProvider());
        if (registered.IsFailure)
            return Task.FromResult(registered);

        // 配置分发：只认识自己 configFields 里的 TestField。
        var section = context.GetConfig().ReadSection();
        var logger = context.GetLogger();
        logger.Info(section is null
            ? "TestField 未配置（使用默认值）"
            : $"TestField = {section["TestField"]?.ToJsonString() ?? "null"}");

        logger.Info("已注册 IScheduleProvider（M0 夹具）");
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Ok());

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // M0 没有任何 IDisposable 注册项（服务由加载器按 owner 清），所以这里是幂等空实现。
        _stopped = true;
        return Task.CompletedTask;
    }

    /// <summary>供测试断言 StopAsync 幂等（不参与业务）。</summary>
    public bool IsStopped => _stopped;
}
