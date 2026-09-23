using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StudyJourney.Avalonia.Helpers;

namespace StudyJourney.Avalonia.Services;

/// <summary>GitHub Release 更新检查结果</summary>
public class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public bool IsSelfContained { get; set; }
}

/// <summary>更新流程的阶段（用于给老师显示"正在下载 / 正在解压 / 重启"）</summary>
public enum UpdatePhase
{
    Downloading,
    Extracting,
    Restarting,
}

/// <summary>下载进度（Total &lt; 0 表示服务端没给 Content-Length，只能显示已下载量）</summary>
public sealed class UpdateProgress
{
    public UpdateProgress(long received, long total, double bytesPerSecond = 0)
    {
        Received = received; Total = total; BytesPerSecond = bytesPerSecond;
    }
    public long Received { get; }
    public long Total { get; }

    /// <summary>下载速度（字节/秒）。带指数平滑，避免数字乱跳；未知为 0。</summary>
    public double BytesPerSecond { get; }

    /// <summary>0..1；总量未知时返回 -1</summary>
    public double Fraction => Total > 0 ? Math.Min(1.0, (double)Received / Total) : -1;

    /// <summary>2026-09-23：给老师看的速度串，如 "3.2 MB/s"；未知返回空串</summary>
    public string SpeedText => BytesPerSecond > 0 ? FormatSpeed(BytesPerSecond) : "";

    /// <summary>2026-09-23：已下载/总量，如 "12.4 / 40.9 MB"；总量未知时只显示已下载</summary>
    public string SizeText => Total > 0
        ? $"{FormatSize(Received)} / {FormatSize(Total)}"
        : FormatSize(Received);

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / 1024 / 1024:0.0} MB/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0} KB/s";
        return $"{bytesPerSec:0} B/s";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.00} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }

    public string Describe()
    {
        // 2026-09-23：带上速度（手动更新的进度窗直接显示这个串）
        string baseText = SizeText.Length > 0
            ? (Total > 0 ? SizeText : $"已下载 {SizeText}")
            : "";
        return SpeedText.Length > 0 ? $"{baseText}  ·  {SpeedText}" : baseText;
    }
}

/// <summary>
/// 自动更新（GitHub Releases）。
///
/// 2026-09-15 修复的问题（本机实测）：
///   ① **下载不走镜像基本不可能成功**：`github.com` 的 release 资产直连 20 秒 0 字节；
///      而 `api.github.com` 直连反而可用 → 检查更新可直连，**下载必须走加速镜像**。
///   ② **全局 10 秒超时会让大文件下载必然失败**：`HttpClient.Timeout` 覆盖"整个响应体读取"，
///      38~84MB 的包不可能 10 秒下完。→ 下载改用独立客户端（无全局超时，逐次调用用 CTS 控时）。
///   ③ **镜像可能返回 200 + HTML 错误页** → 校验 Content-Type 与 zip 魔数，避免把网页当安装包。
///   ④ **更新程序不在位时白下几十 MB** → 下载前先检查。
/// </summary>
public static class UpdateService
{
    /// <summary>加速镜像前缀（形如 https://gh-proxy.com/）；空 = 直连 GitHub。由 App 启动/设置保存时灌入</summary>
    public static string ProxyPrefix { get; set; } = DefaultProxyPrefix;

    /// <summary>实测可用的加速镜像（同时支持 release 资产与 api.github.com 透传）</summary>
    public const string DefaultProxyPrefix = "https://gh-proxy.com/";

    /// <summary>
    /// 内置镜像列表（2026-09-15 本机实测均可用于 release 资产 + API 透传）。
    /// 顺序即尝试顺序：用户配置的前缀优先，然后依次兜底，最后直连。
    /// 这类公益镜像会不定期失效，所以留多个而不是只认一个。
    /// </summary>
    private static readonly string[] BuiltInMirrors =
    {
        "https://gh-proxy.com/",   // 实测最快（资产约 1.9 MB/s）
        "https://gh-proxy.org/",   // 同源备用域名
    };

