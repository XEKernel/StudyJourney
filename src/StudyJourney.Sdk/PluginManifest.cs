using System.Text.Json.Serialization;

namespace StudyJourney.Sdk;

/// <summary>
/// <c>plugins/&lt;id&gt;/plugin.json</c> 的强类型投影（CORE_ARCHITECTURE §2.11）。
///
/// **加载器在任何 ALC 创建之前**解析它，字段校验失败直接拒载——
/// 能在"读一个 JSON"时发现的问题，绝不留到"加载程序集"之后（§2.11）。
///
/// 命名约定见 §1.4.1：<c>id</c> 的 PascalCase 即源码目录名。
/// </summary>
public sealed record PluginManifest
{
    /// <summary>插件唯一标识，小写字母/数字/连字符。<b>必须等于目录名（忽略大小写）。</b></summary>
    /// <remarks>消费者：PluginLoader（id-match 校验）、注册表 owner、data/&lt;id&gt;/ 目录、日志前缀</remarks>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>显示名（"课表"/"远程控制"），用于设置页插件列表。</summary>
    /// <remarks>消费者：设置页"插件"列表</remarks>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>语义化版本 <c>x.y.z</c>。格式非法只告警不拒载。</summary>
    /// <remarks>消费者：设置页插件列表、更新器（C4-L2 插件独立更新）</remarks>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>入口程序集文件名（相对清单所在目录），例如 <c>StudyJourney.Plugin.Test.dll</c>。</summary>
    /// <remarks>消费者：PluginLoader（LoadFromAssemblyPath；文件不存在 → entry-missing）</remarks>
    [JsonPropertyName("entry")]
    public string Entry { get; init; } = string.Empty;

    /// <summary>声明本插件面向的契约版本，例如 <c>"1.x"</c>。**只比对主版本**（D2）。</summary>
    /// <remarks>消费者：PluginLoader（CoreInfo.IsApiCompatible；不匹配 → core-api-mismatch）</remarks>
    [JsonPropertyName("coreApi")]
    public string CoreApi { get; init; } = string.Empty;

    /// <summary>依赖的插件 id 列表（拓扑排序输入）。缺省为空数组。</summary>
    /// <remarks>消费者：PluginLoader（拓扑排序；缺失 → dependency-missing；成环 → dependency-cycle）</remarks>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// **本插件认领的 <c>settings.json</c> 顶层字段名清单**（D-3 已拍板：扁平结构）。
    /// 空数组合法（纯后台无配置插件）。
    /// </summary>
    /// <remarks>
    /// 消费者：ConfigStore（按它把扁平键摘出来给插件 / 原位写回）、
    /// PluginLoader（全局字段冲突检测 → field-conflict）、
    /// --selftest configfields（孤儿字段检查）
    /// </remarks>
    [JsonPropertyName("configFields")]
    public IReadOnlyList<string> ConfigFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 预留的**嵌套节**能力（D-2）。v1 核心**不读它**；填了非 null 只记 Warn
    /// （"本版本不支持嵌套节，已按扁平处理"）。**留钩子不留实现。**
    /// </summary>
    /// <remarks>消费者：PluginLoader（仅用于记录一条 Warn；不参与任何分发逻辑）</remarks>
    [JsonPropertyName("configSection")]
    public string? ConfigSection { get; init; }

    /// <summary>
    /// 预留的**只读共享字段**清单。v1 **不实现**：不参与冲突检测，只记录一条 Warn。
    /// 将来真要"两个插件共读一个字段"时才启用（届时 manifest 从 12 个字段变 13 个）。
    /// </summary>
    /// <remarks>消费者：PluginLoader（仅记录 Warn；当前无实际行为）</remarks>
    [JsonPropertyName("configFieldsShared")]
    public IReadOnlyList<string>? ConfigFieldsShared { get; init; }

    /// <summary>一句话说明（"课表编辑与调休"）。</summary>
    /// <remarks>消费者：设置页"插件"列表</remarks>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>作者展示名。</summary>
    /// <remarks>消费者：设置页"插件"列表</remarks>
    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    /// <summary>需要的最低宿主版本（比 coreApi 更粗的兜底）；<c>null</c> = 不检查。</summary>
    /// <remarks>消费者：PluginLoader（宿主版本过低时的友好提示）</remarks>
    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; init; }

    /// <summary>是否提供设置页。纯后台插件（诊断）设 false，侧栏不出现空页。</summary>
    /// <remarks>消费者：设置窗（决定是否向侧栏请求该插件的页面）</remarks>
    [JsonPropertyName("hasSettingsPage")]
    public bool HasSettingsPage { get; init; } = true;
}
