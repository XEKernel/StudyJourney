using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 听力播放的判定规则（v2.27.0，规划 2.9）——**全是纯函数**，便于自检钉住。
///
/// 最关键的一条语义（用户 2026-09-29 确认）：
///   **只有"整份播完"才推进指针**。听了一半手动关掉 → 指针停住、下次续听同一份。
///   所以来源上存的是「LastFile + Finished」两个字段，而不是单一的"上次播过的文件"：
///     · Finished == true  → 下次播序列里的**下一份**
///     · Finished == false → 下次仍播 LastFile（续听）
///     · LastFile 为空/文件已不在 → 从序列第一份开始
///
/// ⚠ **不能直接用 `FileSequence.Next`**：那个实现是**循环**的（到末尾绕回第一份，
///   注释写明"避免听力播完卡死"），所以它永远不返回 null、也就无法表达"这套听完了"。
///   听力需要严格语义（到末尾就停、由老师补新录音），故这里自己实现 `NextStrict`。
/// </summary>
public static class ListeningRules
{
    /// <summary>课节算不算"听力"节次：科目名里含「听力」（兼容"听力课""英语听力"等叫法）</summary>
    public const string ListeningKeyword = "听力";

    /// <summary>该课节是不是听力节次</summary>
    public static bool IsListeningPeriod(Models.ScheduleEntry? entry)
    {
        var sub = (entry?.Subject ?? "").Trim();
        return sub.Length > 0 && sub.Contains(ListeningKeyword, StringComparison.Ordinal);
    }

    /// <summary>
    /// 今天该不该**跳过自动播放**（不播，也**不推进进度**）。
    /// 场景（用户 2026-09-29 澄清）：英语周考那天听力由全校广播播放，本软件放了就是重复。
    /// </summary>
    /// <param name="data">听力数据</param>
    /// <param name="date">要判断的日期</param>
    /// <param name="hasEnglishExam">当天课表里是否有英语考试（由调用方查课表）</param>
    public static bool ShouldSkipToday(Models.ListeningData? data, DateTime date, bool hasEnglishExam)
    {
        if (data == null) return false;
        var key = date.ToString("yyyy-MM-dd");
        // ⚠ 按 yyyy-MM-dd 精确比较；空串/格式不对的脏值不会误命中
        if (data.SkipDates != null &&
            data.SkipDates.Any(x => string.Equals((x ?? "").Trim(), key, StringComparison.Ordinal)))
            return true;
        return data.SkipIfExam && hasEnglishExam;
    }

    /// <summary>
    /// 序列中 current 的**严格**下一份：到末尾返回 null（**不循环** —— 用于判定"这套听完了"）。
    /// current 不在列表里（文件被删/换了一整套）→ 从第一份开始。
    /// </summary>
    public static string? NextStrict(string? current, IReadOnlyList<string> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;
        var list = FileSequence.Sort(candidates);
        if (list.Count == 0) return null;
        var cur = (current ?? "").Trim();
        if (cur.Length == 0) return list[0];

        for (int k = 0; k < list.Count; k++)
        {
            if (!string.Equals(list[k], cur, StringComparison.OrdinalIgnoreCase)) continue;
            return k + 1 < list.Count ? list[k + 1] : null;   // 最后一份 → null
        }
        return list[0];   // 找不到 current（被删/换套）→ 从第一份开始
    }

    /// <summary>
    /// 自动触发时**这次该播哪一份**：
    /// 播完了 → 下一份（到末尾返回 null = 这套听完了）；没播完 → 续听那份；没记录 → 第一份。
    /// </summary>
    public static string? CurrentToPlay(Models.ListeningSource? source, IReadOnlyList<string> candidates)
    {
        if (source == null || candidates == null || candidates.Count == 0) return null;

        var last = (source.LastFile ?? "").Trim();
        if (last.Length > 0)
        {
            if (!source.Finished)
            {
                // 上次听了一半 → 续听同一份（文件还在才算；被删了就往下走）
                if (candidates.Any(c => string.Equals(c, last, StringComparison.OrdinalIgnoreCase)))
                    return last;
            }
            else
            {
                return NextStrict(last, candidates);   // 到末尾 → null（这套听完了）
            }
        }
        return FileSequence.First(FileSequence.Sort(candidates));
    }

