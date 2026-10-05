using System;
using System.IO;
using System.Linq;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 文件持久化助手：原子写 + 损坏备份清理。
///
/// **原子写**（#6 修复）：先写唯一名 .tmp 再原子替换，避免断电/崩溃留下半截 JSON 被静默加载。
/// File.Replace 在 NTFS 上是原子的；临时名带随机后缀 → UI 线程与 Kestrel 线程并发保存时
/// 各自写入完整 JSON 后替换（最后写者胜），永不产生半截文件。
/// .tmp 残留（极端崩溃）不影响下次启动；现有 .corrupted 备份机制保留作解析层兜底。
///
/// **损坏备份清理**（2026-10-05 代码审查）：原实现在 settings / schedule / automations / listening
/// 四个 Store 里各复制了一份（写法略有差异、逻辑等价），现合并到此处，调用点只传自己的 basePath。
/// </summary>
public static class FileAtomic
{
    public static void WriteAllText(string path, string content)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, destinationBackupFileName: null);   // 原子替换（NTFS）
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    /// <summary>
    /// 清理过期的 .corrupted 备份，只保留最近 <paramref name="maxCount"/> 份。
    /// 备份文件名含 yyyyMMdd_HHmmss 时间戳 → 字典序即时间序，倒序后跳过前 N 份删其余。
    /// 纯尽力而为：任何异常（权限/占用）都静默跳过，绝不影响主流程。
    /// </summary>
    public static void TrimCorruptedBackups(string basePath, int maxCount = 3)
    {
        try
        {
            var dir = Path.GetDirectoryName(basePath);
            if (string.IsNullOrEmpty(dir)) return;
            var files = Directory.GetFiles(dir, Path.GetFileName(basePath) + ".corrupted.*")
                .OrderByDescending(f => f)
                .Skip(maxCount);
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }
}
