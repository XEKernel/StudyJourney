namespace StudyJourney.Core.Plugins;

/// <summary>插件状态机（CORE_ARCHITECTURE §3.0）。失败刻意分成四种：恢复动作完全不同。</summary>
public enum PluginState
{
    /// <summary>扫到了 manifest，正在解析。</summary>
    Discovered,

    /// <summary>manifest 缺失/非法/id 与目录名不符/字段冲突（终态）。</summary>
    ManifestInvalid,

    /// <summary>依赖已满足、拓扑序已定。</summary>
    Sorted,

    /// <summary>ALC 加载失败、找不到唯一 IPlugin、coreApi 不匹配、entry 缺失（终态）。</summary>
    LoadFailed,

    /// <summary>程序集已加载、实例已创建。</summary>
    Loaded,

    /// <summary>InitializeAsync 返回失败或抛异常（终态，已注册项已回滚）。</summary>
    InitFailed,

    /// <summary>注册完成。</summary>
    Initialized,

    /// <summary>StartAsync 失败（已回滚）。</summary>
    StartFailed,

    /// <summary>运行中。</summary>
    Started,

    /// <summary>已停止。</summary>
    Stopped,

    /// <summary>ALC 已卸载（或卸载失败，只记 Warn）。</summary>
    Unloaded,
}

/// <summary>单个插件的加载结论（设置页「插件」列表与 smoke 输出都用它）。</summary>
/// <param name="PluginId">manifest 的 id（manifest 读不出来时用目录名）。</param>
/// <param name="State">当前状态。</param>
/// <param name="Code">失败时的错误码（<see cref="Sdk.ErrorCodes"/>）；成功为 <c>null</c>。</param>
/// <param name="Message">失败原因（中文，可直接给用户看）。</param>
public sealed record PluginLoadResult(string PluginId, PluginState State, string? Code = null, string? Message = null)
{
    /// <summary>是否正在运行。</summary>
    public bool IsRunning => State == PluginState.Started;
}

/// <summary>一次完整加载的结果快照。</summary>
/// <param name="Plugins">按加载顺序（拓扑序）排列。</param>
public sealed record PluginLoadReport(IReadOnlyList<PluginLoadResult> Plugins)
{
    /// <summary>成功进入 Started 的插件数。</summary>
    public int RunningCount => Plugins.Count(p => p.IsRunning);
}
