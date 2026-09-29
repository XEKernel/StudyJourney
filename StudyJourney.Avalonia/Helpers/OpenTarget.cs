using System;
using System.IO;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 「打开目标」分类（2026-09-29，v2.25.0）：把老师填的路径分成「软件」与「文件」两类，
/// 因为两者的"是不是已经打开了"判据完全不同：
///   · 软件（exe/lnk/bat/cmd/com/msi/scr/ps1）→ 看**进程是否已在运行**
///   · 文件（文档/媒体）            → 看**窗口标题是否含文件名**（文件没有稳定的进程标识）
///
/// ⚠ 扩展名集合与 `AutomationService.IsSequenceCoursewareRule` 的"软件类"完全一致 ——
///   改一处必须改另一处，否则会出现"课件序列认为它是软件、去重认为它是文件"的割裂。
///
/// 空扩展名一律算**文件**：老师没填全时无从判断，保守走原来的标题匹配，
/// 不会因为"猜它是软件"而误判成"已经在运行"导致不打开。
/// </summary>
public static class OpenTarget
{
    /// <summary>软件类扩展名（与课件序列判定的排除列表保持一致）</summary>
    private static readonly string[] SoftwareExts =
        { ".exe", ".lnk", ".bat", ".cmd", ".com", ".msi", ".scr", ".ps1" };

    /// <summary>是不是"打开一个软件"，而不是"打开一份文件"</summary>
    public static bool IsSoftware(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext;
        try { ext = Path.GetExtension(path.Trim()); }
        catch { return false; }
        if (ext.Length == 0) return false;
        foreach (var e in SoftwareExts)
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 软件路径 → 进程名（**不含 .exe**）。
    /// 只有 `.exe` 能静态推断出来；`.lnk` / `.bat` 等是 shell 目标，真实进程名只能靠
    /// 「我们启动它之后把 proc.ProcessName 记下来」（见 OpenStateStore.RememberAppProcess）。
    /// 推不出来时返回空串，调用方继续走兜底判据。
    /// </summary>
    public static string ProcessNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim();
        if (!p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "";
        try { return Path.GetFileNameWithoutExtension(p) ?? ""; }
        catch { return ""; }
    }

    /// <summary>给老师看的软件名（.lnk 用快捷方式名、.exe 用文件名）—— 也用作窗口标题兜底匹配的关键词</summary>
    public static string DisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFileNameWithoutExtension(path.Trim()) ?? ""; }
        catch { return ""; }
    }
}
