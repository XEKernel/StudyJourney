using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 课件/音频「自然序号」工具（2.5.8 顺序记忆的地基）。
///
/// 老师命名习惯多样，需要能识别其中携带的序号：
///   01.pptx / unit1.mp3 / Unit 03.mp3 / 第1讲.pdf / Lesson2.docx / 1-2 复习.pptx / L05.wav
/// 解析规则：取文件名里**第一段连续数字**为该文件的序号；
///   - 无数字 → 视为无序号（排在有序号之后，按名称排）
///   - 数字过大（>999，如年份 2024）→ 视为无序号，避免"高三复习2024"排在最后
///   - 同名序号（如 "1-1" 与 "1-2" 都解析为 1）→ 用完整文件名做次级排序，保证稳定
/// </summary>
public static class FileSequence
{
    /// <summary>序号上限：超过视为"年份/页码"而非课次编号，按无序号处理</summary>
    private const int MaxSequenceNumber = 999;

    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>提取文件名中的序号；无有效序号返回 -1</summary>
    public static int ExtractNumber(string pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName)) return -1;
        var name = Path.GetFileNameWithoutExtension(pathOrName);
        if (string.IsNullOrEmpty(name)) return -1;

        var m = NumberRegex.Match(name);
        if (!m.Success) return -1;
        if (!int.TryParse(m.Value, out int n)) return -1;
        return n is >= 0 and <= MaxSequenceNumber ? n : -1;
    }

    /// <summary>自然序号排序（有序号在前、按序号；同号或无号按文件名不区分大小写）</summary>
    public static List<string> Sort(IEnumerable<string> paths)
    {
        return paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OrderBy(p => ExtractNumber(p) is var n && n >= 0 ? 0 : 1)
            .ThenBy(p => { var n = ExtractNumber(p); return n >= 0 ? n : int.MaxValue; })
            .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>序列第一份（编号最小的；全无编号则取名称最小的一份）</summary>
    public static string? First(IEnumerable<string> paths)
        => Sort(paths).FirstOrDefault();

    /// <summary>
    /// 序列中 current 的下一份；current 不在序列里/已是最后一份 → 退回"名称最小的一份"（形成循环，避免听力播完卡死）。
    /// 单份文件时返回它自己。
    /// </summary>
    public static string? Next(string? current, IEnumerable<string> paths)
    {
        var list = Sort(paths);
        if (list.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(current)) return list[0];

        int idx = list.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return list[0];
        return list[(idx + 1) % list.Count];
    }

    /// <summary>列出目录下的候选文件（排除占位文件与隐藏文件），按自然序号排序</summary>
    public static List<string> ListCandidates(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();
            return Sort(Directory.GetFiles(directory)
                .Where(f => !f.EndsWith(".placeholder", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).StartsWith('~'))     // Office 临时文件 ~$xxx
                .Where(f => (File.GetAttributes(f) & FileAttributes.Hidden) == 0));
        }
        catch
        {
            return new List<string>();
        }
    }
}
