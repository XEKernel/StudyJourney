namespace StudyJourney.Sdk.Context;

/// <summary>
/// 插件日志器（CORE_ARCHITECTURE §2.9 / §6.9）。输出自动带 <c>[&lt;pluginId&gt;]</c> 前缀。
///
/// 规则（§6.9）：绝不记录密码 / 密码哈希 / tokens.json 内容；异常必须带堆栈；
/// 同一日志不许在循环里刷（用"首次 + 每 N 次汇总"）。
/// </summary>
public interface IPluginLogger
{
    /// <summary>高频内部状态（生产不落盘）。</summary>
    /// <remarks>消费者：自动化（逐 Tick 判定过程）</remarks>
    void Trace(string message);

    /// <summary>流程分支（默认不落盘，core.json 可开）。</summary>
    /// <remarks>消费者：课表（"今天按周五上"）</remarks>
    void Debug(string message);

    /// <summary>生命周期事件（启动、加载、更新检查）。</summary>
    /// <remarks>消费者：全部插件</remarks>
    void Info(string message);

    /// <summary>可恢复异常（配置缺失、热键冲突、降级行为）。</summary>
    /// <remarks>消费者：全部插件</remarks>
    void Warn(string message);

    /// <summary>需要人介入的失败（落盘失败、连续异常）。</summary>
    /// <param name="message">人可读描述。</param>
    /// <param name="exception">原始异常，用于堆栈。</param>
    /// <remarks>消费者：全部插件</remarks>
    void Error(string message, Exception? exception = null);
}
