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

            // 2. 准备新文件：优先用主程序已解压好的 staging 目录
            //    （主程序在退出前已校验过解压结果，这里就只是拷文件，最不容易出错）；
            //    没有才自己解压 zip（兼容旧版主程序或手工调用）。
            string workDir;
            bool ownWorkDir = false;
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
                ownWorkDir = true;
                Log($"自行解压更新包：{opts.ZipPath} → {tempDir}");
            }
            else
            {
                Log("既没有 staging 目录也没有可用的更新包");
                ShowError("没有可用的更新内容。\n\n请重新下载更新包。");
                return 1;
            }

            try
            {
                // 3. 复制文件（覆盖）
                // ⚠ 必须跳过"更新程序自己那一组文件"（StudyJourney.Updater.exe/.dll/...）。
                //    发布包根目录也含这些文件，而本进程正是从目标目录启动的 —— 运行时已把
                //    更新的 exe 与 dll 锁住，覆盖会抛共享冲突，导致**整个更新失败**。
                //    （新版更新程序由主程序在拉起本进程"之前"就整组替换好了，这里无需再换。）
                CopyDirectory(workDir, opts.TargetDir, "StudyJourney.Updater.");

                // 4. 清理临时内容
                if (ownWorkDir)
                {
                    try { Directory.Delete(workDir, true); } catch { }
                }
                else
                {
                    try { Directory.Delete(workDir, true); } catch { }   // staging 也是临时的
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
                ShowWarn($"启动新版本失败：{ex.Message}\n\n请手动打开：\n{opts.ExePath}");
            }

            // 6. 自清理
            Log("完成，准备自清理");
            ScheduleSelfDelete();
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
    /// 把 source 下的文件覆盖到 dest。
    /// <paramref name="skipFileNamePrefix"/> 用来跳过正在运行、被系统锁定的更新器自身文件。
    /// </summary>
    private static void CopyDirectory(string source, string dest, string? skipFileNamePrefix = null)
    {
        Directory.CreateDirectory(dest);
        int skipped = 0, copied = 0, protectedSkipped = 0;

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(file);

            if (!string.IsNullOrEmpty(skipFileNamePrefix) &&
                fileName.StartsWith(skipFileNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (IsProtectedUserData(fileName))
            {
                protectedSkipped++;
                Log($"跳过用户数据文件（不覆盖）：{Path.GetRelativePath(source, file)}");
                continue;
            }

            string rel = Path.GetRelativePath(source, file);
            string destFile = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, true);
            copied++;
        }

        Log($"复制完成：{copied} 个；跳过更新器自身 {skipped} 个；跳过用户数据 {protectedSkipped} 个");
    }

    /// <summary>
    /// 本进程退出后删除自身的 exe。
    ///
    /// ⚠ 原实现用 `:retry / del / if exist goto retry` **无延时忙等** —— 正在运行的 exe
    /// 删不掉，于是 bat 会以满 CPU 空转直到本进程真正退出。这里改成"带延时 + 次数上限"。
    /// </summary>
    private static void ScheduleSelfDelete()
    {
        try
        {
            string self = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(self)) return;

            string bat = Path.Combine(Path.GetTempPath(), $"sj_updater_cleanup_{DateTime.Now.Ticks}.bat");

            // ping 当延时用（timeout 命令需要控制台，隐藏窗口下会报错）
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "setlocal\r\n" +
                $"set \"SELF={self}\"\r\n" +
                "set /a n=0\r\n" +
                ":retry\r\n" +
                "del /F /Q \"%SELF%\" >nul 2>&1\r\n" +
                "if not exist \"%SELF%\" goto done\r\n" +
                "set /a n+=1\r\n" +
                "if %n% GEQ 60 goto done\r\n" +
                "ping -n 1 -w 500 127.0.0.1 >nul\r\n" +
                "goto retry\r\n" +
                ":done\r\n" +
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
