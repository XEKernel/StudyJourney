using System;
using System.IO;
using System.Linq;
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
        public bool IsSelfContained { get; set; }
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
        private static readonly Lazy<string> _currentVersion = new(() =>
        {
            try
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.6.0";
            }
            catch { return "1.6.0"; }
        });

        public static string CurrentVersion => _currentVersion.Value;

        /// <summary>检测当前应用是否自包含（coreclr.dll 是否在应用目录中）</summary>
        public static bool IsSelfContained
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coreclr.dll"));
                }
                catch { return false; }
            }
        }

        /// <summary>检查 GitHub Release 最新版本，自动匹配自包含/框架依赖的下载链接</summary>
        public static async Task<UpdateInfo> CheckAsync(string owner, string repo)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string body    = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            string latestVer = Regex.Replace(tagName, @"^v", "", RegexOptions.IgnoreCase);
            string curVer = CurrentVersion;
            bool hasUpdate = CompareVersions(latestVer, curVer) > 0;
            bool isSC = IsSelfContained;

            // 从 assets 中找匹配的 zip：自包含找不带 -fd 的，框架依赖找带 -fd 的
            string downloadUrl = $"https://github.com/{owner}/{repo}/releases/latest";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    string? assetUrl  = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(assetUrl)) continue;

                    bool isFDAsset = assetName.Contains("-fd", StringComparison.OrdinalIgnoreCase);
                    if (isSC && !isFDAsset && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        downloadUrl = assetUrl;
                    else if (!isSC && isFDAsset)
                        downloadUrl = assetUrl;
                }
            }

            return new UpdateInfo
            {
                HasUpdate     = hasUpdate,
                LatestVersion = latestVer,
                DownloadUrl   = downloadUrl,
                ReleaseNotes  = body.Length > 500 ? body[..500] + "\u2026" : body,
                IsSelfContained = isSC
            };
        }

        /// <summary>后台下载更新 zip 到临时目录，返回文件路径（null=失败）</summary>
        public static async Task<string?> DownloadAsync(string downloadUrl, IProgress<int>? progress = null)
        {
            try
            {
                var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                string tmpDir = Path.Combine(Path.GetTempPath(), "StudyJourneyUpdate");
                Directory.CreateDirectory(tmpDir);
                string outPath = Path.Combine(tmpDir, "StudyJourney-update.zip");

                using var stream = await response.Content.ReadAsStreamAsync();
                using var file = File.Create(outPath);
                await stream.CopyToAsync(file);

                return outPath;
            }
            catch { return null; }
        }

        /// <summary>简易版本比较（支持 1.6 > 1.5 > 1.10）</summary>
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