    /// <summary>这一套是不是已经播到末尾了（设置页显示"已听完"，自动触发则静默跳过）</summary>
    public static bool IsAtEnd(Models.ListeningSource? source, IReadOnlyList<string> candidates)
    {
        if (source == null) return false;
        var last = (source.LastFile ?? "").Trim();
        if (last.Length == 0 || !source.Finished) return false;
        return NextStrict(last, candidates) == null;
    }

    /// <summary>这份之后还有没有下一份（播放器"下一首"按钮用）</summary>
    public static bool HasNext(string? current, IReadOnlyList<string> candidates)
        => NextStrict(current, candidates) != null;

    /// <summary>这份之前有没有上一份（自然序号无法表达"上一份"，故用**列表位置**倒推）</summary>
    public static string? PreviousStrict(string? current, IReadOnlyList<string> candidates)
    {
        if (current == null || candidates == null || candidates.Count == 0) return null;
        var list = FileSequence.Sort(candidates);
        for (int k = 0; k < list.Count; k++)
            if (string.Equals(list[k], current, StringComparison.OrdinalIgnoreCase))
                return k > 0 ? list[k - 1] : null;
        return null;
    }

    /// <summary>音频扩展名（判断老师扔进来的文件是不是录音；也用于过滤目录里混着的封面图/说明文档）</summary>
    private static readonly string[] AudioExts =
        { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".aiff", ".aif", ".ogg", ".mp4" };

    public static bool IsAudioFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext;
        try { ext = Path.GetExtension(path.Trim()); } catch { return false; }
        if (ext.Length == 0) return false;
        foreach (var e in AudioExts)
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>只保留音频文件（听力目录里可能混着封面图、说明文档）</summary>
    public static List<string> OnlyAudio(IEnumerable<string>? files)
        => (files ?? Enumerable.Empty<string>()).Where(IsAudioFile).ToList();

    /// <summary>一套来源此刻可播的候选（目录不存在 → 空列表，不抛）——**已过滤为音频并排好序**</summary>
    public static List<string> CandidatesOf(Models.ListeningSource? source)
        => source == null ? new List<string>() : OnlyAudio(FileSequence.ListCandidates(source.Directory ?? ""));

    /// <summary>显示用：当前是这套里的第几份 / 共几份（1 基；找不到返回 0）</summary>
    public static (int Index, int Total) PositionOf(string? current, IReadOnlyList<string> candidates)
    {
        if (candidates == null || candidates.Count == 0) return (0, 0);
        var list = FileSequence.Sort(candidates);
        for (int k = 0; k < list.Count; k++)
            if (string.Equals(list[k], current, StringComparison.OrdinalIgnoreCase))
                return (k + 1, list.Count);
        return (0, list.Count);
    }

    /// <summary>从路径取"资料名 03/12"这样的进度文本（设置页与播放器标题共用）</summary>
    public static string ProgressText(string? current, IReadOnlyList<string> candidates)
    {
        var (idx, total) = PositionOf(current, candidates);
        if (total == 0) return "（目录里没有音频）";
        return idx > 0 ? $"第 {idx}/{total} 份" : $"共 {total} 份";
    }

    /// <summary>
    /// 主窗口胶囊栏的文案（**纯函数**，v2.28.0）——把"这次该播哪一份 + 今天是否例外日"变成一行话。
    /// 没有任何可播内容（无来源 / 目录里没音频）→ 返回 null，胶囊整个不显示。
    /// </summary>
    /// <returns>(来源名, 完整一行, 紧凑视图用的短串, 今天是否例外日跳过)</returns>
    public static (string Source, string Line, string Short, bool SkipToday)? DescribeCapsule(
        Models.ListeningSource? source, IReadOnlyList<string> candidates, bool skipToday)
    {
        if (source == null || candidates == null || candidates.Count == 0) return null;

        // 例外日（英语周考全校广播那天）要**明说"跳过"**，否则老师会以为软件坏了
        if (skipToday)
            return (source.Name, "今天例外（广播放），跳过", "跳过", true);

        var toPlay = CurrentToPlay(source, candidates);
        if (toPlay == null)                     // 这套已播到最后一份
            return (source.Name, $"已听完（共 {candidates.Count} 份）", "听完", false);

        var (idx, total) = PositionOf(toPlay, candidates);
        // 三种状态必须分开：从没播过 / 上次听了一半（续听同一份）/ 上次播完（下一份）
        string verb = string.IsNullOrWhiteSpace(source.LastFile) ? "待播"
                    : source.Finished ? "下次" : "续听";
        return (source.Name, $"{verb} 第 {idx}/{total} 份", $"{idx}/{total}", false);
    }
}
