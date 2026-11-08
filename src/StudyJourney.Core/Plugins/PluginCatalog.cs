using System.Text.Json;
using StudyJourney.Core.Config;
using StudyJourney.Core.Services;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// 加载过程中一个插件的**全部可变状态**（内部用；对外一律转成不可变的 <see cref="PluginLoadResult"/>）。
/// </summary>
internal sealed class DiscoveredPlugin(string directory)
{
    /// <summary>目录名（manifest 读不出来时它也是报告里的 id）。</summary>
    public string Id { get; } = Path.GetFileName(directory);

    public string Directory { get; } = directory;

    public PluginManifest? Manifest { get; set; }

    public PluginState State { get; set; } = PluginState.Discovered;

    public string? Code { get; private set; }

    public string? Message { get; private set; }

    public PluginLoadContext? Alc { get; set; }

    public IPlugin? Plugin { get; set; }

    public PluginContext? Context { get; set; }

    /// <summary>运行期身份：注册表 owner / 日志 / 回滚都按它走。</summary>
    public string RuntimeId => Manifest?.Id ?? Id;

    public void Fail(PluginState state, string? code, string? message)
    {
        State = state;
        Code = code;
        Message = message;
    }

    public PluginLoadResult ToResult() => new(RuntimeId, State, Code, Message);
}

/// <summary>
/// manifest 扫描与**加载前**的全部校验（CORE_ARCHITECTURE §3.2 E1–E9）：
/// 扫目录 → 解析 manifest → id 与目录名一致 → 全局 configFields 冲突 → coreApi 主版本 → 依赖拓扑排序。
///
/// 为什么这些校验要在建 ALC **之前**完成：此时拒载的成本是零，而加载后才发现问题的成本
/// 是"已经跑过用户代码"，回滚面完全不同（§2.11）。
/// </summary>
internal static class PluginCatalog
{
    /// <summary>扫描并校验；返回按拓扑序排好、状态已定的清单（失败的条目也在里面，排在后面）。</summary>
    public static List<DiscoveredPlugin> Discover(string pluginsDirectory, IPluginLogger logger)
    {
        var slots = Scan(pluginsDirectory, logger);
        RejectDuplicateIds(slots, logger);
        DetectFieldConflicts(slots, logger);
        CheckApiCompatibility(slots, logger);
        SortByDependencies(slots, logger);
        return slots;
    }

    private static List<DiscoveredPlugin> Scan(string pluginsDirectory, IPluginLogger logger)
    {
        var slots = new List<DiscoveredPlugin>();

        foreach (var directory in Directory.GetDirectories(pluginsDirectory).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;   // E1：没有清单的目录直接跳过（可能是用户手工放的空目录）

            var slot = new DiscoveredPlugin(directory);
            slots.Add(slot);

            PluginManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                slot.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid, $"plugin.json 读取/解析失败：{ex.Message}");
                logger.Error($"插件目录 {slot.Id} 的 plugin.json 损坏", ex);
                continue;
            }

