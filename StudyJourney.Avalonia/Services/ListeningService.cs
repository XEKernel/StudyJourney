using System;
using System.Collections.Generic;
using System.Linq;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Services;

/// <summary>
/// 听力播放服务（v2.27.0，规划 2.9）：持有 listening.json、解析"这次该播哪一份"、
/// 维护来源与进度指针。播放本身由 <see cref="Helpers.AudioPlayer"/> + 播放器窗口负责。
///
/// 与自动化引擎的分工（刻意这样切）：
///   · **触发/日程**交给现有自动化规则（「课表有听力节次时 → 播放听力」），不另造一套时间表 ——
///     老师已经在用拼图式规则，多一套只会两处维护、行为还得靠猜。
///   · **例外日**（英语周考全校广播那天不播、也不推进）放在这里判：它属于"听力"这件事，不属于通用触发引擎。
/// </summary>
public sealed class ListeningService
{
    private ListeningData _data;

    /// <summary>来源/指针/例外日等发生变化（设置页保存后、自动播完推进后）</summary>
    public event Action? DataChanged;

    public ListeningService()
    {
        _data = ListeningStore.Load();
    }

    public ListeningData Data => _data;

    public void Save() => ListeningStore.Save(_data);

    public void Reload()
    {
        _data = ListeningStore.Load();
        DataChanged?.Invoke();
    }

    // ── 来源 ────────────────────────────────────────────────

    public List<ListeningSource> Sources => _data.Sources ??= new List<ListeningSource>();

    public ListeningSource? FindSource(string? id)
    {
        var key = (id ?? "").Trim();
        if (key.Length == 0) return null;
        return Sources.FirstOrDefault(s => string.Equals(s.Id, key, StringComparison.Ordinal));
    }

    /// <summary>活跃来源：最近播过的那一套；若它被删/未设置 → 退回第一条（没有则 null）</summary>
    public ListeningSource? ActiveSource
    {
        get
        {
            var hit = FindSource(_data.ActiveSourceId);
            if (hit != null) return hit;
            var first = Sources.FirstOrDefault();
            if (first != null) _data.ActiveSourceId = first.Id;
            return first;
        }
    }

    public ListeningSource AddSource(string name, string directory, bool once)
    {
        var src = new ListeningSource
        {
            Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(directory) : name.Trim(),
            Directory = (directory ?? "").Trim(),
            Once = once,
        };
        Sources.Add(src);
        if (string.IsNullOrEmpty(_data.ActiveSourceId)) _data.ActiveSourceId = src.Id;
        Save();
        DataChanged?.Invoke();
        Helpers.AppLogger.Info($"听力：新增来源「{src.Name}」→ {src.Directory}");
        return src;
    }

    public bool RemoveSource(string id)
    {
        var hit = FindSource(id);
        if (hit == null) return false;
        Sources.Remove(hit);
        if (string.Equals(_data.ActiveSourceId, id, StringComparison.Ordinal))
            _data.ActiveSourceId = Sources.FirstOrDefault()?.Id ?? "";
        Save();
        DataChanged?.Invoke();
        Helpers.AppLogger.Info($"听力：删除来源「{hit.Name}」");
        return true;
    }

    public void SetActive(string id)
    {
        if (FindSource(id) == null) return;
        if (string.Equals(_data.ActiveSourceId, id, StringComparison.Ordinal)) return;
        _data.ActiveSourceId = id;
        Save();
        DataChanged?.Invoke();
    }

    /// <summary>老师手动播了某个来源的文件 → 该来源成为活跃（"下次继续这套"）。一次性来源的清理由播完回退处理。</summary>
    public void PromoteToActive(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        SetActive(sourceId!);
    }

    // ── 例外日 ──────────────────────────────────────────────

    /// <summary>当天课表里的考试科目里有没有英语（SkipIfExam 用）</summary>
    public static bool HasEnglishExam(DateTime date)
    {
        try
        {
            var key = date.ToString("yyyy-MM-dd");
            foreach (var exam in App.Schedule.Data.Exams)
            {
                if (!string.Equals((exam.DateStr ?? "").Trim(), key, StringComparison.Ordinal)) continue;
                foreach (var s in exam.Subjects)
                    if ((s.Name ?? "").Contains("英语", StringComparison.Ordinal)) return true;
            }
        }
        catch { /* 课表未就绪 → 视为没有考试 */ }
        return false;
    }

    /// <summary>今天是否跳过自动播放（不播、也不推进进度）</summary>
    public bool ShouldSkipToday(DateTime date)
        => ListeningRules.ShouldSkipToday(_data, date, HasEnglishExam(date));

    // ── 自动播放解析 ────────────────────────────────────────