    /// <summary>检查更新用的客户端（短超时：只是拉一段 JSON）</summary>
    private static readonly HttpClient _api = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders =
        {
            { "User-Agent", "StudyJourney-UpdateCheck" },
            { "Accept", "application/vnd.github+json" },
        }
    };

    /// <summary>下载用的客户端。⚠ 不能设全局 Timeout —— 它覆盖整个响应体读取，
    /// 大文件会被中途掐断；超时改为每次调用传 CancellationToken。</summary>
    private static readonly HttpClient _download = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "StudyJourney-UpdateCheck" } }
    };

    /// <summary>获取当前应用版本号（优先 InformationalVersion，可含 -beta 后缀）</summary>
    private static readonly Lazy<string> _currentVersion = new(() =>
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            // InformationalVersion = "2.3.0-beta" 或 "2.3.0-beta+hash"，取 + 之前
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var idx = info.IndexOf('+');
                if (idx >= 0) info = info[..idx];
                if (!string.IsNullOrWhiteSpace(info)) return info.Trim();
            }
            var ver = asm.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "2.16.0";
        }
        catch { return "2.16.0"; }
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

    /// <summary>更新程序是否在位（不在位就别让用户白等下载）</summary>
    public static bool UpdaterPresent
    {
        get
        {
            try { return File.Exists(UpdaterPath); }
            catch { return false; }
        }
    }

    /// <summary>更新程序文件名（程序目录里一份、更新包里也有一份，见 PrepareUpdateAsync / LaunchUpdater 的说明）</summary>
    private const string UpdaterFileName = "StudyJourney.Updater.exe";

    private static string UpdaterPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdaterFileName);

    /// <summary>
    /// 决定这次用哪个更新程序（2026-09-19 新增，internal 供自检断言）：
    /// **优先用更新包里那一份**（在 staging 里，从 %TEMP% 运行不会锁住程序目录的 DLL），
    /// 包里没有才回退到程序目录里那份（很旧的包/手工调用）。两个都没有 → null（无法更新）。
    /// </summary>
    internal static string? ResolveUpdaterExe(string? stagingDir)
    {
        try
        {
            if (!string.IsNullOrEmpty(stagingDir))
            {
                var staged = Path.Combine(stagingDir, UpdaterFileName);
                if (File.Exists(staged)) return staged;
            }
        }
        catch { /* 目录不可访问 → 走回退 */ }

        try
        {
            if (File.Exists(UpdaterPath)) return UpdaterPath;
        }
        catch { }
        return null;
    }

    // ── 镜像 / 直连候选 ──────────────────────────────────────

    /// <summary>把原始 GitHub 链接展开成候选列表（用户配置镜像 → 内置镜像 → 直连兜底）。
    /// 只对 github 链接加前缀，避免把自定义/已代理的链接拼坏掉。
    /// 拼接格式就是最简单的"前缀 + 原始URL"，如
    /// https://gh-proxy.com/ + https://github.com/o/r/releases/download/v1/x.zip</summary>
    public static List<string> BuildCandidates(string url)
    {
        var list = new List<string>();
        bool isGithub = url.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                     || url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        if (isGithub)
        {
            void Add(string prefix)
            {
                var p = (prefix ?? "").Trim();
                if (p.Length == 0) return;
                if (!p.EndsWith('/')) p += "/";
                // 已经带了此前缀就别重复加
                if (url.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return;
                var full = p + url;
                if (!list.Contains(full)) list.Add(full);
            }

            Add(ProxyPrefix);                       // 用户配置的（默认 https://gh-proxy.com/）
            foreach (var m in BuiltInMirrors) Add(m);   // 内置镜像兜底
        }

        list.Add(url);   // 直连兜底
        return list;
    }

    /// <summary>日志里显示用：把长 URL 截断，并标出走的是镜像还是直连</summary>
    private static string Describe(string url)
    {
        bool proxied = !url.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://api.github.com", StringComparison.OrdinalIgnoreCase)
                    && url.Contains("github.com", StringComparison.OrdinalIgnoreCase);
        var u = url.Length > 90 ? url[..90] + "…" : url;
        return proxied ? $"镜像 {u}" : $"直连 {u}";
    }

    // ── 检查更新 ─────────────────────────────────────────────

    /// <summary>检查 GitHub Release 最新版本，自动匹配自包含/框架依赖的下载链接</summary>
    public static async Task<UpdateInfo> CheckAsync(string owner, string repo, CancellationToken ct = default)
    {
        string direct = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        foreach (var url in BuildCandidates(direct))
        {
            try
            {
                var json = await _api.GetStringAsync(url, ct);
                return ParseRelease(json);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[UpdateService] 检查更新失败（{Describe(url)}）: {ex.Message}");
            }
        }

        // 全部候选都失败：静默视为无更新，不打扰用户
        return new UpdateInfo { HasUpdate = false };
    }

    /// <summary>解析 Release JSON（internal 供自检用合成数据验证资产匹配）</summary>
    internal static UpdateInfo ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        string latestVer = Regex.Replace(tagName, @"^v", "", RegexOptions.IgnoreCase);
        bool hasUpdate = CompareVersions(latestVer, CurrentVersion) > 0;
        bool isSC = IsSelfContained;

        // 从 assets 找匹配的 zip：自包含找不带 -fd 的，框架依赖找带 -fd 的。
        // 用 EndsWith("-fd.zip") 精确判定 —— 原来用 Contains("-fd") 会把名字里
        // 恰好含 "-fd" 的其它文件（如 -fdx）也算进来。
        string downloadUrl = "";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? assetUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(assetUrl)) continue;

                bool endsZip = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                bool isFD = assetName.EndsWith("-fd.zip", StringComparison.OrdinalIgnoreCase);
                if (!endsZip) continue;

                if (isSC && !isFD) { downloadUrl = assetUrl; break; }
                if (!isSC && isFD) { downloadUrl = assetUrl; break; }
            }
        }

        // 没有匹配的下载包时视为不可更新，避免兜底成 HTML 页面导致下载后解压失败
        if (downloadUrl.Length == 0) hasUpdate = false;

        return new UpdateInfo
        {
            HasUpdate = hasUpdate,
            LatestVersion = latestVer,
            DownloadUrl = downloadUrl,
            ReleaseNotes = body.Length > 500 ? body[..500] + "\u2026" : body,
            IsSelfContained = isSC,
        };
    }

    // ── 下载 ─────────────────────────────────────────────────

    /// <summary>
    /// 下载更新包到临时目录，返回 zip 完整路径。
    /// 镜像优先、直连兜底；<paramref name="progress"/> 实时报告进度（界面别假装卡死）。
    /// </summary>
    public static async Task<string> DownloadUpdateAsync(string downloadUrl,
        IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException("这个 Release 没有对应架构的压缩包，无法自动更新。");

        Exception? last = null;
        foreach (var url in BuildCandidates(downloadUrl))
        {
            try
            {
                return await DownloadOneAsync(url, progress, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // 用户取消：不继续换候选
            }
            catch (Exception ex)
            {
                last = ex;
                AppLogger.Warn($"[Updater] 下载失败（{Describe(url)}）: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"下载失败：{last?.Message ?? "所有下载通道都不可用"}\n可稍后重试，或到 GitHub Releases 手动下载。", last);
    }

    private static async Task<string> DownloadOneAsync(string url,
        IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "StudyJourneyUpdate");
        Directory.CreateDirectory(tmpDir);
        string zipPath = Path.Combine(tmpDir, "update.zip");

        using var response = await _download.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // 镜像失效时常见"200 + HTML 错误页"：先看 Content-Type，别把网页当安装包解压
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("下载到的是网页而不是安装包（加速镜像可能已失效）");

        long total = response.Content.Headers.ContentLength ?? -1;

        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(zipPath))
        {
            var buffer = new byte[81920];
            long received = 0;
            int n;

            // 速度采样（2026-09-23 新增）：每 500ms 取一次瞬时速度并做指数平滑 ——
            // 直接用"总字节/总耗时"在开头会剧烈抖动，用瞬时值又跳得厉害；
            // 进度本身仍每次读取都上报（保证环形进度流畅）。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastSampleBytes = 0, lastSampleMs = 0;
            double speed = 0;

            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                received += n;

                long ms = sw.ElapsedMilliseconds;
                if (ms - lastSampleMs >= 500)
                {
                    double dt = (ms - lastSampleMs) / 1000.0;
                    if (dt > 0)
                    {
                        double instant = (received - lastSampleBytes) / dt;
                        speed = speed <= 0 ? instant : speed * 0.6 + instant * 0.4;
                    }
                    lastSampleBytes = received;
                    lastSampleMs = ms;
                }

                progress?.Report(new UpdateProgress(received, total, speed));
            }
            if (received == 0) throw new InvalidOperationException("下载内容为空");
        }

        // 双保险：校验 zip 魔数（PK\x03\x04 或空包 PK\x05\x06）
        if (!LooksLikeZip(zipPath))
            throw new InvalidOperationException("下载的文件不是有效的 zip 包");

        return zipPath;
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            if (fs.Read(head) < 4) return false;
            return head[0] == 0x50 && head[1] == 0x4B &&
                   (head[2] == 0x03 || head[2] == 0x05 || head[2] == 0x07);
        }
        catch { return false; }
    }

    // ── 拉起更新程序 ─────────────────────────────────────────

    /// <summary>
    /// 下载 → 解压到 staging → 拉起更新程序（由它等主进程退出 → 替换文件 → 重启）。
    /// 返回 false 表示未启动成功（原因已记日志）；true 表示调用方应当退出进程。
    /// </summary>
    /// <param name="onPhase">
    /// 阶段回调，供 UI 显示「正在下载新版本 / 正在解压 / 重启」。
    /// ⚠ 只在 UI 线程之外调用一次，回调实现里若要碰控件请自行 Dispatcher 封送。
    /// </param>
    /// <summary>
    /// 下载 → 解压到 staging → 刷新更新程序。**不启动更新程序**（见 <see cref="LaunchUpdater"/>）。
    ///
    /// 拆成"准备 / 启动"两步是为了修一个严重缺陷（2026-09-22 代码审查发现）：
    /// 原来下载解压完就立刻拉起更新程序，而更新程序只等主进程 **15 秒**就 `Kill()` ——
    /// 于是主程序里那段"最多等 5 分钟、等老师关掉白板再重启"的保护**完全是假的**，
    /// 15 秒后照样被强杀，白板上未导出的板书就丢了（正是那段保护想避免的事）。
    ///
    /// 返回 null 表示准备失败（原因已记日志），调用方**不应**退出进程。
    /// </summary>
    /// <param name="onPhase">
    /// 阶段回调，供 UI 显示「正在下载新版本 / 正在解压」。
    /// ⚠ 回调里若要碰控件请自行 Dispatcher 封送。
    /// </param>
    /// <summary>
    /// 已下载并解压好、只等"程序退出"就位的更新。
    ///
    /// **拆出这一步是为了修一个严重缺陷**（2026-09-22 代码审查发现）：
    /// 原来「下载+解压+拉起更新程序」是一件事，而更新程序只等主进程 **15 秒**就 `Kill()`。
    /// 于是主程序里那段"最多等 5 分钟，等老师关掉白板再重启"的保护**完全是假的** ——
    /// 15 秒后照样被强杀，白板上未导出的板书就丢了（正是那段保护想避免的事）。
    /// 现在：先把准备工作做完（**不启动更新程序**），确认可以安全退出后，才由
    /// <see cref="LaunchUpdater"/> 启动它。
    /// </summary>
    public sealed record PreparedUpdate(
        string StagingDir, string ZipPath, string UpdaterExe, string TargetDir, string ExePath);

    /// <summary>
    /// 下载 → 解压到 staging → 刷新更新程序。**不启动更新程序**（见 <see cref="LaunchUpdater"/>）。
    /// 返回 null 表示准备失败（原因已记日志），调用方不应退出进程。
    /// </summary>
    public static async Task<PreparedUpdate?> PrepareUpdateAsync(string downloadUrl,
        string? zipPath = null, IProgress<UpdateProgress>? progress = null,
        Action<UpdatePhase>? onPhase = null, CancellationToken ct = default)
    {
        onPhase?.Invoke(UpdatePhase.Downloading);
        try
        {
            zipPath ??= await DownloadUpdateAsync(downloadUrl, progress, ct);

            // 解压放到主程序做（原来在更新程序里做）。三个好处：
            //   ①「正在解压」这个状态是真的，可以显示给老师看；
            //   ② 包损坏/解压失败能在**退出程序之前**发现并中止，不会出现"程序退了但没装上"；
            //   ③ 更新程序只需拷文件，它自己也更简单、更不容易出错。
            onPhase?.Invoke(UpdatePhase.Extracting);
            // 放后台线程解压：包里有几百个文件，同步做会卡住 UI 线程 ——
            // 那样"正在解压"这四个字根本来不及画出来，界面看着就是死的。
            string stagingDir = await Task.Run(() => ExtractToStaging(zipPath), ct);

            // ⚠ 关键（2026-09-19 修）：更新程序要**从 staging 目录里那一份启动**，
            //    不能再从程序目录启动。
            //
            //    原因：发布包根目录自带 StudyJourney.Updater.*（CI 会把更新程序发布产物
            //    拷进产物根），而它是**自包含**应用 —— 从程序目录运行时，Windows 会把它的
            //    .NET 运行时 DLL（coreclr.dll / System.Private.CoreLib.dll / hostpolicy.dll …）
            //    也映射成"从程序目录加载"。这些文件随即变成"正被另一进程使用"，
            //    更新程序随后要把新包覆盖上去时必然抛共享冲突 → **整个更新失败**。
            //    用户看到的就是：主程序已经退出了，更新程序却报"某个 DLL 正由另一进程使用"。
            //    从 %TEMP% 的 staging 里启动后，它锁的全是临时目录里的文件，程序目录一个都不锁。
            string? updaterExe = ResolveUpdaterExe(stagingDir);
            if (updaterExe == null)
            {
                AppLogger.Warn($"[Updater] 找不到可用的更新程序（更新包里没有，{UpdaterPath} 也没有）");
                return null;
            }

            bool fromStaging = !string.Equals(updaterExe, UpdaterPath, StringComparison.OrdinalIgnoreCase);
            if (fromStaging)
                AppLogger.Info("[Updater] 用更新包自带的更新程序（从临时目录运行，不占用程序目录里的 DLL）");
            else
                AppLogger.Warn("[Updater] 更新包里没有更新程序，回退用程序目录里那份（只有很旧的包才会这样）");

            // 顺手把**程序目录里那份**也换成新版：这样下次更新、以及上面那条回退路径
            // 用到的都是新版本（此刻它还没运行，覆盖是安全的）。
            TryRefreshUpdater(stagingDir, zipPath);

            // ⚠ 路径一律去掉尾部分隔符再拼参数。
            // AppDomain.BaseDirectory **必然以反斜杠结尾**，直接拼进带引号的参数会变成
            //   --target "E:\app\"
            // 其中 `\"` 把结束引号**转义**掉 → 整段参数错位 → 更新程序拿不到 --exe
            // → 报「更新程序参数错误」。这就是 2026-09-17 用户遇到的故障。
            string targetDir = TrimTrailingSeparator(AppDomain.CurrentDomain.BaseDirectory);
            string exePath = Path.Combine(targetDir, "StudyJourneyAvalonia.exe");

            AppLogger.Info($"[Updater] 更新已就绪（staged: {stagingDir}），等待安全退出时机");
            return new PreparedUpdate(stagingDir, zipPath, updaterExe, targetDir, exePath);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Info("[Updater] 用户取消了更新");
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Updater] 更新准备失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 拉起更新程序。**调用方必须随即退出进程** —— 更新程序最多等 15 秒就会强杀主进程
    /// （见 <see cref="PreparedUpdate"/> 的说明）。返回 false 表示没拉起来，此时**不应**退出，
    /// 否则程序就没了。
    /// </summary>
    public static bool LaunchUpdater(PreparedUpdate p, int currentPid,
        Action<UpdatePhase>? onPhase = null)
    {
        try
        {
            // 用 ArgumentList 而不是手拼 Arguments：.NET 会按 Windows 规则正确转义，
            // 从根本上避免"路径带空格 / 以反斜杠结尾"这类引号事故。
            // ArgumentList 要求 UseShellExecute = false（更新程序是普通 exe，不需要 shell）。
            var psi = new ProcessStartInfo
            {
                FileName = p.UpdaterExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = p.TargetDir,
            };
            psi.ArgumentList.Add("--pid");    psi.ArgumentList.Add(currentPid.ToString());
            psi.ArgumentList.Add("--staged"); psi.ArgumentList.Add(p.StagingDir);
            psi.ArgumentList.Add("--zip");    psi.ArgumentList.Add(p.ZipPath);   // 兼容旧版更新程序
            psi.ArgumentList.Add("--target"); psi.ArgumentList.Add(p.TargetDir);
            psi.ArgumentList.Add("--exe");    psi.ArgumentList.Add(p.ExePath);

            onPhase?.Invoke(UpdatePhase.Restarting);
            Process.Start(psi);
            AppLogger.Info("[Updater] 已拉起更新程序，主程序即将退出");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Updater] 拉起更新程序失败", ex);
            return false;
        }
    }

    /// <summary>去掉路径尾部的目录分隔符（拼 Windows 命令行参数前必须做，否则 \" 会转义掉结束引号）</summary>
    private static string TrimTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var t = path.TrimEnd('\\', '/');
        return t.Length == 0 ? path : t;      // 别把 "C:\" 削成 "C:"
    }

    /// <summary>把更新包解压到临时 staging 目录，返回该目录</summary>
    private static string ExtractToStaging(string zipPath)
    {
        string staging = Path.Combine(Path.GetTempPath(), "StudyJourneyUpdate", "staged");
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
            AppLogger.Info($"[Updater] 已解压到 staging：{staging}");
            return staging;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"解压更新包失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 用新版更新程序覆盖磁盘上的旧版（趁它还没运行）。
    /// 必须是**整组**替换（exe + dll + deps.json + runtimeconfig.json）——
    /// 只换 exe 会造成新版 exe 配旧版 dll 的错配。
    /// 优先从已解压的 staging 目录取（简单可靠）；没有才回退去读 zip。
    /// 失败不影响主流程：现有更新程序仍能完成其余文件的替换，只是自身版本留在旧版。
    /// </summary>
    private static void TryRefreshUpdater(string stagingDir, string zipPath)
    {
        const string namePrefix = "StudyJourney.Updater.";
        try
        {
            // 只取**根目录下**的更新器文件（子目录里的同名文件不是它）
            var files = Directory.Exists(stagingDir)
                ? Directory.GetFiles(stagingDir, namePrefix + "*", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                AppLogger.Warn($"[Updater] staging 里没有 {namePrefix}* 文件，沿用现有更新程序");
                return;
            }

            int n = 0;
            foreach (var f in files)
            {
                var dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(f));
                File.Copy(f, dest, overwrite: true);
                n++;
            }

            AppLogger.Info($"[Updater] 已用新版本替换更新程序（{n} 个文件）");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Updater] 刷新更新程序失败（沿用现有版本）: {ex.Message}");
        }
    }

    /// <summary>版本比较：支持 v1.7.0 / 1.7.0-beta / 1.10 等格式</summary>
    private static int CompareVersions(string a, string b)
    {
        // 去掉 v 前缀；预发布后缀（如 -beta、-rc1）低于正式版
        var clean = (string s) =>
        {
            s = Regex.Replace(s, @"^v", "", RegexOptions.IgnoreCase);
            var idx = s.IndexOfAny(new[] { '-', '+' });
            return idx >= 0 ? s[..idx] : s;
        };
        string pa = clean(a), pb = clean(b);

        var numsA = pa.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var numsB = pb.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int len = Math.Max(numsA.Length, numsB.Length);
        for (int i = 0; i < len; i++)
        {
            int na = i < numsA.Length && int.TryParse(numsA[i], out int x) ? x : 0;
            int nb = i < numsB.Length && int.TryParse(numsB[i], out int y) ? y : 0;
            if (na != nb) return na.CompareTo(nb);
        }
        // 版本号相同 → 无预发布后缀者更新（正式版 > 预发布版）
        bool hasPreA = Regex.IsMatch(a, @"[-+](alpha|beta|rc|pre)", RegexOptions.IgnoreCase);
        bool hasPreB = Regex.IsMatch(b, @"[-+](alpha|beta|rc|pre)", RegexOptions.IgnoreCase);
        if (hasPreA != hasPreB) return hasPreA ? -1 : 1;
        return 0;
    }
}
