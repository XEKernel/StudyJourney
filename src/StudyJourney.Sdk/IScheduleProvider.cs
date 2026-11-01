using StudyJourney.Sdk.Dto;

namespace StudyJourney.Sdk;

/// <summary>
/// 课表能力（CORE_ARCHITECTURE §2.6）。**全项目被消费次数最多的跨插件接口**（5 个消费方）。
///
/// 一致性约定（§2.6.5）：
/// <list type="bullet">
/// <item>返回的是某一时刻的快照，调用方不得长期持有；</item>
/// <item>快照与 <see cref="ScheduleChangedEvent.Version"/> 绑定，缓存必须一并存版本号；</item>
/// <item>同一次事件处理内多次调用应返回同一版本（课表插件在自增版本**之后**才发布事件）。</item>
/// </list>
///
/// ⚠ **订阅课表变更不走本接口**，用 <see cref="IEventBus"/> 订阅 <see cref="ScheduleChangedEvent"/>——
/// 一套机制，没有例外（E1）。
/// </summary>
public interface IScheduleProvider
{
    /// <summary>取某日的实际课表。返回值已含**调休映射**（<see cref="TodaySchedule.EffectiveDayOfWeek"/>）。</summary>
    /// <param name="date">要查的日期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>消费者：主窗（今日摘要）、提醒（Tick 逐节判）、自动化（规则命中）、远程（/api/schedule）</remarks>
    Task<Result<TodaySchedule>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>取今天的课表。等价于 <c>GetDayAsync(DateOnly.FromDateTime(DateTime.Today))</c>。</summary>
    /// <remarks>消费者：与上同（5 个消费方都在调"今天"，单列出来是因为调用点最多）</remarks>
    Task<Result<TodaySchedule>> GetTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 把日期解析成"那天实际按星期几执行"（1=周一 … 7=周日，周日归一化为 7）。
    /// **纯函数**：不读盘、不查缓存，可被自检直接断言。
    /// </summary>
    /// <remarks>
    /// 消费者：提醒（生成去重键）、自动化（TriggerDays 匹配）、远程（网页星期标题）、主窗（调休提示）。
    /// 之所以单独暴露而不是只放在 <see cref="TodaySchedule"/> 里：
    /// 自动化要算"未来几天的规则是否命中"，需要在不取课表的情况下做日期映射。
    /// </remarks>
    int ResolveEffectiveDayOfWeek(DateOnly date);
}
