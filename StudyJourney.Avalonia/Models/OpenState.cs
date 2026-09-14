using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StudyJourney.Avalonia.Models;

// ── 打开类动作的"运行期记忆"（2.5.8 顺序记忆 / 2.5.9 连堂幂等 + 教师端指定）────────
//
// 为什么不放进 AutomationRule / automations.json：
//   AutomationPage 是「克隆规则 → 编辑 → 整表写回」的 dirty 模式，老师在中途打开设置页保存
//   无关改动，就会把服务端运行时更新过的"上次打开文件"一起覆盖回旧值。运行期状态与
//   规则定义分离，各存各的文件，互不干扰（沿用 automations.json 独立于 settings.json 的同一思路）。

/// <summary>单条规则的打开记忆</summary>
public class RuleOpenState
{
    /// <summary>顺序记忆指针：该规则"上次用到的文件"（自动触发默认续用它）</summary>
    public string LastOpenedFile { get; set; } = "";
    /// <summary>指针更新时间（ISO 字符串；仅用于展示与排查）</summary>
    public string LastOpenedAt { get; set; } = "";
}

/// <summary>由本软件启动的文件（路径 + 进程 Id），用于连堂幂等判断"是不是我们开的、还开着没"</summary>
public class TrackedOpen
{
    public string Path { get; set; } = "";
    public int Pid { get; set; }
    public string OpenedAt { get; set; } = "";
}

/// <summary>教师端网页指定的"下一节课打开什么"（一次性消费）。文件与软件都走这里</summary>
public class PendingOpen
{
    /// <summary>"file" = 打开文件（课件等）/ "app" = 启动软件</summary>
    public string Kind { get; set; } = "file";
    /// <summary>文件路径 或 可执行文件路径/名称</summary>
    public string Path { get; set; } = "";
    /// <summary>限定科目（空 = 不限，下一次任意打开类动作都消费它）</summary>
    public string Subject { get; set; } = "";
    /// <summary>给老师看的说明（可空）</summary>
    public string Note { get; set; } = "";
    public string SetBy { get; set; } = "";
    public string SetAt { get; set; } = "";
}

/// <summary>老师用过的软件（exe 路径 + 显示名），供教师端下次快速再选</summary>
public class KnownApp
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string LastUsedAt { get; set; } = "";
}

public class OpenStateData
{
    public Dictionary<string, RuleOpenState> Rules { get; set; } = new();
    public List<TrackedOpen> Opened { get; set; } = new();
    public PendingOpen? Pending { get; set; }

    /// <summary>常用软件（教师端指定过 / 打开过的软件，最多保留 20 条）</summary>
    public List<KnownApp> KnownApps { get; set; } = new();
}

