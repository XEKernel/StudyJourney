using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace StudyJourney.Avalonia.Services;

/// <summary>
/// 诊断包生成（2026-09-24 新增，用户要求）。
///
/// 把分析"课件顺序为什么不对"所需的东西打成一个 zip 放到桌面，用户带过来即可。
/// 内容见 <see cref="CreateAsync"/> 里的分节。
///
/// ⚠ **安全**：`settings.json` 里有老师账号的密码哈希、`tokens.json` 里有远程登录 token ——
/// 两者**绝不进包**。settings.json 会**剔除** Password/PasswordHash 字段后再放入。
/// </summary>
public static class DiagnosticPackager
{
    /// <summary>生成诊断包，返回 zip 的完整路径；失败返回 null（原因已记日志）</summary>
    public static string? Create()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string root = Path.Combine(Path.GetTempPath(), $"StudyJourneyDiag-{stamp}");
        var notes = new List<string>();

        try
        {
            Directory.CreateDirectory(root);
            notes.Add($"学程诊断包  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            notes.Add($"版本：{UpdateService.CurrentVersion}");
            notes.Add("");

            int n;

            // ① 活动记录（一天一个 jsonl）
            n = CopyDir(ActivityRecorder.RecordDir, Path.Combine(root, "活动记录"));
            notes.Add($"活动记录/      —— {n} 个文件（自动化打开 / 老师手动打开 / U 盘插拔）");

            // ② 课件目录清单（**带解析出的序号**，这是分析顺序规律的关键）
            n = WriteCoursewareListing(Path.Combine(root, "课件清单.txt"));
            notes.Add($"课件清单.txt   —— {n} 个课件（含解析出的序号、大小、修改时间）");

            // ③ 桌面文件树
            n = WriteDesktopTree(Path.Combine(root, "桌面文件树.txt"));
            notes.Add($"桌面文件树.txt —— {n} 个条目（深度≤3，桌面目录）");

            // ④ 配置快照（脱敏）
            n = WriteConfigSnapshots(Path.Combine(root, "配置"));
            notes.Add($"配置/          —— {n} 个文件（settings.json 已剔除密码字段；tokens.json 不打包）");

            // ⑤ 系统信息
            WriteSystemInfo(Path.Combine(root, "系统信息.txt"));
            notes.Add("系统信息.txt   —— 操作系统 / 运行时 / 屏幕 / 驱动器");

            // ⑥ 应用日志（只取尾部，避免包过大）
            n = CopyLogTail(Path.Combine(root, "日志"), 3000);
            notes.Add($"日志/          —— app.log 尾部 {n} 行");

            notes.Add("");
            notes.Add("说明：本包不含任何密码、token 或聊天内容；只有文件路径/名称、窗口标题与设备信息。");
            File.WriteAllText(Path.Combine(root, "说明.txt"), string.Join(Environment.NewLine, notes), Encoding.UTF8);

            // 打包到桌面
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string zip = Path.Combine(desktop, $"学程诊断包-{stamp}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(root, zip, CompressionLevel.Optimal, includeBaseDirectory: false);

            Helpers.AppLogger.Info($"[诊断包] 已生成：{zip}");
            return zip;
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Error("[诊断包] 生成失败", ex);
            return null;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ── 各分节 ──────────────────────────────────────────────

    private static int CopyDir(string src, string dst)
    {
        if (!Directory.Exists(src)) return 0;
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var f in Directory.GetFiles(src))
        {
            try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; } catch { }
        }
        return n;
    }

    /// <summary>
    /// 课件清单：列出「上传目录\课件\&lt;科目&gt;」下每个文件，**并给出 FileSequence 解析出的序号** ——
    /// 分析"顺序规律"时最需要的就是"文件名 → 解析序号"这一列（一眼看出哪些文件解析错了）。
    /// </summary>
    private static int WriteCoursewareListing(string outFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("课件目录清单（每份课件 + FileSequence 解析出的序号）");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("序号列说明：[解析失败] = 文件名里没识别出序号，会排到最后；");
        sb.AppendLine("            数字 = 解析出的序号（阿拉伯与中文数字都支持，如 “第3讲”“三 化学平衡”）。");
        sb.AppendLine();

        int total = 0;
        try
        {
            string root = Path.Combine(HttpServerService.UploadRootPath, "课件");
            sb.AppendLine($"课件根目录：{root}");
            sb.AppendLine();

            if (!Directory.Exists(root))
            {
                sb.AppendLine("（目录不存在 —— 老师还没上传过课件，或上传目录设置不同）");
            }
            else
            {
                foreach (var dir in Directory.GetDirectories(root).OrderBy(x => x))
                {
                    var files = Directory.GetFiles(dir);
                    sb.AppendLine($"── {Path.GetFileName(dir)}（{files.Length} 个）──");

                    // 按解析出的序号排一下，便于人眼核对（解析失败的排最后）
                    var ordered = files
                        .OrderBy(f => Helpers.FileSequence.ExtractNumber(f) > 0 ? 0 : 1)
                        .ThenBy(f => Helpers.FileSequence.ExtractNumber(f))
                        .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (var f in ordered)
                    {
                        int seq = Helpers.FileSequence.ExtractNumber(f);
                        string seqText = seq > 0 ? seq.ToString().PadLeft(4) : "[解析失败]";
                        var fi = new FileInfo(f);
                        sb.AppendLine($"  {seqText}  {Path.GetFileName(f)}" +
                                      $"   （{fi.Length / 1024} KB，{fi.LastWriteTime:yyyy-MM-dd HH:mm}）");
                        total++;
                    }
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"（读取课件目录失败：{ex.Message}）"); }

        File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
        return total;
    }

    /// <summary>桌面文件树（限深度与数量，避免桌面东西多时生成一个巨长的文件）</summary>
    private static int WriteDesktopTree(string outFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("桌面文件树（深度 ≤ 3）");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        int count = 0;
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            sb.AppendLine($"桌面路径：{desktop}");
            sb.AppendLine();
            Walk(sb, desktop, "", 0, 3, ref count);
        }
        catch (Exception ex) { sb.AppendLine($"（读取桌面失败：{ex.Message}）"); }

        File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
        return count;
    }

    private static void Walk(StringBuilder sb, string dir, string indent, int depth, int maxDepth, ref int count)
    {
        if (depth > maxDepth || count > 3000) return;
        try
        {
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x))
            {
                sb.AppendLine($"{indent}📁 {Path.GetFileName(d)}/");
                count++;
                Walk(sb, d, indent + "   ", depth + 1, maxDepth, ref count);
            }
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                sb.AppendLine($"{indent}📄 {fi.Name}   （{fi.Length / 1024} KB，{fi.LastWriteTime:yyyy-MM-dd HH:mm}）");
                count++;
                if (count > 3000) { sb.AppendLine($"{indent}…（条目过多，已截断）"); return; }
            }
        }
        catch (UnauthorizedAccessException) { sb.AppendLine($"{indent}（无权限访问）"); }
        catch { }
    }

    /// <summary>配置快照：schedule / automations / open-state 原样；settings **剔除密码**</summary>
    private static int WriteConfigSnapshots(string outDir)
    {
        Directory.CreateDirectory(outDir);
        int n = 0;
        string app = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var name in new[] { "schedule.json", "automations.json", "open-state.json", "pdf-state.json" })
        {
            try
            {
                string src = Path.Combine(app, name);
                if (File.Exists(src)) { File.Copy(src, Path.Combine(outDir, name), true); n++; }
            }
            catch { }
        }

        // settings.json：**必须脱敏**（含密码哈希）
        try
        {
            string src = Path.Combine(app, "settings.json");
            if (File.Exists(src))
            {
                string json = File.ReadAllText(src);
                // 粗暴但可靠：把 Password / PasswordHash 的值替换掉（不解析结构，避免结构变化漏字段）
                json = System.Text.RegularExpressions.Regex.Replace(
                    json, "(\"(?:Password|PasswordHash)\"\\s*:\\s*)\"[^\"]*\"", "$1\"<已移除>\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                File.WriteAllText(Path.Combine(outDir, "settings.json"), json, Encoding.UTF8);
                n++;
            }
        }
        catch { }

        // tokens.json **不打包**（远程登录凭据）
        File.WriteAllText(Path.Combine(outDir, "注意.txt"),
            "本目录不含 tokens.json（远程登录凭据，出于安全不打包）。\r\n" +
            "settings.json 里的 Password / PasswordHash 已替换为 <已移除>。", Encoding.UTF8);
        n++;
        return n;
    }

    private static void WriteSystemInfo(string outFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("系统信息");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"操作系统：{Environment.OSVersion}");
        sb.AppendLine($".NET 运行时：{Environment.Version}");
        sb.AppendLine($"64 位进程：{Environment.Is64BitProcess}");
        sb.AppendLine($"机器名：{Environment.MachineName}   用户名：{Environment.UserName}");
        sb.AppendLine($"处理器数：{Environment.ProcessorCount}");
        sb.AppendLine($"系统启动于：{DateTime.Now.AddMilliseconds(-Environment.TickCount64):yyyy-MM-dd HH:mm}");
        sb.AppendLine($"本程序目录：{AppDomain.CurrentDomain.BaseDirectory}");
        sb.AppendLine($"上传目录：{HttpServerService.UploadRootPath}");
        sb.AppendLine($"当前时区：{TimeZoneInfo.Local.DisplayName}");
        sb.AppendLine();

        sb.AppendLine("── 驱动器 ──");
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                string info;
                try
                {
                    info = d.IsReady
                        ? $"{d.DriveType}  卷标={(string.IsNullOrEmpty(d.VolumeLabel) ? "-" : d.VolumeLabel)}  " +
                          $"总 {d.TotalSize / 1024.0 / 1024 / 1024:0.0} GB  可用 {d.AvailableFreeSpace / 1024.0 / 1024 / 1024:0.0} GB"
                        : $"{d.DriveType}  （未就绪）";
                }
                catch (Exception ex) { info = $"{d.DriveType}  （读取失败：{ex.Message}）"; }
                sb.AppendLine($"  {d.Name}  {info}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  （枚举失败：{ex.Message}）"); }

        sb.AppendLine();
        sb.AppendLine("── 已运行的自动化相关进程（便于确认软件路径）──");
        try
        {
            foreach (var (exe, title, path) in Helpers.WindowEnumerator.RunningApps().Take(60))
                sb.AppendLine($"  {exe}  |  {title}  |  {path}");
        }
        catch { }

        File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
    }

    private static int CopyLogTail(string outDir, int lines)
    {
        Directory.CreateDirectory(outDir);
        try
        {
            string src = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app.log");
            if (!File.Exists(src)) return 0;
            var all = File.ReadAllLines(src);
            var tail = all.Skip(Math.Max(0, all.Length - lines));
            File.WriteAllLines(Path.Combine(outDir, "app.log"), tail, Encoding.UTF8);
            return tail.Count();
        }
        catch { return 0; }
    }
}
