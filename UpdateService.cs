using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GaokaoCountdown
{
    /// <summary>GitHub Release 更新检查结果</summary>
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
    }

    /// <summary>自动检查 GitHub Releases 更新</summary>
    public static class UpdateService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "StudyJourney-UpdateCheck" } }
        };

        /// <summary>获取当前应用版本号（从 Assembly 读取）</summary>
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return ver != null ? $"{ver.Major}.{ver.Minor}" : "1.5";
                }
                catch { return "1.5"; }
            }
        }

        /// <summary>检查 GitHub Release 最新版本</summary>
        public static async Task<UpdateInfo> CheckAsync(string owner, string repo)
        {
            try
            {
                string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                string body    = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

                // 去掉 v 前缀：v1.5 → 1.5
                string latestVer = Regex.Replace(tagName, @"^v", "", RegexOptions.IgnoreCase);
                string curVer = CurrentVersion;

                bool hasUpdate = CompareVersions(latestVer, curVer) > 0;

                return new UpdateInfo
                {
                    HasUpdate    = hasUpdate,
                    LatestVersion = latestVer,
                    DownloadUrl  = string.IsNullOrEmpty(htmlUrl)
                        ? $"https://github.com/{owner}/{repo}/releases/latest"
                        : htmlUrl,
                    ReleaseNotes = body.Length > 500 ? body[..500] + "…" : body
                };
            }
            catch
            {
                return new UpdateInfo { HasUpdate = false };
            }
        }

        /// <summary>简易版本比较（支持 1.5 > 1.4 > 1.10）</summary>
        private static int CompareVersions(string a, string b)
        {
            try
            {
                var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
                int len = Math.Max(pa.Length, pb.Length);
                for (int i = 0; i < len; i++)
                {
                    int na = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                    int nb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                    if (na != nb) return na.CompareTo(nb);
                }
                return 0;
            }
            catch { return string.CompareOrdinal(a, b); }
        }
    }
}
