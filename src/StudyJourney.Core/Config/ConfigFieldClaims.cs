using StudyJourney.Sdk;

namespace StudyJourney.Core.Config;

/// <summary>一次字段认领冲突（CORE_ARCHITECTURE §5.0.1）。提示文案按文档逐字给出。</summary>
/// <param name="FieldName">被两个插件同时认领的 <c>settings.json</c> 顶层字段名。</param>
/// <param name="FirstPluginId">字典序在前、保留不动的插件 id。</param>
/// <param name="SecondPluginId">字典序在后、被拒载的插件 id。</param>
public sealed record FieldConflict(string FieldName, string FirstPluginId, string SecondPluginId)
{
    /// <summary>用户可见提示（§5.0.1 的文案，点名双方插件）。</summary>
    public string Message =>
        $"插件 {SecondPluginId} 的配置字段 {FieldName} 已被插件 {FirstPluginId} 占用，拒绝加载。" +
        $"请重命名 {SecondPluginId} 的字段。";
}

/// <summary>
/// <c>configFields</c> 的**全局**认领结果（CORE_ARCHITECTURE §5.0.1）。
///
/// 三条规则（都在 <see cref="Resolve"/> 里实现，没有第二条路径）：
/// <list type="number">
/// <item>读完**全部** manifest 后才做检测，不边读边拒——否则结果取决于目录枚举顺序；</item>
/// <item>按 <see cref="PluginManifest.Id"/> 的**字典序**保留第一个认领者，**与加载顺序无关**；</item>
/// <item>后者整份拒载（<c>field-conflict</c>）——它认领的**其它**字段也一并作废，不许半认领。</item>
/// </list>
///
/// 为什么拒载而不是"警告后允许"：<c>settings.json</c> 是扁平的，一个字段名只有一个物理位置，
/// 允许共存等于"两个插件轮流覆盖同一个值"，症状是"我的设置自己变了"，且无法归因。
/// </summary>
public sealed class ConfigFieldClaimReport
{
    private ConfigFieldClaimReport(
        IReadOnlyDictionary<string, string> fieldOwners,
        IReadOnlyList<FieldConflict> conflicts,
        IReadOnlySet<string> rejectedPluginIds)
    {
        FieldOwners = fieldOwners;
        Conflicts = conflicts;
        RejectedPluginIds = rejectedPluginIds;
    }

    /// <summary>字段名 → 认领插件 id（只有被接受的插件在里面）。</summary>
    public IReadOnlyDictionary<string, string> FieldOwners { get; }

    /// <summary>全部冲突（按被发现顺序）。</summary>
    public IReadOnlyList<FieldConflict> Conflicts { get; }

    /// <summary>因 <c>field-conflict</c> 被拒载的插件 id。</summary>
    public IReadOnlySet<string> RejectedPluginIds { get; }

    /// <summary>
    /// 做全局冲突检测。
    /// </summary>
    /// <param name="manifests">本次扫描到的全部 manifest（顺序任意；本方法内部按 id 字典序排序）。</param>
    public static ConfigFieldClaimReport Resolve(IEnumerable<PluginManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var ordered = manifests
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new List<FieldConflict>();

        foreach (var manifest in ordered)
        {
            // 同一个插件自己的 configFields 写重复了不算冲突，只认领一次。
            var claimed = manifest.ConfigFields
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var clash = claimed.FirstOrDefault(owners.ContainsKey);
            if (clash is not null)
            {
                rejected.Add(manifest.Id);
                foreach (var field in claimed.Where(owners.ContainsKey))
                    conflicts.Add(new FieldConflict(field, owners[field], manifest.Id));
                continue;   // 被拒载的插件**一个字段都不认领**（否则"半认领"会让孤儿清单失真）
            }

            foreach (var field in claimed)
                owners[field] = manifest.Id;
        }

        return new ConfigFieldClaimReport(owners, conflicts, rejected);
    }
}
