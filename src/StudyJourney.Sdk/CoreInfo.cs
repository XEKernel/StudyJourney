namespace StudyJourney.Sdk;

/// <summary>
/// 契约版本信息。**这是 <c>plugin.json</c> 里 <c>coreApi</c> 的比对基准**（D2：只比对主版本）。
///
/// 为什么放在 Sdk 而不是 Core：
/// 插件要在**不引用 Core** 的前提下知道自己是否兼容（例如自检、错误提示），
/// 版本号属于契约的一部分，必须和接口放在同一侧。
/// </summary>
public static class CoreInfo
{
    /// <summary>当前契约版本，形如 <c>"1.0"</c>。</summary>
    /// <remarks>消费者：PluginLoader（写日志）、--selftest（输出）、设置页"关于"</remarks>
    public const string ApiVersion = "1.0";

    /// <summary>当前契约主版本号。**只有它参与兼容性判定。**</summary>
    /// <remarks>消费者：PluginLoader（core-api-mismatch 判定）</remarks>
    public const int ApiMajorVersion = 1;

    /// <summary>
    /// 判定插件声明的 <paramref name="manifestCoreApi"/> 是否与当前核心兼容。
    ///
    /// 规则（D2 已定）：**只比对主版本**。<c>"1.x"</c> / <c>"1"</c> / <c>"1.9.9"</c> 都视为与 1.x 兼容；
    /// 次版本与修订版不参与判定——比它们会制造"插件明明能用却拒载"的假故障。
    /// </summary>
    /// <param name="manifestCoreApi">manifest 里声明的 coreApi，例如 <c>"1.x"</c>。</param>
    /// <returns>主版本一致返回 true；为空、格式非法、主版本不同返回 false。</returns>
    /// <remarks>消费者：PluginLoader（manifest 校验阶段，早于建 ALC）</remarks>
    public static bool IsApiCompatible(string? manifestCoreApi)
    {
        if (string.IsNullOrWhiteSpace(manifestCoreApi)) return false;

        // 只取第一段，允许多余的分段（"1.2.3" 与 "1.x" 同等对待）
        var head = manifestCoreApi.Trim();
        var dot = head.IndexOf('.');
        if (dot >= 0) head = head[..dot];

        return int.TryParse(head, out var major) && major == ApiMajorVersion;
    }
}
