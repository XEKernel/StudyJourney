using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Threading;

namespace StudyJourney.Avalonia.Services;

/// <summary>
/// 上课活动记录（2026-09-24 新增，用户要求）。
///
/// **为什么要它**：老师上课实际打开的课件顺序"有规律但不确定"——自动化按编号猜的"下一份"经常不是
/// 老师真正要的那份。要改进顺序逻辑，就得先拿到**真实使用数据**：一天里到底按什么次序打开了哪些文件。
/// 所以这里把一天里发生的事都记下来：
///   · **打开文件/软件** —— 区分「自动化打开」与「老师自己打开」（后者靠轮询顶层窗口标题识别）
///   · **U 盘插拔** —— 老师常把课件放 U 盘，插上后打开的那份往往就是"今天要讲的"
///
/// 落盘到程序目录 `records/activity-yyyy-MM-dd.jsonl`（**一天一个文件、追加写**），
/// 便于用 <see cref="DiagnosticPackager"/> 打包带走做分析。
///
/// ⚠ 只在设置里显式开启后才记录（默认关）—— 这是行为日志，应当由使用者明确同意。
/// ⚠ 记录里**不含**任何密码/token；只记文件路径、标题、驱动器等。
/// </summary>
public static class ActivityRecorder
{
    /// <summary>记录根目录（程序目录\records）</summary>
    public static string RecordDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "records");

    private static DispatcherTimer? _timer;
    private static bool _running;

    /// <summary>已记过的窗口标题（避免同一份课件反复记录）</summary>
    private static readonly HashSet<string> _seenTitles = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>当前在线的可移动盘（用于判断插入/移除）</summary>
    private static readonly HashSet<string> _presentDrives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>标题里出现这些扩展名 → 认为这个窗口在展示某个"课件/文档"</summary>
    private static readonly string[] DocExts =
    {
        ".pdf", ".ppt", ".pptx", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
        ".mp4", ".avi", ".mkv", ".wmv", ".mov", ".mp3", ".wav", ".flac",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
    };

    /// <summary>自己的窗口标题里带这些字样 → 不记（否则会把自己的界面当成课件）</summary>
    private static readonly string[] SelfMarks = { "学程", "StudyJourney", "高考倒计时" };

    /// <summary>开始记录（App 启动时按设置调用；重复调用安全）</summary>
    public static void Start()
    {
        if (_running) return;
        _running = true;

        try
        {
            Directory.CreateDirectory(RecordDir);
            // 先把当前已存在的窗口与盘符登记一遍：否则启动那一刻开着的窗口会被当成"刚打开"全记一遍
            try
            {
                foreach (var w in Helpers.WindowEnumerator.TopLevelWindows())
                    if (!string.IsNullOrWhiteSpace(w.Title)) _seenTitles.Add(w.Title);
            }
            catch { }
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Removable && d.IsReady) _presentDrives.Add(d.Name);
            }
            catch { }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (_, _) => { PollWindows(); PollDrives(); };
            _timer.Start();

            Record("session", "start", "", "", "", "");
            Helpers.AppLogger.Info($"[记录] 已开始上课活动记录 → {RecordDir}");
        }
        catch (Exception ex)
        {
            _running = false;
            Helpers.AppLogger.Error("[记录] 启动失败", ex);
        }
    }

    /// <summary>停止记录</summary>
    public static void Stop()
    {
        _running = false;
        try { _timer?.Stop(); } catch { }
        _timer = null;
    }

    /// <summary>
    /// 记录一次"打开"。自动化打开时由 AutomationService 调用；
    /// 老师手动打开的由窗口轮询识别（见 <see cref="PollWindows"/>）。
    /// </summary>
    /// <param name="source">auto = 自动化打开；manual = 老师自己打开</param>
    public static void RecordOpen(string source, string path, string subject = "", string ruleName = "")
        => Record(source == "auto" ? "open" : "open", source, SafeName(path), path, subject, ruleName);

    // ── 内部 ────────────────────────────────────────────────

    /// <summary>把一份记录追加到当天的 jsonl 文件</summary>
    private static void Record(string kind, string action, string name, string path,
        string subject, string extra)
    {
        if (!_running) return;
        try
        {
            Directory.CreateDirectory(RecordDir);
            string file = Path.Combine(RecordDir, $"activity-{DateTime.Now:yyyy-MM-dd}.jsonl");

            // 手写 JSON（只有固定几个字段，不引序列化器；也避免把奇怪字符写坏）
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"t\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",");
            sb.Append("\"kind\":\"").Append(Esc(kind)).Append("\",");
            sb.Append("\"action\":\"").Append(Esc(action)).Append("\",");
            sb.Append("\"name\":\"").Append(Esc(name)).Append("\",");
            sb.Append("\"path\":\"").Append(Esc(path)).Append("\",");
            sb.Append("\"subject\":\"").Append(Esc(subject)).Append("\",");
            sb.Append("\"extra\":\"").Append(Esc(extra)).Append("\"");
            sb.Append('}');

            File.AppendAllText(file, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* 记录失败绝不能影响上课 */ }
    }

    private static string Esc(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    private static string SafeName(string path)
    {
        try { return Path.GetFileName(path); } catch { return path ?? ""; }
    }

    /// <summary>轮询顶层窗口，识别"老师自己打开了某个课件"</summary>
    private static void PollWindows()
    {
        try
        {
            foreach (var w in Helpers.WindowEnumerator.TopLevelWindows())
            {
                string title = w.Title ?? "";
                if (title.Length == 0) continue;
                if (_seenTitles.Contains(title)) continue;

                // 自己的窗口不记
                if (SelfMarks.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    _seenTitles.Add(title);
                    continue;
                }

                // 只有标题里含"文档类扩展名"才认为是在看课件 —— 否则会把浏览器/微信/资源管理器全记进来
                string? ext = DocExts.FirstOrDefault(e => title.Contains(e, StringComparison.OrdinalIgnoreCase));
                if (ext == null) continue;

                _seenTitles.Add(title);

                string exeName = "";
                try { exeName = System.Diagnostics.Process.GetProcessById((int)w.Pid).ProcessName; }
                catch { }

                Record("open", "manual", title, "", "", exeName.Length > 0 ? $"由 {exeName} 打开" : "");
            }
        }
        catch { }
    }

    /// <summary>轮询可移动盘，记录 U 盘插入/拔出</summary>
    private static void PollDrives()
    {
        try
        {
            var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Removable) continue;
                now.Add(d.Name);
                if (_presentDrives.Contains(d.Name)) continue;

                // 新出现的可移动盘 = 插入
                string label = "", size = "";
                try { label = d.IsReady ? d.VolumeLabel ?? "" : ""; } catch { }
                try { size = d.IsReady ? $"{d.TotalSize / 1024.0 / 1024 / 1024:0.0} GB" : ""; } catch { }
                Record("usb", "in", label, d.Name, "", size);
            }

            // 之前在线、现在不在了 = 拔出
            foreach (var gone in _presentDrives.Where(x => !now.Contains(x)).ToList())
                Record("usb", "out", "", gone, "", "");

            _presentDrives.Clear();
            foreach (var x in now) _presentDrives.Add(x);
        }
        catch { }
    }
}