/// <summary>
/// 打开状态存储（软件目录 open-state.json，原子写、随文件夹分发）。
/// 内存单例 + 锁：API 线程（教师端指定）与 UI 线程（自动化服务）共用同一份实例。
/// </summary>
public static class OpenStateStore
{
    private static readonly string StorePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "open-state.json");

    public static string FilePath => StorePath;

    private static readonly object Gate = new();
    private static OpenStateData _data = LoadFromDisk();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static OpenStateData LoadFromDisk()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var d = JsonSerializer.Deserialize<OpenStateData>(json, JsonOpts);
                if (d != null)
                {
                    d.Rules ??= new Dictionary<string, RuleOpenState>();
                    d.Opened ??= new List<TrackedOpen>();
                    d.KnownApps ??= new List<KnownApp>();
                    return d;
                }
            }
        }
        catch (Exception ex)
        {
            // 运行期状态文件损坏无需备份保留（可再生），重建成空即可
            Helpers.AppLogger.Warn($"open-state.json 加载失败，重建: {ex.Message}");
        }
        return new OpenStateData();
    }

    private static void SaveLocked()
    {
        try
        {
            Helpers.FileAtomic.WriteAllText(StorePath, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("保存 open-state.json 失败", ex);
        }
    }

    // ── 顺序记忆指针 ────────────────────────────────────────

    public static string GetPointer(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return "";
        lock (Gate)
            return _data.Rules.TryGetValue(ruleId, out var st) ? st.LastOpenedFile ?? "" : "";
    }

    public static void SetPointer(string ruleId, string filePath)
    {
        if (string.IsNullOrEmpty(ruleId) || string.IsNullOrWhiteSpace(filePath)) return;
        lock (Gate)
        {
            if (!_data.Rules.TryGetValue(ruleId, out var st))
                _data.Rules[ruleId] = st = new RuleOpenState();
            if (string.Equals(st.LastOpenedFile, filePath, StringComparison.OrdinalIgnoreCase)) return;
            st.LastOpenedFile = filePath;
            st.LastOpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveLocked();
        }
    }

    /// <summary>规则被删除时清理其记忆</summary>
    public static void RemoveRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;
        lock (Gate)
        {
            if (_data.Rules.Remove(ruleId)) SaveLocked();
        }
    }

    // ── 已打开进程跟踪（连堂幂等）────────────────────────────

    public static void TrackOpen(string filePath, int pid)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        lock (Gate)
        {
            _data.Opened.RemoveAll(o => string.Equals(o.Path, filePath, StringComparison.OrdinalIgnoreCase));
            _data.Opened.Add(new TrackedOpen
            {
                Path = filePath,
                Pid = pid,
                OpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
            PruneLocked();
            SaveLocked();
        }
    }

    /// <summary>该文件是否"由我们打开且进程仍存活"；命中时返回活着的进程 Id</summary>
    public static bool TryGetAlivePid(string filePath, out int pid)
    {
        pid = 0;
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        lock (Gate)
        {
            PruneLocked();
            var hit = _data.Opened.FirstOrDefault(o =>
                string.Equals(o.Path, filePath, StringComparison.OrdinalIgnoreCase));
            if (hit == null) return false;
            pid = hit.Pid;
            return true;
        }
    }

    /// <summary>进程已退出的记录就地清掉（避免列表无限增长）</summary>
    private static void PruneLocked()
    {
        _data.Opened.RemoveAll(o =>
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(o.Pid).HasExited;
            }
            catch
            {
                return true;   // 进程不存在/无权限 → 视为已退出
            }
        });
    }

    // ── 教师端指定（一次性消费）──────────────────────────────

    public static PendingOpen? GetPending()
    {
        lock (Gate) return _data.Pending;
    }

    public static void SetPending(PendingOpen pending)
    {
        lock (Gate)
        {
            _data.Pending = pending;
            SaveLocked();
        }
    }

    public static void ClearPending()
    {
        lock (Gate)
        {
            if (_data.Pending == null) return;
            _data.Pending = null;
            SaveLocked();
        }
    }

    /// <summary>
    /// 取走"适用于本次科目"的指定（有则一并清除 = 一次性消费）。
    /// subject 为空或指定未限定科目时视为适用。
    /// </summary>
    public static PendingOpen? ConsumePending(string? subject)
    {
        lock (Gate)
        {
            var p = _data.Pending;
            if (p == null) return null;
            if (!string.IsNullOrWhiteSpace(p.Subject) &&
                !string.Equals(p.Subject, subject?.Trim() ?? "", StringComparison.Ordinal))
                return null;
            _data.Pending = null;
            SaveLocked();
            return p;
        }
    }

    // ── 常用软件（教师端指定软件时快速再选）──────────────────

    public static List<KnownApp> GetKnownApps()
    {
        lock (Gate)
            return _data.KnownApps
                .OrderByDescending(a => a.LastUsedAt, StringComparer.Ordinal)
                .Take(20)
                .ToList();
    }

    public static void AddKnownApp(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate)
        {
            _data.KnownApps.RemoveAll(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            _data.KnownApps.Insert(0, new KnownApp
            {
                Path = path,
                Name = string.IsNullOrWhiteSpace(name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : name,
                LastUsedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
            if (_data.KnownApps.Count > 20)
                _data.KnownApps.RemoveRange(20, _data.KnownApps.Count - 20);
            SaveLocked();
        }
    }
}
