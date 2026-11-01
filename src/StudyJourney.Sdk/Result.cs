namespace StudyJourney.Sdk;

/// <summary>
/// 操作结果（无返回值）。
///
/// 这是**核心 ↔ 插件边界上唯一允许的失败表达**：边界方法返回它，不抛异常（E4）。
/// 插件内部照常 <c>throw</c>，由核心在边界处捕获并转成 <see cref="Fail"/>。
/// </summary>
public sealed record Result
{
    /// <summary>是否成功。</summary>
    /// <remarks>消费者：加载器（决定是否继续 Start）、调用方（决定是否提示用户）</remarks>
    public bool IsOk { get; init; }

    /// <summary>机器可读错误码，取值见 <see cref="ErrorCodes"/>。</summary>
    /// <remarks>
    /// 消费者：加载器（写 plugins/.state.json 的 lastLoadError）、
    /// 设置页插件列表（红字）、日志（可聚合统计）
    /// </remarks>
    public string Code { get; init; } = string.Empty;

    /// <summary>人可读错误信息（中文，直接给老师看）。</summary>
    /// <remarks>消费者：设置页插件列表（悬停提示）、通知服务（出错提示）</remarks>
    public string Message { get; init; } = string.Empty;

    /// <summary>原始异常，仅用于日志，**不用于展示**。</summary>
    /// <remarks>消费者：AppLogger（带堆栈落盘）</remarks>
    public Exception? Exception { get; init; }

    /// <summary>失败的便捷判断。</summary>
    /// <remarks>消费者：所有调用方（比 <c>!IsOk</c> 可读）</remarks>
    public bool IsFailure => !IsOk;

    /// <summary>构造成功结果。</summary>
    public static Result Ok() => new() { IsOk = true };

    /// <summary>构造失败结果。</summary>
    public static Result Fail(string code, string message, Exception? exception = null)
        => new() { IsOk = false, Code = code, Message = message, Exception = exception };
}

/// <summary>
/// 带返回值的操作结果。
///
/// ⚠ 与 <see cref="Result"/> 是**两个独立类型**（不是继承），
/// 因为"有值"与"无值"在调用点必须一眼分清——继承会让 <c>Result&lt;T&gt;</c>
/// 被当成 <c>Result</c> 传递，丢掉 <see cref="Value"/> 却编译通过。
/// </summary>
public sealed record Result<T>
{
    /// <summary>是否成功。</summary>
    /// <remarks>消费者：所有调用方</remarks>
    public bool IsOk { get; init; }

    /// <summary>成功时的值；失败时为 <c>default</c>。</summary>
    /// <remarks>消费者：所有取值的调用方（必须先判 IsOk）</remarks>
    public T? Value { get; init; }

    /// <summary>机器可读错误码，取值见 <see cref="ErrorCodes"/>。</summary>
    /// <remarks>消费者：加载器、设置页、日志</remarks>
    public string Code { get; init; } = string.Empty;

    /// <summary>人可读错误信息。</summary>
    /// <remarks>消费者：界面提示</remarks>
    public string Message { get; init; } = string.Empty;

    /// <summary>原始异常，仅用于日志。</summary>
    /// <remarks>消费者：AppLogger</remarks>
    public Exception? Exception { get; init; }

    /// <summary>失败的便捷判断。</summary>
    /// <remarks>消费者：所有调用方</remarks>
    public bool IsFailure => !IsOk;

    /// <summary>构造成功结果。</summary>
    public static Result<T> Ok(T value) => new() { IsOk = true, Value = value };

    /// <summary>构造失败结果。</summary>
    public static Result<T> Fail(string code, string message, Exception? exception = null)
        => new() { IsOk = false, Code = code, Message = message, Exception = exception };
}
