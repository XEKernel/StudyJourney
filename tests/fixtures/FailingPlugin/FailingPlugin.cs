using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Failing;

/// <summary>
/// 加载器失败分支夹具（见 csproj 注释）。行为由**自己目录下的 <c>fail-mode.txt</c>** 驱动：
/// 用文件而不是环境变量/静态字段，是因为插件跑在自己的 ALC 里，
/// 默认 ALC 里设的标志它看不见；而每个测试都会复制一份独立插件目录。
/// </summary>
public sealed class FailingPlugin : IPlugin
{
    /// <summary>与 plugin.json 的 id 一致。</summary>
    public string Id => "failing-plugin";

    /// <inheritdoc />
    public Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var mode = ReadMode();

        if (mode.StartsWith("throw", StringComparison.Ordinal))
            throw new InvalidOperationException("夹具：InitializeAsync 故意抛异常");

        // 先注册一个服务：这样"失败后是否回滚"才可观测（§3.2 E14 的回滚断言）。
        var registry = context.GetService<IServiceRegistry>();
        var registered = registry?.Register<IScheduleProvider>(new FailingScheduleProvider());
        if (registered is { IsOk: false })
            return Task.FromResult(registered);

        return mode.StartsWith("init", StringComparison.Ordinal)
            ? Task.FromResult(Result.Fail("fixture-init-failed", "夹具：InitializeAsync 故意返回失败"))
            : Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
        => ReadMode().StartsWith("start", StringComparison.Ordinal)
            ? Task.FromResult(Result.Fail("fixture-start-failed", "夹具：StartAsync 故意返回失败"))
            : Task.FromResult(Result.Ok());

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static string ReadMode()
    {
        var directory = Path.GetDirectoryName(typeof(FailingPlugin).Assembly.Location);
        var path = Path.Combine(directory ?? ".", "fail-mode.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }
}

/// <summary>夹具用的假课表能力（注册后再失败，用来验证加载器的按 owner 回滚）。</summary>
internal sealed class FailingScheduleProvider : IScheduleProvider
{
    public Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<TodaySchedule>.Ok(new TodaySchedule
        {
            Date = date,
            EffectiveDayOfWeek = 1,
            IsMakeupDay = false,
            Periods = Array.Empty<ScheduledPeriod>(),
        }));

    public Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default)
        => GetDayAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    public int ResolveEffectiveDayOfWeek(DateOnly date) => 1;
}
