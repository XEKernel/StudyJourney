using System;
using System.IO;

namespace StudyJourney.Avalonia.Helpers
{
    /// <summary>
    /// 轻量日志工具：Debug 输出 + 可选文件日志。
    /// 替代散落各处的空 catch（静默吞异常），便于排障。
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _lock = new();
        private static string? _logPath;

        /// <summary>启用文件日志（写日志到 exe 目录 logs/app.log）</summary>
        public static void EnableFileLogging()
        {
            try
            {
                var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                _logPath = System.IO.Path.Combine(dir, "app.log");
            }
            catch { /* 无法创建日志目录时仅 Debug 输出 */ }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            Write("ERROR", message + (ex != null ? $" | {ex.GetType().Name}: {ex.Message}" : ""));
            if (ex != null) System.Diagnostics.Debug.WriteLine(ex.ToString());
        }

        private static void Write(string level, string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            if (_logPath == null) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch { /* 日志写入失败不影响主流程 */ }
        }
    }
}