            var invalid = Validate(slot.Id, manifest);
            if (invalid is not null)
            {
                slot.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid, invalid);
                logger.Error($"插件 {slot.Id} 清单非法：{invalid}");
                continue;
            }

            slot.Manifest = manifest;
            slot.State = PluginState.Discovered;

            // 已声明但 v1 不实现的钩子字段：只记 Warn（§2.11）。
            if (manifest!.ConfigSection is not null)
                logger.Warn($"插件 {slot.Id} 声明了 configSection（嵌套节尚未实现），已按扁平字段处理");
            if (manifest.ConfigFieldsShared is not null)
                logger.Warn($"插件 {slot.Id} 声明了 configFieldsShared（只读共享字段 v1 未实现），已忽略");
            if (!string.IsNullOrWhiteSpace(manifest.Version) && !Version.TryParse(manifest.Version, out _))
                logger.Warn($"插件 {slot.Id} 的 version「{manifest.Version}」不是 x.y.z 形式（只告警，不拒载）");
        }

        return slots;
    }

    private static string? Validate(string directoryName, PluginManifest? manifest)
    {
        if (manifest is null)
            return "plugin.json 为空或不是 JSON 对象";

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "缺少必需字段 id";
        if (string.IsNullOrWhiteSpace(manifest.Entry))
            return "缺少必需字段 entry";
        if (string.IsNullOrWhiteSpace(manifest.CoreApi))
            return "缺少必需字段 coreApi";

        // E4：id 必须等于目录名（忽略大小写），否则「卸载时删错目录」。
        if (!string.Equals(manifest.Id, directoryName, StringComparison.OrdinalIgnoreCase))
            return $"id「{manifest.Id}」与目录名「{directoryName}」不一致";

        // "core" 是核心的 owner 名：插件占用它会让按 owner 清理时误伤核心登记。
        if (string.Equals(manifest.Id, ServiceRegistry.CoreOwner, StringComparison.OrdinalIgnoreCase))
            return "id 不能是保留名 core";

        return null;
    }

    /// <summary>E5：同 id 重复。E4 已保证"id = 目录名"，因此正常扫描下走不到这里（防御性保留）。</summary>
    private static void RejectDuplicateIds(List<DiscoveredPlugin> slots, IPluginLogger logger)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in slots.Where(s => s.Manifest is not null))
        {
            if (seen.Add(slot.Manifest!.Id))
                continue;

            slot.Fail(PluginState.ManifestInvalid, ErrorCodes.ManifestInvalid,
                $"id「{slot.Manifest.Id}」重复（先扫描到的同 id 插件保留）");
            logger.Error($"插件目录 {slot.Id} 的 id 与另一个插件重复，已拒载");
        }
    }

    /// <summary>§5.0.1：读完所有 manifest 后做**全局**冲突检测，字典序保留第一个。</summary>
    private static void DetectFieldConflicts(List<DiscoveredPlugin> slots, IPluginLogger logger)
    {
        var manifests = slots.Where(s => s.Manifest is not null).Select(s => s.Manifest!).ToList();
        if (manifests.Count == 0)
            return;

        var report = ConfigFieldClaimReport.Resolve(manifests);

        foreach (var slot in slots.Where(s => s.Manifest is not null))
        {
            if (!report.RejectedPluginIds.Contains(slot.Manifest!.Id))
                continue;

            var conflict = report.Conflicts.FirstOrDefault(c => c.SecondPluginId == slot.Manifest.Id);
            slot.Fail(PluginState.LoadFailed, ErrorCodes.FieldConflict,
                conflict?.Message ?? $"配置字段与其它插件冲突：{slot.Manifest.Id}");
            logger.Error(conflict?.Message ?? $"插件 {slot.Manifest.Id} 配置字段冲突，已拒载");
        }
    }

    /// <summary>E7 / D2：只比 coreApi 的主版本。</summary>
    private static void CheckApiCompatibility(List<DiscoveredPlugin> slots, IPluginLogger logger)
    {
        foreach (var slot in slots.Where(s => s.State == PluginState.Discovered))
        {
            if (CoreInfo.IsApiCompatible(slot.Manifest!.CoreApi))
                continue;

            slot.Fail(PluginState.LoadFailed, ErrorCodes.CoreApiMismatch,
                $"插件声明 coreApi「{slot.Manifest.CoreApi}」，当前核心为 {CoreInfo.ApiVersion}（主版本必须一致）");
            logger.Error($"插件 {slot.Manifest.Id} 的 coreApi 与核心不兼容，已拒载");
        }
    }

    /// <summary>E8 / E9：L1 只查插件 id；服务存在性只能在插件 Initialize 里用 GetRequired 断言（§4.3）。</summary>
    private static void SortByDependencies(List<DiscoveredPlugin> slots, IPluginLogger logger)
    {
        var candidates = slots.Where(s => s.State == PluginState.Discovered && s.Manifest is not null).ToList();
        var byId = candidates.ToDictionary(s => s.Manifest!.Id, s => s, StringComparer.Ordinal);

        var alive = new List<DiscoveredPlugin>();
        foreach (var slot in candidates)
        {
            var missing = slot.Manifest!.Dependencies.Where(dep => !byId.ContainsKey(dep)).ToList();
            if (missing.Count == 0)
            {
                alive.Add(slot);
                continue;
            }

            slot.Fail(PluginState.LoadFailed, ErrorCodes.DependencyMissing, $"缺少依赖插件：{string.Join(", ", missing)}");
            logger.Error($"插件 {slot.Manifest.Id} 依赖缺失：{string.Join(", ", missing)}");
        }

        // Kahn：出不了队的是环上节点（或依赖环的节点）→ dependency-cycle。
        var indegree = alive.ToDictionary(s => s.Manifest!.Id, s => s.Manifest!.Dependencies.Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var queue = new Queue<DiscoveredPlugin>(alive.Where(s => indegree[s.Manifest!.Id] == 0));
        var ordered = new List<DiscoveredPlugin>(alive.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            current.State = PluginState.Sorted;
            ordered.Add(current);

            foreach (var dependent in alive.Where(s => s.State != PluginState.Sorted
                                                       && s.Manifest!.Dependencies.Contains(current.Manifest!.Id, StringComparer.Ordinal)))
            {
                if (--indegree[dependent.Manifest!.Id] == 0)
                    queue.Enqueue(dependent);
            }
        }

        foreach (var slot in alive.Where(s => s.State != PluginState.Sorted))
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.DependencyCycle,
                $"依赖成环：{slot.Manifest!.Id} → {string.Join(" → ", slot.Manifest.Dependencies)}");
            logger.Error($"插件 {slot.Manifest!.Id} 依赖成环，已拒载");
        }

        // 成功的按拓扑序排前面，失败的保持原顺序排后面（报告按这个顺序输出）。
        var failed = slots.Where(s => s.State != PluginState.Sorted).ToList();
        slots.Clear();
        slots.AddRange(ordered);
        slots.AddRange(failed);
    }
}
