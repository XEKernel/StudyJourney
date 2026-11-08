using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// 插件存储句柄（§2.9 / D5）：读写边界由核心钉死，插件拿不到整盘。
///
/// 两条生命周期约定：
/// <list type="bullet">
/// <item><c>data/&lt;id&gt;/</c> 在加载时**确保存在**，卸载插件时**保留**（用户业务数据不删）；</item>
/// <item><c>plugins/&lt;id&gt;/</c> 只读：更新会整目录覆盖，写进去的状态会丢。</item>
/// </list>
/// </summary>
public sealed class PluginStorage : IPluginStorage
{
    public PluginStorage(string pluginId, string pluginsRoot, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        PluginDirectory = Path.GetFullPath(Path.Combine(pluginsRoot, pluginId));
        DataDirectory = Path.GetFullPath(Path.Combine(dataRoot, pluginId));
        Directory.CreateDirectory(DataDirectory);
    }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public string PluginDirectory { get; }

    /// <inheritdoc />
    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var full = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
        var relative = Path.GetRelativePath(DataDirectory, full);

        // 越界判定：解析后必须仍在 DataDirectory 之内（拒绝 ../ 逃逸与绝对路径）。
        if (Path.IsPathRooted(relativePath) || relative.StartsWith("..", StringComparison.Ordinal))
            throw new ArgumentException($"路径越界（只允许 data/<id>/ 之内的相对路径）：{relativePath}", nameof(relativePath));

        return full;
    }
}
