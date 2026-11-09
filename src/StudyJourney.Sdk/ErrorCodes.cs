namespace StudyJourney.Sdk;

/// <summary>
/// 错误码常量。**新增错误码必须同时更新 CORE_ARCHITECTURE §6.8 的清单**——
/// 散落的字符串字面量无法聚合统计，等于没有错误码。
/// </summary>
public static class ErrorCodes
{
    // ── 插件加载（manifest / ALC 阶段）──────────────────────────
    /// <summary>manifest 缺字段、字段非法、id 与目录名不符。</summary>
    public const string ManifestInvalid = "manifest-invalid";

    /// <summary>manifest 的 entry 指向的文件不存在。</summary>
    public const string EntryMissing = "entry-missing";

    /// <summary>coreApi 主版本与当前核心不匹配。</summary>
    public const string CoreApiMismatch = "core-api-mismatch";

    /// <summary>dependencies 里声明的插件不在扫描结果中。</summary>
    public const string DependencyMissing = "dependency-missing";

    /// <summary>插件依赖成环。</summary>
    public const string DependencyCycle = "dependency-cycle";

    /// <summary>程序集里没有或有多于一个 IPlugin 实现。</summary>
    public const string PluginTypeAmbiguous = "plugin-type-ambiguous";

    /// <summary>插件实例的 Id 与 manifest 的 id 不一致。</summary>
    public const string IdMismatch = "id-mismatch";

    /// <summary>插件在 Initialize / Start 里抛了未捕获异常。</summary>
    public const string PluginThrew = "plugin-threw";

    /// <summary>同一 owner 连续异常次数超阈值，插件被停用。</summary>
    public const string ErrorThreshold = "error-threshold";

    /// <summary>插件整体加载失败（未细分的兜底）。</summary>
    public const string PluginLoadFailed = "plugin-load-failed";

    // ── 服务注册表 ────────────────────────────────────────────
    /// <summary>同一服务类型被注册两次。</summary>
    public const string ServiceConflict = "service-conflict";

    /// <summary>GetRequired 时服务未注册。</summary>
    public const string ServiceMissing = "service-missing";

    /// <summary>Seal 之后仍尝试注册。</summary>
    public const string RegistrySealed = "registry-sealed";

    /// <summary>插件注册了核心保留接口。</summary>
    public const string ReservedService = "reserved-service";

    // ── 配置 ─────────────────────────────────────────────────
    /// <summary>两个插件认领了同一个 settings.json 字段（§5.0.1）。</summary>
    public const string FieldConflict = "field-conflict";

    /// <summary>配置读写失败（磁盘、权限）。</summary>
    public const string IoError = "io-error";

    // ── 壳层 ─────────────────────────────────────────────────
    /// <summary>槽位已被别的插件占用（§1.2.9，一槽一内容）。</summary>
    public const string SlotOccupied = "slot-occupied";

    /// <summary>槽位 ID 不在四个固定常量里。</summary>
    public const string SlotUnknown = "slot-unknown";

    /// <summary>热键组合已被占用。</summary>
    public const string HotKeyTaken = "hotkey-taken";

    /// <summary>热键编号超出上限。</summary>
    public const string HotKeyLimit = "hotkey-limit";

    /// <summary>热键主键不在支持清单里（只有字母/数字/F1–F12/方向键/常用控制键）。</summary>
    public const string HotKeyUnsupported = "hotkey-unsupported";
}
