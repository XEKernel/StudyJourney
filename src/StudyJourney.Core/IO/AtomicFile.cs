using System.Text;

namespace StudyJourney.Core.IO;

/// <summary>
/// 原子写（CORE_ARCHITECTURE §5.3 第 4 条 / I-4.5）。
///
/// **为什么必须有它**：<c>File.WriteAllText</c> 在"写了一半"时断电 / 被杀软拦住 / 进程被结束，
/// 磁盘上就会留下半截 JSON —— 下次启动读不出来，走"损坏重建"分支，用户的全部设置丢失。
/// v3.3.0 就吃过这个亏。原子写的做法是"写临时文件 → 同卷 rename 覆盖"，
/// rename 在 NTFS 上是替换语义，**读到的永远是某个完整版本**。
/// </summary>
internal static class AtomicFile
{
    /// <summary>以 UTF-8 写入（可选 BOM），先写同目录临时文件再 rename 覆盖目标。</summary>
    public static void WriteAllText(string path, string text, bool withBom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 临时文件必须与目标同目录（同卷）——跨卷时 rename 会退化成"复制+删除"，失去原子性。
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch { /* 删除失败只影响临时文件，交给系统清理 */ }
            }
        }
    }
}
