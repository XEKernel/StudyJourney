using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace StudyJourney.Updater;

/// <summary>
/// 独立更新程序 — 由主应用唤起，负责下载→替换→重启流程
/// 命令行: StudyJourney.Updater.exe --pid 12345 --zip "C:\update.zip" --target "C:\app" --exe "C:\app\学程.exe"
/// </summary>
class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    private const uint MB_OK = 0, MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30;
    private static void ShowError(string msg) => MessageBoxW(IntPtr.Zero, msg, "学程更新", MB_OK | MB_ICONERROR);
    private static void ShowWarn(string msg) => MessageBoxW(IntPtr.Zero, msg, "学程更新", MB_OK | MB_ICONWARNING);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Log($"=== 启动 pid={Environment.ProcessId} ===");
            Log("收到参数: " + string.Join(" | ", args.Select(a => $"\"{a}\"")));

            var opts = ParseArgs(args);
            if (opts == null)
            {
                // 把收到的参数一并显示 —— 2026-09-17 那次「更新参数错误」就是因为
                // 只报一句笼统错误，看不出到底哪个参数没接住（实为 --target 尾部的
                // 反斜杠把引号转义掉了，导致 --exe 丢失）。
                Log("参数解析失败");
                ShowError("更新程序参数错误。\n\n收到：" +
                          (args.Length == 0 ? "(无)" : string.Join(" ", args)));
                return 1;
            }

            // 1. 等待主进程退出（最多 15 秒，超时强杀）
            if (opts.Pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(opts.Pid);
                    if (!proc.HasExited)
                    {
                        proc.WaitForExit(15_000);
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(5_000);
                        }
                    }
                }
                catch { /* 进程已退出 */ }
            }

            // 等待文件句柄释放
            Thread.Sleep(1000);

            // 本进程自己住在哪个目录 —— 决定"要不要跳过自己那一组文件"和"能不能当场删临时目录"
            string runDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";

            // 2. 准备新文件：优先用主程序已解压好的 staging 目录
            //    （主程序在退出前已校验过解压结果，这里就只是拷文件，最不容易出错）；
            //    没有才自己解压 zip（兼容旧版主程序或手工调用）。
            string workDir;
            if (!string.IsNullOrEmpty(opts.StagedDir) && Directory.Exists(opts.StagedDir))
            {
                workDir = opts.StagedDir;
                Log($"使用主程序解压好的 staging 目录：{workDir}");
            }
            else if (!string.IsNullOrEmpty(opts.ZipPath) && File.Exists(opts.ZipPath))
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"StudyJourneyUpdate_{DateTime.Now:yyyyMMddHHmmss}");
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(opts.ZipPath, tempDir, true);
                workDir = tempDir;
                Log($"自行解压更新包：{opts.ZipPath} → {tempDir}");
            }
            else
            {
                Log("既没有 staging 目录也没有可用的更新包");
                ShowError("没有可用的更新内容。\n\n请重新下载更新包。");
                return 1;
            }

            Log($"本进程目录：{runDir}；目标目录：{opts.TargetDir}；内容目录：{workDir}");

            List<string> failedFiles;
            try
            {
                // 3. 复制文件（覆盖）
                //    ⚠ 只有"更新程序自己就跑在目标目录里"时才需要跳过 StudyJourney.Updater.*
                //    —— 运行时它的 exe/dll 被系统锁住，覆盖必抛共享冲突。
                //    新版主程序改从 staging（%TEMP%）启动更新程序，于是目标目录里没有任何文件
                //    被本进程占用：这时**该**把更新程序自己也更新掉，而不是跳过。
                //
                //    背景（2026-09-19 用户实测的故障）：更新程序是自包含应用，从程序目录启动时
                //    会把 coreclr.dll / System.Private.CoreLib.dll 等运行时 DLL 也映射成
                //    "来自程序目录"，覆盖它们必然失败 → 报"某个 DLL 正由另一进程使用"。
                //    根因在主程序（不该从程序目录启动它），这里再要求"本进程没有占用目标目录"
                //    也只是兜底。
                bool runningInsideTarget = SamePath(runDir, opts.TargetDir);
                if (runningInsideTarget)
                    Log("⚠ 本进程正跑在目标目录里（旧版主程序的行为）：将跳过更新程序自身那一组文件");
                string? skipPrefix = runningInsideTarget ? "StudyJourney.Updater." : null;

                // 3.0 先算出"该删哪些旧文件"。
                //     ⚠ 必须在复制**之前**读旧清单 —— 复制会把目标目录里那份
                //       install-manifest.txt 覆盖成本次包里的新清单，之后就比不出来了。
                var removedList = PlanRemovedFiles(workDir, opts.TargetDir);

                failedFiles = CopyDirectory(workDir, opts.TargetDir, skipPrefix);

                // 3.5 再执行清理。顺序是「先复制、后删除」而不是反过来：
                //     万一删除环节出问题，程序目录仍是**完整可用**的（只是多了几个旧文件）；
                //     反过来的话就可能出现"旧的删了、新的没拷进来"的残缺状态。
                DeletePlannedFiles(removedList, opts.TargetDir);

                // 4. 清理临时内容
                //    ⚠ 本进程就跑在 workDir 里时当场删不掉自己（exe/dll 被占用）→ 交给
                //      退出后的清理脚本删（放在 ScheduleSelfDelete 里）。
                string? dirToClean = SamePath(runDir, workDir) ? workDir : null;
                if (dirToClean == null)
                {
                    try { Directory.Delete(workDir, true); } catch { }
                }
                else
                {
                    Log($"本进程住在内容目录里，退出后由清理脚本删除：{dirToClean}");
                }

                if (!string.IsNullOrEmpty(opts.ZipPath))
                {
                    try { File.Delete(opts.ZipPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"替换失败：{ex}");
                ShowError($"更新文件替换失败：{ex.Message}");
                return 1;
            }

            // 5. 启动新版本
            try
            {
                Log($"启动新版本：{opts.ExePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = opts.ExePath,
                    UseShellExecute = true,
                    WorkingDirectory = opts.TargetDir
                });
            }
            catch (Exception ex)
            {
                Log($"启动新版本失败：{ex.Message}");
                // ⚠ 文案必须说清"文件已经换好了"。2026-09-24 用户看到这条提示时第一反应是
                //    "更新失败了"，其实更新已经完成、只是自动启动那一步没成功 —— 两回事。
                ShowWarn("学程已更新完成，但没能自动打开它。\n\n请手动打开：\n" +
                         opts.ExePath + "\n\n（原因：" + ex.Message + "）");
            }

            // 5.5 有个别文件没覆盖成功时告诉老师（但仍然照常重启：程序退不回去，
            //     把情况说清楚比让程序停在旧版本半截状态更好）。
            if (failedFiles.Count > 0)
            {
                var head = string.Join("\n", failedFiles.Take(5));
                var more = failedFiles.Count > 5 ? $"\n…还有 {failedFiles.Count - 5} 个" : "";
                Log("以下文件未能覆盖：\n" + string.Join("\n", failedFiles));
                ShowWarn($"有 {failedFiles.Count} 个文件没能替换（多半是被杀毒软件、或另一个还在运行的" +
                         $"学程进程占用）：\n\n{head}{more}\n\n" +
                         "程序照常启动。若功能异常，请关闭全部学程进程后重新更新一次。");
            }

            // 6. 自清理（顺带删掉"本进程住的临时目录"）
            Log("完成，准备自清理");
            ScheduleSelfDelete(SamePath(runDir, workDir) ? workDir : null);
        }
        catch (Exception ex)
        {
            Log($"未预期的失败：{ex}");
            ShowError($"更新失败：{ex.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 记一份日志到 %TEMP%\StudyJourneyUpdate\updater.log。
    /// 更新程序是独立进程、出错时主程序已经退出，没有日志就完全查不到原因（有前车之鉴）。
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "StudyJourneyUpdate");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "updater.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响更新 */ }
    }

    private static UpdaterOptions? ParseArgs(string[] args)
    {
        int pid = 0;
        string? staged = null, zip = null, target = null, exe = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length: pid = int.Parse(args[++i]); break;
                case "--staged" when i + 1 < args.Length: staged = args[++i]; break;
                case "--zip" when i + 1 < args.Length: zip = args[++i]; break;
                case "--target" when i + 1 < args.Length: target = args[++i]; break;
                case "--exe" when i + 1 < args.Length: exe = args[++i]; break;
            }
        }

        // 去掉路径尾部的反斜杠/正斜杠（拼参数或再拼接时容易出问题）
        static string? Trim(string? p) => string.IsNullOrEmpty(p) ? p : p.TrimEnd('\\', '/');

        target = Trim(target);
        exe = Trim(exe);
        staged = Trim(staged);
        zip = Trim(zip);

        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(exe)) return null;

        // 更新内容二选一：给了 staging 目录，或者给了（存在的）zip
        bool hasStaged = !string.IsNullOrEmpty(staged) && Directory.Exists(staged);
        bool hasZip = !string.IsNullOrEmpty(zip) && File.Exists(zip);
        if (!hasStaged && !hasZip) return null;

        return new UpdaterOptions
        {
            Pid = pid,
            StagedDir = hasStaged ? staged! : "",
            ZipPath = hasZip ? zip! : "",
            TargetDir = target,
            ExePath = exe,
        };
    }

    /// <summary>
    /// 用户数据文件 —— 更新时**绝不覆盖**（2026-09-17 新增）。
    ///
    /// 更新流程是"把包内文件覆盖到程序目录"，如果包里恰好带了这些文件，就会把老师的
    /// 设置/课表/自动化规则冲掉。实测 v2.12.0 之前的发布包确实误带了 settings.json
    /// （csproj 里有一条 CopyToOutputDirectory 把它打进产物），靠这里做第二道防线：
    /// 即使某个旧包/手工解压带上了它们，也不会覆盖用户数据。
    ///
    /// 同时也防"老师自己把 settings.json 放进 zip 手动升级"这种误操作。
    /// 真要发默认配置，请用别的文件名（如 schedule_example.json），不要用这些名字。
    /// </summary>
    private static readonly string[] ProtectedUserDataFiles =
    {
        "settings.json",        // 应用设置（账号/课表参数/上传目录/更新镜像…）
        "schedule.json",        // 课表
        "automations.json",     // 自动化规则
        "open-state.json",      // 打开类动作运行期状态
        "pdf-state.json",       // PDF 阅读进度
        "tokens.json",          // 远程登录 token
    };

    private static bool IsProtectedUserData(string fileName)
        => ProtectedUserDataFiles.Any(p => string.Equals(p, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 把 source 下的文件覆盖到 dest，返回**没能覆盖的文件**（相对路径 + 原因）。
    ///
    /// 两个要点（2026-09-19 故障复盘）：
    ///   · 单个文件失败**不再让整包失败** —— 原来第一处共享冲突就抛出、整个更新中断，
    ///     程序目录被留在"一半新一半旧"的状态里，还不会重启。现在失败的文件继续往后拷，
    ///     最后统一报出来（并仍然重启程序）。
    ///   · 每个文件带**重试**：杀毒软件扫描、资源管理器索引、另一个还在退出的学程进程
    ///     都可能短暂锁住文件，等几百毫秒再试通常就过了。
    /// </summary>
    /// <param name="skipFileNamePrefix">跳过该前缀的文件（仅当更新程序自己就跑在目标目录里时才需要）</param>
    private static List<string> CopyDirectory(string source, string dest, string? skipFileNamePrefix = null)
    {
        Directory.CreateDirectory(dest);
        int skipped = 0, copied = 0, protectedSkipped = 0;
        var failed = new List<string>();

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(file);

            if (!string.IsNullOrEmpty(skipFileNamePrefix) &&
                fileName.StartsWith(skipFileNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            // 2026-09-22：保护只对**包根目录**下的同名文件生效。
            // 原来按纯文件名匹配且递归所有子目录 —— 一旦包里某个子目录恰有同名文件
            // （如以后放个样例 settings.json 到 samples/）就会被静默跳过、造成新旧混装。
            bool isRootFile = !Path.GetRelativePath(source, file).Contains(Path.DirectorySeparatorChar);
            if (isRootFile && IsProtectedUserData(fileName))
            {
                protectedSkipped++;
                Log($"跳过用户数据文件（不覆盖）：{Path.GetRelativePath(source, file)}");
                continue;
            }

            string rel = Path.GetRelativePath(source, file);
            string destFile = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (TryCopyWithRetry(file, destFile, out string reason))
            {
                copied++;
            }
            else
            {
                failed.Add($"{rel}（{reason}）");
                Log($"复制失败：{rel} — {reason}");
            }
        }

        Log($"复制完成：成功 {copied} 个；跳过更新器自身 {skipped} 个；" +
            $"跳过用户数据 {protectedSkipped} 个；失败 {failed.Count} 个");
        return failed;
    }

    /// <summary>
    /// 安装清单文件名。**必须与 CI 生成的那份逐字一致**
    /// （`.github/workflows/dotnet-desktop.yml` 的 "Generate install manifest" 步骤）。
    /// </summary>
    private const string ManifestFileName = "install-manifest.txt";

    /// <summary>
    /// 算出"上一次装着、但本次包里已经没有了"的文件（相对路径列表）。
    ///
    /// 背景（2026-09-24 用户提出）：更新一直是**只覆盖、不删除** —— 旧版本独有的文件
    /// （合并掉的 DLL、改名的资源）会永远留在程序目录里。轻则目录越来越乱、可能加载到
    /// 旧组件；将来切 NativeAOT 单文件分发时，残留的几十个运行时 DLL 更是会让"单文件"
    /// 变成假象。
    ///
    /// 关键设计：用**清单差分**，而不是"扫描目录里多余的文件然后删掉"。
    ///   新清单 = 本次包内 install-manifest.txt 列出的文件
    ///   旧清单 = 目标目录里 install-manifest.txt（上一次安装时随包写下的）
    ///   要删的 = 旧清单 − 新清单
    /// 这样**只删我们曾经装进去的东西** —— 老师自己往程序目录里放的任何文件都不在旧清单里，
    /// 天然不会被碰；用户数据文件（settings.json 等）另有一层名单保险。
    ///
    /// ⚠ 目标目录没有旧清单时**直接跳过**（不删任何文件）：那是"第一个带清单的版本"，
    ///   本次装好后从下一次更新起才具备清理能力。**绝不要**改成"扫到多余的就删" ——
    ///   那会开始吃老师的文件。
    /// </summary>
    private static List<string> PlanRemovedFiles(string newContentDir, string targetDir)
    {
        var newSet = ReadManifest(newContentDir);
        if (newSet == null)
        {
            Log("本次包里没有安装清单（旧包或手工准备的目录）→ 跳过旧文件清理");
            return new List<string>();
        }

        var oldSet = ReadManifest(targetDir);
        if (oldSet == null)
        {
            Log("目标目录没有安装清单（首次带清单安装）→ 本次不删除任何文件；" +
                "装好后，从下一次更新开始具备清理旧文件的能力");
            return new List<string>();
        }

        var toDelete = oldSet.Where(x => !newSet.Contains(x)).ToList();
        Log(toDelete.Count == 0
            ? "旧文件清理：没有需要删除的文件"
            : $"旧文件清理：待删除 {toDelete.Count} 个（旧清单 {oldSet.Count} 项 − 新清单 {newSet.Count} 项）");
        return toDelete;
    }

    /// <summary>执行 <see cref="PlanRemovedFiles"/> 算出的删除列表。单个失败不影响其余，也不影响更新结果。</summary>
    private static void DeletePlannedFiles(List<string> toDelete, string targetDir)
    {
        if (toDelete.Count == 0) return;

        string fullTarget = Path.GetFullPath(targetDir).TrimEnd('\\', '/');
        int ok = 0, missing = 0, skipped = 0, failed = 0;

        foreach (string rel in toDelete)
        {
            if (!IsSafeRelativePath(rel))
            {
                Log($"⚠ 清单里的路径不安全，跳过：{rel}");
                failed++;
                continue;
            }

            string path = Path.GetFullPath(Path.Combine(fullTarget, rel.Replace('/', Path.DirectorySeparatorChar)));

            // 双保险：解析后必须真的落在目标目录内（防 `..` 逃逸、盘符跳转）
            if (!path.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Log($"⚠ 解析后不在目标目录内，跳过：{rel}");
                failed++;
                continue;
            }

            // 用户数据文件永远不删（即使它诡异地出现在清单里也不动）
            if (IsProtectedUserData(Path.GetFileName(path)))
            {
                Log($"跳过用户数据文件（不删除）：{rel}");
                skipped++;
                continue;
            }

            try
            {
                if (!File.Exists(path)) { missing++; continue; }
                File.Delete(path);
                ok++;
                Log($"已清理旧文件：{rel}");
            }
            catch (Exception ex)
            {
                // 被占用/权限不足都只记录 —— 多留一个旧文件不影响使用，绝不能因此让更新失败
                failed++;
                Log($"清理失败（不影响本次更新）：{rel} — {ex.Message}");
            }
        }

        Log($"旧文件清理结果：删除 {ok} 个；本来就不存在 {missing} 个；" +
            $"保护跳过 {skipped} 个；失败 {failed} 个");

        // 顺手删掉因此变空的目录（按路径长度倒序 = 先深后浅，否则删父目录时里面还有东西）
        try
        {
            foreach (string dir in Directory.GetDirectories(fullTarget, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    Log($"已清理空目录：{Path.GetRelativePath(fullTarget, dir)}");
                }
            }
        }
        catch { /* 清理空目录纯属锦上添花，失败无所谓 */ }
    }

    /// <summary>读安装清单（正斜杠归一化）。文件不存在或读不出 → null，调用方据此跳过清理。</summary>
    private static HashSet<string>? ReadManifest(string dir)
    {
        try
        {
            string file = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(file)) return null;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim().TrimStart('\uFEFF').Replace('\\', '/').TrimStart('/');
                if (line.Length == 0) continue;
                set.Add(line);
            }
            return set.Count > 0 ? set : null;
        }
        catch (Exception ex)
        {
            Log($"读取安装清单失败（按「无清单」处理，即不删除任何文件）：{dir} — {ex.Message}");
            return null;
        }
    }

    /// <summary>清单里的相对路径是否安全（不接受绝对路径、盘符/ADS、空段、`.` 与 `..`）</summary>
    private static bool IsSafeRelativePath(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return false;
        if (rel.Contains(':')) return false;
        if (Path.IsPathRooted(rel)) return false;

        foreach (string seg in rel.Split('/', '\\'))
            if (seg.Length == 0 || seg == "." || seg == "..") return false;

        return true;
    }

    /// <summary>带退避重试的覆盖复制（共享冲突/权限问题大多是短暂的）</summary>
    private static bool TryCopyWithRetry(string source, string dest, out string reason)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, dest, true);
                reason = "";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= MaxCopyRetries)
                {
                    reason = ex.Message;
                    return false;
                }
                Thread.Sleep(250 * attempt);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }

    /// <summary>单个文件覆盖的最大尝试次数（含首次）</summary>
    private const int MaxCopyRetries = 5;

    /// <summary>两个目录是否同一个（忽略大小写与尾部分隔符；解析失败按不同处理）</summary>
    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// 本进程退出后删除自身的 exe（以及可选的一个临时目录）。
    ///
    /// ⚠ 原实现用 `:retry / del / if exist goto retry` **无延时忙等** —— 正在运行的 exe
    /// 删不掉，于是 bat 会以满 CPU 空转直到本进程真正退出。这里改成"带延时 + 次数上限"。
    /// </summary>
    /// <param name="extraDirToDelete">
    /// 本进程"住在里面"的临时目录（新版主程序从 staging 启动更新程序）。当场删不掉自己，
    /// 只能等退出后由这里删。**绝不能传目标目录**（调用方已保证传进来的不是目标目录）。
    /// </param>
    private static void ScheduleSelfDelete(string? extraDirToDelete = null)
    {
        try
        {
            string self = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(self)) return;

            string bat = Path.Combine(Path.GetTempPath(), $"sj_updater_cleanup_{DateTime.Now.Ticks}.bat");

            string workLine = string.IsNullOrEmpty(extraDirToDelete)
                ? ""
                : $"set \"WORK={extraDirToDelete}\"\r\n";
            string workClean = string.IsNullOrEmpty(extraDirToDelete)
                ? ""
                : "if defined WORK rmdir /S /Q \"%WORK%\" >nul 2>&1\r\n";

            // ping 当延时用（timeout 命令需要控制台，隐藏窗口下会报错）
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "setlocal\r\n" +
                $"set \"SELF={self}\"\r\n" +
                workLine +
                "set /a n=0\r\n" +
                ":retry\r\n" +
                "del /F /Q \"%SELF%\" >nul 2>&1\r\n" +
                "if not exist \"%SELF%\" goto done\r\n" +
                "set /a n+=1\r\n" +
                "if %n% GEQ 60 goto done\r\n" +
                "ping -n 1 -w 500 127.0.0.1 >nul\r\n" +
                "goto retry\r\n" +
                ":done\r\n" +
                workClean +
                "del /F /Q \"%~f0\" >nul 2>&1\r\n");

            Process.Start(new ProcessStartInfo
            {
                FileName = bat,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });
        }
        catch { }
    }

    class UpdaterOptions
    {
        public int Pid { get; set; }
        /// <summary>主程序已解压好的目录（首选）</summary>
        public string StagedDir { get; set; } = "";
        /// <summary>更新包（回退方案：本程序自行解压）</summary>
        public string ZipPath { get; set; } = "";
        public string TargetDir { get; set; } = "";
        public string ExePath { get; set; } = "";
    }
}
