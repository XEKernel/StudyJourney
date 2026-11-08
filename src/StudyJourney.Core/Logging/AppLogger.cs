using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Logging;

/// <summary>
/// M0 的**极简**日志器（CORE_ARCHITECTURE §6.9 的 M0 子集）：控制台一行一条，可注入 <c>sink</c> 供测试断言。
///
/// ⚠ 刻意**不搬** v3.3.0 的 <c>AppLogger</c>（文件轮转 / 按天分文件 / 全局 <c>Flush</c>）——
/// 那是 M1 的"日志"组件（§1.2）。M0 只需要"有地方看得到插件加载结论"，实现大了反而要维护两套。
///
/// 输出格式：<c>HH:mm:ss.fff 级别 [pluginId] 消息</c>。插件日志器由 <see cref="ForPlugin"/> 派生，
/// 前缀固定为 <c>[pluginId]</c>（§6.9：任何插件日志都必须能看出是谁写的）。
/// </summary>
public sealed class AppLogger : IPluginLogger
{
    private readonly object _gate = new();
    private readonly Action<string>? _sink;
    private readonly string _prefix;

    /// <param name="sink">输出目标；<c>null</c> = 控制台。测试注入收集器。</param>
    /// <param name="prefix">行前缀（插件派生时传 <c>[id] </c>）。</param>
    public AppLogger(Action<string>? sink = null, string? prefix = null)
    {
        _sink = sink ?? Console.WriteLine;
        _prefix = prefix ?? string.Empty;
    }

    /// <summary>派生带 <c>[pluginId]</c> 前缀的插件日志器。</summary>
    public AppLogger ForPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new AppLogger(_sink, $"[{pluginId}] ");
    }

    /// <inheritdoc />
    public void Trace(string message) => Write("TRACE", message);

    /// <inheritdoc />
    public void Debug(string message) => Write("DEBUG", message);

    /// <inheritdoc />
    public void Info(string message) => Write("INFO", message);

    /// <inheritdoc />
    public void Warn(string message) => Write("WARN", message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("ERROR", message);
            return;
        }

        // §6.9：异常必须带类型 + 堆栈，否则线上只能看到"出错了"。
        Write("ERROR", $"{message} | {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {_prefix}{message}";
        lock (_gate)
        {
            try { _sink?.Invoke(line); }
            catch { /* 日志器自身绝不抛：否则"记录失败"会变成"业务失败" */ }
        }
    }
}
