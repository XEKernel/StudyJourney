using StudyJourney.Plugin.Schedule.Models;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// <see cref="IScheduleProvider"/> 的实现：把 <c>schedule.json</c> 的 <see cref="ScheduleEntry"/>
/// 投影成契约 DTO <see cref="ScheduledPeriod"/>。
///
/// 性能：主窗**每秒**会被刷新链调好几次"今天"（当前课 / 下一节 / 剩余时间各一次），
/// 每次全表扫描 + 排序 + 分配 → 空载 CPU 的主要来源。这里按日期缓存 200ms
/// （沿用 v3.3.0 的 TTL），Reload 时立即失效。
/// 200ms 而不是"只靠事件失效"：课表编辑器会直接改数据、不一定立刻发事件，
/// 短 TTL 兜住这条路径，而 0.2 秒的延迟对"老师改课表"完全无感。
///
/// 缓存条目**绑定版本号**（§2.6.5）：同一事件处理内多次调用必须拿到同一版本。
/// </summary>
internal sealed class ScheduleProvider : IScheduleProvider
{
    /// <summary>查询缓存有效期（毫秒）。</summary>
    private const int CacheTtlMs = 200;

    /// <summary>缓存键上限（跨天课凌晨会查"昨天"，留点余量防无界增长）。</summary>
    private const int CacheCapacity = 8;

    private readonly ScheduleStore _store;
    private readonly IPluginLogger? _logger;
    private readonly object _gate = new();
    private readonly Dictionary<DateOnly, CacheEntry> _cache = [];

    private ScheduleData _data = new();
    private long _version;

    /// <param name="store">读写句柄（构造后需调用一次 <see cref="Reload"/>）。</param>
    /// <param name="logger">可选日志器；测试可传 <c>null</c>。</param>
    public ScheduleProvider(ScheduleStore store, IPluginLogger? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>当前数据版本号（单调递增，<see cref="ScheduleChangedEvent.Version"/> 用它）。</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    /// 从磁盘重载并**自增版本号**、清空缓存。返回新版本号（调用方拿它发事件）。
    /// </summary>
    public long Reload()
    {
        var data = _store.Load();

        lock (_gate)
        {
            _data = data;
            _cache.Clear();
            var next = _version + 1;
            Volatile.Write(ref _version, next);
            return next;
        }
    }

    /// <inheritdoc />
    public Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // 刻意不检查 cancellationToken：本方法是纯内存快照，没有 I/O 也没有可中断点，
        // 立刻返回比"抛取消"更符合调用方预期（主窗每秒调、自动化每 tick 调）。
        try
        {
            return Task.FromResult(Result<TodaySchedule>.Ok(GetDayCached(date)));
        }
        catch (Exception ex)
        {
            _logger?.Error($"课表查询失败（{date:yyyy-MM-dd}）", ex);
            return Task.FromResult(Result<TodaySchedule>.Fail(ErrorCodes.PluginThrew, "课表查询失败", ex));
        }
    }

    /// <inheritdoc />
    public Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default)
        => GetDayAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    /// <inheritdoc />
    public int ResolveEffectiveDayOfWeek(DateOnly date)
    {
        ScheduleData data;
        lock (_gate)
            data = _data;

        return ScheduleResolver.ResolveEffectiveDayOfWeek(date, data.MakeupDays);
    }

    private TodaySchedule GetDayCached(DateOnly date)
    {
        var stamp = Environment.TickCount64;

        lock (_gate)
        {
            if (_cache.TryGetValue(date, out var hit) &&
                hit.Version == _version &&
                stamp - hit.StampMs < CacheTtlMs)
            {
                return hit.Schedule;
            }

            var schedule = Build(date, _data);

            if (_cache.Count >= CacheCapacity)
                _cache.Clear();

            _cache[date] = new CacheEntry(schedule, _version, stamp);
            return schedule;
        }
    }

    /// <summary>
    /// 把某天的 <see cref="ScheduleEntry"/> 投影成 <see cref="ScheduledPeriod"/>：
    /// 先按调休映射取"实际星期几"，再筛该天、按 <see cref="ScheduledPeriod.Period"/> 升序。
    /// </summary>
    private static TodaySchedule Build(DateOnly date, ScheduleData data)
    {
        var effective = ScheduleResolver.ResolveEffectiveDayOfWeek(date, data.MakeupDays);

        var periods = data.Entries
            .Where(e => e is not null && e.DayOfWeek == effective)
            .Select(e => new ScheduledPeriod
            {
                DayOfWeek = effective,
                Period = e.Period,
                Subject = e.Subject ?? string.Empty,
                StartTimeStr = NormalizeTime(e.StartTimeStr, "08:00"),
                EndTimeStr = NormalizeTime(e.EndTimeStr, "08:45"),
                Type = e.Type,
            })
            // 契约要求按 Period 升序；同节次再按开始时间排，保证输出稳定可断言。
            .OrderBy(p => p.Period)
            .ThenBy(p => p.StartTimeStr, StringComparer.Ordinal)
            .ToList();

        return new TodaySchedule
        {
            Date = date,
            EffectiveDayOfWeek = effective,
            IsMakeupDay = ScheduleResolver.IsMakeupDay(date, data.MakeupDays),
            Periods = periods,
        };
    }

    /// <summary>
    /// 把落盘时间串归一成契约要求的 <c>HH:mm</c>。
    /// 解析不了的原样回传（trim 后），不做静默替换 —— 老师能一眼看出文件里那条是坏的。
    /// </summary>
    private static string NormalizeTime(string? raw, string fallbackWhenMissing)
    {
        if (ScheduleResolver.TryParseTime(raw, out var value))
            return ScheduleResolver.FormatTime(value);

        return string.IsNullOrWhiteSpace(raw) ? fallbackWhenMissing : raw.Trim();
    }

    /// <summary>缓存条目：快照 + 生成时的版本号 + 生成时刻（<see cref="Environment.TickCount64"/>）。</summary>
    private readonly record struct CacheEntry(TodaySchedule Schedule, long Version, long StampMs);
}