    /// <summary>
    /// 自动触发时解析"这次该播哪一份"。
    /// 返回 null 时 <paramref name="reason"/> 说明为什么（跳过 / 没来源 / 目录空 / 已听完），供日志与提示。
    /// </summary>
    public string? ResolveAuto(DateTime now, out string reason)
    {
        reason = "";
        if (ShouldSkipToday(now.Date))
        {
            reason = $"{now:yyyy-MM-dd} 是例外日（如英语周考由全校广播播放）→ 本次不播，也不推进进度";
            return null;
        }

        var src = ActiveSource;
        if (src == null)
        {
            reason = "还没有配置听力来源（设置 → 课表 → 听力播放器）";
            return null;
        }
        if (string.IsNullOrWhiteSpace(src.Directory) || !System.IO.Directory.Exists(src.Directory))
        {
            reason = $"来源「{src.Name}」的目录不存在：{src.Directory}";
            return null;
        }

        var cands = ListeningRules.CandidatesOf(src);
        if (cands.Count == 0)
        {
            reason = $"来源「{src.Name}」里没有音频文件：{src.Directory}";
            return null;
        }

        var file = ListeningRules.CurrentToPlay(src, cands);
        if (file == null)
        {
            reason = $"来源「{src.Name}」已听完（共 {cands.Count} 份）—— 请往目录里补新录音";
            return null;
        }
        return file;
    }

    /// <summary>播完（或播到末尾）后，一次性来源自动回退到上一个非一次性来源；返回是否发生了回退</summary>
    public bool FallbackFromOnceSource(ListeningSource? source)
    {
        if (source == null || !source.Once) return false;
        var back = Sources.FirstOrDefault(s => !s.Once);
        if (back == null) return false;
        _data.ActiveSourceId = back.Id;
        Save();
        DataChanged?.Invoke();
        Helpers.AppLogger.Info($"听力：一次性来源「{source.Name}」已播完 → 回退到「{back.Name}」");
        return true;
    }

    // ── 胶囊栏摘要 / 播放记录（v2.28.0，规划 2.9 第 2 步）─────────

    /// <summary>
    /// 主窗口胶囊栏用的一句话摘要。
    /// 没有任何可播内容（未配置来源 / 目录不存在 / 目录里没音频）→ 返回 null，胶囊不显示。
    /// </summary>
    /// <returns>(来源名, 完整一行, 紧凑视图用的短串, 今天是否例外日跳过)</returns>
    public (string Source, string Line, string Short, bool SkipToday)? GetCapsuleStatus(DateTime now)
    {
        var src = ActiveSource;
        if (src == null) return null;
        return ListeningRules.DescribeCapsule(src, CandidatesCached(src), ShouldSkipToday(now.Date));
    }

    // ── 候选文件短时缓存（2026-10-01 性能优化）────────────────────
    // 主窗口按 5 秒节拍调 GetCapsuleStatus 来刷新胶囊，而取候选要**枚举音频目录**（IO）。
    // 目录内容对"显示"来说是分钟级的事 → 10 秒缓存足够，省掉持续的目录扫描。
    // ⚠ 只用于显示路径；真正的"该播哪一份"（NextStrict / CurrentToPlay）不经过这里，永远读最新目录。
    private readonly Dictionary<string, (List<string> Items, DateTime At)> _candCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int CandsCacheSeconds = 10;

    private List<string> CandidatesCached(ListeningSource src)
    {
        var dir = src.Directory ?? "";
        if (string.IsNullOrWhiteSpace(dir)) return new List<string>();

        if (_candCache.TryGetValue(dir, out var hit) &&
            (DateTime.Now - hit.At).TotalSeconds < CandsCacheSeconds)
            return hit.Items;

        var list = ListeningRules.CandidatesOf(src);
        if (_candCache.Count > 8) _candCache.Clear();
        _candCache[dir] = (list, DateTime.Now);
        return list;
    }

    /// <summary>播放记录（进当天活动记录，与课件打开同一套 jsonl）；失败绝不影响播放</summary>
    public void RecordPlay(string path, bool auto)
    {
        try
        {
            Services.ActivityRecorder.RecordOpen(auto ? "auto" : "manual", path, "听力", ActiveSource?.Name ?? "");
        }
        catch { }
    }

    // ── 进度指针 ────────────────────────────────────────────

    /// <summary>刚要开播这一份：LastFile = 它，**Finished = false**（还没听完）。</summary>
    public void MarkPlaying(string sourceId, string file)
    {
        var src = FindSource(sourceId) ?? ActiveSource;
        if (src == null || string.IsNullOrWhiteSpace(file)) return;
        src.LastFile = file;
        src.Finished = false;
        _data.ActiveSourceId = src.Id;
        Save();
        DataChanged?.Invoke();
    }

    /// <summary>整份**自然播完**：Finished = true（下次才推进到下一份），并累加计数。</summary>
    public void MarkFinished(string sourceId, string file)
    {
        var src = FindSource(sourceId) ?? ActiveSource;
        if (src == null || string.IsNullOrWhiteSpace(file)) return;
        src.LastFile = file;
        src.Finished = true;
        src.LastAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        src.PlayedCount++;
        _data.ActiveSourceId = src.Id;
        Save();
        DataChanged?.Invoke();
    }

    /// <summary>老师手动播了别的来源的文件 → 活跃来源跟随（"换了一套就继续这套"）</summary>
    public void TouchManual(string sourceId, string file)
    {
        var src = FindSource(sourceId);
        if (src == null) return;
        MarkPlaying(sourceId, file);
    }
}
