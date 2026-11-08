using System.Globalization;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Config;

/// <summary>
/// §5.7 分支一的落地：<c>settings.json</c> **内容真不是 JSON** 时，
/// 备份为 <c>settings.json.corrupted.&lt;yyyyMMdd_HHmmss&gt;</c> → 删原文件 → 用默认值启动 → 只保留最近 3 份。
///
/// ⚠ 只有 <see cref="System.Text.Json.JsonException"/>（内容损坏）能进这里。
/// IO / 权限 / 杀软占用**绝不能**当损坏处理 —— v3.3.0 就是"任何异常都备份+删除"，
/// 结果一次文件占用就清空了老师全部设置（§5.7 的血泪来源）。
/// </summary>
internal static class ConfigFileRecovery
{
    private const int MaxBackups = 3;

    public static void BackupCorrupted(string path, IPluginLogger? logger)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var backup = path + ".corrupted." + stamp;
            File.Copy(path, backup, overwrite: true);
            File.Delete(path);
            logger?.Error($"已把损坏的 settings.json 备份为 {Path.GetFileName(backup)}，并用默认值启动");

            PruneBackups(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Error("备份损坏的配置文件失败（原文件保留，本次用默认值启动）", ex);
        }
    }

    /// <summary>只保留最近 3 份备份（按文件名里的时间戳倒序）。</summary>
    private static void PruneBackups(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            return;

        var prefix = Path.GetFileName(path) + ".corrupted.";
        var stale = Directory.GetFiles(directory, prefix + "*")
            .OrderByDescending(file => file, StringComparer.Ordinal)
            .Skip(MaxBackups);

        foreach (var file in stale)
        {
            try { File.Delete(file); }
            catch { /* 备份清理失败不影响启动 */ }
        }
    }
}
