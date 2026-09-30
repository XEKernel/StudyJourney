using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StudyJourney.Avalonia.Models;

// ── 听力播放器（v2.27.0，规划 2.9）──────────────────────────────
//
// 为什么独立 listening.json 而不放 settings.json：
//   「恢复默认设置」会把 settings.json 整份重置 —— 那会把老师攒的**听力来源与进度指针**一起清掉。
//   与 automations.json / open-state.json 同样的理由：运行期进度与"可被重置的偏好"分开存。
//
// 核心概念：
//   · 来源（Source）= 一套录音 = 一个目录 = 一条序列（资料A / 周考卷 / 桌面临时）
//   · 活跃来源（ActiveSourceId）= 最近播过的那一套；自动触发时继续播它
//   · 指针（LastFile + Finished）——**只有整份播完才推进**：
//       听了一半被关掉 → Finished=false → 下次**续听同一份**（老师不会漏听）
//       整份播完       → Finished=true  → 下次播**序列里的下一份**

/// <summary>一套听力录音（一个目录 = 一条按文件名序号排的序列）</summary>
public class ListeningSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名（资料A / 周考卷 / 桌面临时）</summary>
    public string Name { get; set; } = "";

    /// <summary>录音所在目录</summary>
    public string Directory { get; set; } = "";

    /// <summary>进度指针：上次播到哪一份（空 = 还没播过，从第一份开始）</summary>
    public string LastFile { get; set; } = "";

    /// <summary>
    /// 上次那一份是否**整份播完**。
    /// ⚠ 这是"听一半关掉下次续听"的关键：false 时下次仍播 LastFile 本身，而不是下一份。
    /// </summary>
    public bool Finished { get; set; }

    public string LastAt { get; set; } = "";

    /// <summary>已播完的份数（只用于展示"这套听过 N 份"）</summary>
    public int PlayedCount { get; set; }

    /// <summary>
    /// 一次性来源：这一套播到末尾后自动回退到上一个来源（老师临时扔进来的文件夹 / 桌面文件属于这类）。
    /// </summary>
    public bool Once { get; set; }
}

public class ListeningData
{
    public List<ListeningSource> Sources { get; set; } = new();

    /// <summary>活跃来源 Id（最近播过的那一套；自动触发时继续播它）</summary>
    public string ActiveSourceId { get; set; } = "";

    /// <summary>
    /// 例外日（yyyy-MM-dd）：这些日期**不自动播放、也不推进进度**。
    /// 主要用于英语周考 —— 那天听力是全校广播放的，本软件再播就是重复（用户 2026-09-29 澄清）。
    /// </summary>
    public List<string> SkipDates { get; set; } = new();

    /// <summary>当天课表里有英语考试时自动跳过（默认**关**：需要老师在设置里显式打开）</summary>
    public bool SkipIfExam { get; set; }

    /// <summary>播完后延时多少秒自动关闭播放器窗口（默认 3；0 = 立刻关；-1 = 不自动关）</summary>
    public int AutoCloseSeconds { get; set; } = 3;

    /// <summary>音量 0~100</summary>
    public int Volume { get; set; } = 80;
}

/// <summary>
/// 听力数据存储（软件目录 listening.json，原子写、随文件夹分发）。
/// 与 AutomationStore 同构：损坏时备份后重建（保留最近 3 份）。
/// </summary>
public static class ListeningStore
{
    private static readonly string StorePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "listening.json");

    public static string FilePath => StorePath;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ListeningData Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var d = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListeningData);
                if (d != null)
                {
                    d.Sources ??= new List<ListeningSource>();
                    d.SkipDates ??= new List<string>();
                    return d;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var bak = StorePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(StorePath, bak, overwrite: true);
                TrimCorruptedBackups();
            }
            catch { }
            Helpers.AppLogger.Warn($"listening.json 加载失败，使用默认: {ex.Message}");
        }
        return new ListeningData();
    }

    public static void Save(ListeningData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, AppJsonContext.Default.ListeningData);
            Helpers.FileAtomic.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("保存 listening.json 失败", ex);
        }
    }

    private static void TrimCorruptedBackups(int maxCount = 3)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.GetFiles(dir, Path.GetFileName(StorePath) + ".corrupted.*")
                .OrderByDescending(f => f)
                .Skip(maxCount)
                .ToList()
                .ForEach(f => { try { File.Delete(f); } catch { } });
        }
        catch { }
    }
}
