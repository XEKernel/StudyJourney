using StudyJourney.Sdk;

namespace StudyJourney.Plugin.A015Probe;

/// <summary>
/// A0-15 探针插件：一个程序集里**恰好一个** <see cref="IPlugin"/> 实现（0 或 ≥2 会 plugin-type-ambiguous）。
/// </summary>
public sealed class ProbePlugin : IPlugin
{
    /// <summary>与 <c>plugin.json</c> 的 <c>id</c> 一致。</summary>
    public string Id => "a015-probe";

    /// <summary>
    /// 注册阶段：先碰一次 Avalonia 类型（触发程序集解析），再把 <see cref="IScheduleProvider"/> 注册进注册表。
    /// </summary>
    public Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        // ⚠ 本文件最重要的一行：**只取类型，不实例化**（不碰 UI 线程、不建控件）。
        // 作用：强制运行时解析 Avalonia.Controls 程序集。
        //   · 白名单正确 → Load 返回 null → 由默认 ALC 提供 → 与核心同一份 Type；
        //   · 白名单漏了 Avalonia → 从插件目录加载第二份 → A0-15-b 立刻失败。
        _ = typeof(Avalonia.Controls.TextBlock);

        var registry = context.GetService<IServiceRegistry>();
        if (registry is null)
        {
            return Task.FromResult(
                Result.Fail(ErrorCodes.ServiceMissing, "测试上下文未提供 IServiceRegistry"));
        }

        return Task.FromResult(registry.Register<IScheduleProvider>(new ProbeScheduleProvider()));
    }

    /// <summary>无后台任务，直接成功。</summary>
    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Ok());

    /// <summary>没有需要释放的东西。</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 供 A0-15-b 通过反射取"**插件那一侧**看到的 TextBlock 类型"。
    /// 返回的 <see cref="Type"/> 所属程序集若与核心侧不同，说明加载了两份 Avalonia。
    /// </summary>
    public static Type TextBlockType() => typeof(Avalonia.Controls.TextBlock);
}
