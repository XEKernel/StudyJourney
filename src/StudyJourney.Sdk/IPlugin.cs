namespace StudyJourney.Sdk;

/// <summary>
/// 插件入口。**一个插件程序集里必须恰好有一个实现**（0 个或 ≥2 个 → plugin-type-ambiguous）。
///
/// 两阶段启动（CORE_ARCHITECTURE §3.1）：
/// <see cref="InitializeAsync"/> 只做**注册**，<see cref="StartAsync"/> 才**开工**。
/// 加载器保证"全部 Initialize 完成后"才逐个 Start，所以插件可以在 Start 里安全地
/// <c>GetService&lt;IScheduleProvider&gt;()</c>。
/// </summary>
public interface IPlugin
{
    /// <summary>插件标识，**必须与 plugin.json 的 id 完全一致**（否则 id-mismatch 拒载）。</summary>
    /// <remarks>消费者：PluginLoader（加载期一致性校验）</remarks>
    string Id { get; }

    /// <summary>
    /// 注册阶段：注册服务、设置页、菜单/托盘/热键声明、读配置。
    /// **不启动后台任务、不打开窗口。**
    /// </summary>
    /// <param name="context">插件唯一的依赖入口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败 → 该插件标记 InitFailed，**其它插件继续加载**。</returns>
    /// <remarks>消费者：PluginLoader（第 11 步）</remarks>
    Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动阶段：起定时器、起后台服务、订阅事件、建窗口。
    /// 失败时加载器会调 <see cref="StopAsync"/> 回滚已注册项。
    /// </summary>
    /// <returns>失败 → StartFailed。</returns>
    /// <remarks>消费者：PluginLoader（第 13 步；超时 10 秒视为失败但不卸载）</remarks>
    Task<Result> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止并释放：停定时器、停服务、**释放 Initialize/Start 里拿到的全部 IDisposable**
    /// （菜单项、托盘项、热键、槽位内容、事件订阅）。
    /// </summary>
    /// <remarks>
    /// 消费者：PluginLoader（卸载 / 退出 / Start 失败回滚）。
    /// 不返回 Result：卸载是尽力而为，插件失败也必须继续往下走，否则程序关不掉。
    /// 加载器超时 3 秒后跳过该插件，不阻断退出。
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);
}
