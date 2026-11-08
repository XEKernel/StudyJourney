using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Tests.TestSupport;

/// <summary>
/// 记录型日志器：把插件/核心写出的每一行留在内存里，供断言"该告警的真的告警了"。
/// 刻意**不做去重、不做过滤**——测试要看的就是原文。
/// </summary>
internal sealed class RecordingLogger : IPluginLogger
{
    public List<(string Level, string Message)> Entries { get; } = new();

    public IReadOnlyList<string> Messages(string level)
        => Entries.Where(e => e.Level == level).Select(e => e.Message).ToList();

    public bool Contains(string level, string fragment)
        => Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.Ordinal));

    public void Trace(string message) => Entries.Add(("trace", message));

    public void Debug(string message) => Entries.Add(("debug", message));

    public void Info(string message) => Entries.Add(("info", message));

    public void Warn(string message) => Entries.Add(("warn", message));

    public void Error(string message, Exception? exception = null)
        => Entries.Add(("error", exception is null ? message : message + " | " + exception.GetType().Name));
}
