using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StudyJourney.Avalonia.Models;

// ── PDF 阅读进度记忆（PLANNING 2.1「记住上次阅读位置（文件+页码，重开续读）」）────
//
// 为什么单独一个文件、不并进 open-state.json：
//   open-state.json 是「自动化打开类动作」的运行期状态（顺序记忆指针 / 连堂幂等 / 教师端指定），
//   属于"机器怎么开文件"；这里是**阅读器自身的阅读进度**，属于"人读到哪了"。
//   语义、生命周期、失效条件都不同（PDF 被删/改名时阅读进度该失效，但自动化指针不该受影响）。

public class PdfReadingStateData
{
    /// <summary>文件路径 → 上次看到第几页（0 基）</summary>
    public Dictionary<string, int> LastPage { get; set; } = new();

    /// <summary>文件路径 → 最后阅读时间（ISO 字符串），用于淘汰最旧记录</summary>
    public Dictionary<string, string> LastReadAt { get; set; } = new();
}

/// <summary>
/// PDF 阅读进度存储（软件目录 pdf-state.json，原子写、随文件夹分发）。
/// 纯本地、可再生，损坏时直接重建为空（不备份、不弹窗）。
/// </summary>
public static class PdfReadingState
{
    /// <summary>最多记住多少个文件的进度（超出后淘汰最久未读的）</summary>
    private const int MaxEntries = 100;

    private static readonly string StorePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pdf-state.json");

    public static string FilePath => StorePath;

    private static readonly object Gate = new();
    private static PdfReadingStateData _data = LoadFromDisk();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static PdfReadingStateData LoadFromDisk()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var d = JsonSerializer.Deserialize(json, AppJsonContext.Default.PdfReadingStateData);
                if (d != null)
                {
                    d.LastPage ??= new Dictionary<string, int>();
                    d.LastReadAt ??= new Dictionary<string, string>();
                    return d;
                }
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn($"pdf-state.json 加载失败，重建: {ex.Message}");
        }
        return new PdfReadingStateData();
    }

    private static void SaveLocked()
    {
        try
        {
            Helpers.FileAtomic.WriteAllText(StorePath, JsonSerializer.Serialize(_data, AppJsonContext.Default.PdfReadingStateData));
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("保存 pdf-state.json 失败", ex);
        }
    }

    private static string Key(string path)
    {
        try { return Path.GetFullPath(path).Trim(); }
        catch { return path.Trim(); }
    }

    /// <summary>取上次阅读页码（0 基）；无记录返回 0</summary>
    public static int GetLastPage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        lock (Gate)
            return _data.LastPage.TryGetValue(Key(path), out var p) ? Math.Max(p, 0) : 0;
    }

    /// <summary>记住阅读页码（0 基）</summary>
    public static void SetLastPage(string path, int pageIndex)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate)
        {
            var k = Key(path);
            _data.LastPage[k] = Math.Max(pageIndex, 0);
            _data.LastReadAt[k] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            TrimLocked();
            SaveLocked();
        }
    }

    /// <summary>淘汰最久未读的记录，避免无限增长</summary>
    private static void TrimLocked()
    {
        if (_data.LastPage.Count <= MaxEntries) return;

        var stale = _data.LastPage.Keys
            .OrderBy(k => _data.LastReadAt.TryGetValue(k, out var t) ? t : "")
            .Take(_data.LastPage.Count - MaxEntries)
            .ToList();

        foreach (var k in stale)
        {
            _data.LastPage.Remove(k);
            _data.LastReadAt.Remove(k);
        }
    }
}
