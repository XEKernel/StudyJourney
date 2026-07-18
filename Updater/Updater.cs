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
            var opts = ParseArgs(args);
            if (opts == null)
            {
                ShowError("更新程序参数错误。");
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

            // 2. 解压到临时目录
            string tempDir = Path.Combine(Path.GetTempPath(), $"StudyJourneyUpdate_{DateTime.Now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(opts.ZipPath, tempDir, true);

                // 3. 复制文件（覆盖）
                CopyDirectory(tempDir, opts.TargetDir);

                // 清理
                Directory.Delete(tempDir, true);
                try { File.Delete(opts.ZipPath); } catch { }
            }
            catch (Exception ex)
            {
                ShowError($"更新文件替换失败：{ex.Message}");
                return 1;
            }

            // 4. 启动新版本
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = opts.ExePath,
                    UseShellExecute = true,
                    WorkingDirectory = opts.TargetDir
                });
            }
            catch (Exception ex)
            {
                ShowWarn($"启动新版本失败：{ex.Message}\n\n请手动打开：\n{opts.ExePath}");
            }

            // 5. 自清理
            ScheduleSelfDelete();
        }
        catch (Exception ex)
        {
            ShowError($"更新失败：{ex.Message}");
            return 1;
        }

        return 0;
    }

    private static UpdaterOptions? ParseArgs(string[] args)
    {
        int pid = 0;
        string? zip = null, target = null, exe = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid"    when i + 1 < args.Length: pid    = int.Parse(args[++i]); break;
                case "--zip"    when i + 1 < args.Length: zip    = args[++i]; break;
                case "--target" when i + 1 < args.Length: target = args[++i]; break;
                case "--exe"    when i + 1 < args.Length: exe    = args[++i]; break;
            }
        }

        if (string.IsNullOrEmpty(zip) || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(exe))
            return null;
        if (!File.Exists(zip)) return null;

        return new UpdaterOptions { Pid = pid, ZipPath = zip, TargetDir = target, ExePath = exe };
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(source, file);
            string destFile = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, true);
        }
    }

    private static void ScheduleSelfDelete()
    {
        try
        {
            string self = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(self)) return;

            string bat = Path.Combine(Path.GetTempPath(), $"sj_updater_cleanup_{DateTime.Now.Ticks}.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                ":retry\r\n" +
                $"del /F /Q \"{self}\" 2>nul\r\n" +
                $"if exist \"{self}\" goto retry\r\n" +
                $"del /F /Q \"{bat}\" 2>nul\r\n");

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
        public string ZipPath { get; set; } = "";
        public string TargetDir { get; set; } = "";
        public string ExePath { get; set; } = "";
    }
}
