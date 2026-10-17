using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// HTTP 远程控制台自检（<c>SJ_SELFTEST=http</c>）—— **真实起 Kestrel、真实发 HTTP 请求**。
///
/// <para><b>为什么需要它</b>：`api` 模式只断言 DTO 的序列化键名，**17 个端点一个都没被请求过**。
/// 本模式走完整 HTTP 栈（Kestrel → 中间件 → 端点 → 响应），为 P0-C 拆分实现提供行为护栏。</para>
///
/// <para><b>断言分类（P0-B 约定）</b>：
/// <list type="bullet">
///   <item><b>A 类（现状）</b>：锁住当前实现的行为，防止重构时无意改变。失败即模式 FAIL。</item>
///   <item><b>B 类（疑似缺陷）</b>：当前行为是 X、但按代码意图/安全常识怀疑应为 Y。
///   记为 expected-fail，<b>不阻塞本模式 PASS</b>，单独汇总；P0-C 修复后升为 A 类。</item>
/// </list></para>
///
/// <para><b>隔离原则</b>：`tokens.json` / `WebRoot` / `whitelist.txt` 的路径在
/// <see cref="Services.HttpServerService"/> 里是硬编码的（本阶段不改实现），
/// 因此统一采用「备份字节 + mtime → 跑完原样还原」；上传目录临时指向 %TEMP% 并还原设置。</para>
/// </summary>
public static class HttpSelfTest
{
    private const string P = "[HTTP]";

    private static int _aPass;
    private static int _bExpectedFail;
    private static int _bAlreadyOk;

    // ── A/B 断言 ─────────────────────────────────────────────

    /// <summary>A 类：锁住现状。不成立即抛异常（模式 FAIL）。</summary>
    private static void CheckA(StringBuilder sb, bool ok, string what)
    {
        sb.AppendLine($"{P}   [A] {(ok ? "ok" : "✗ ")} {what}");
        if (!ok) throw new Exception("A 类断言失败：" + what);
        _aPass++;
    }

    /// <summary>B 类：断言"期望的行为"。不成立记 expected-fail（不抛异常），成立则说明可升级为 A。</summary>
    private static void CheckB(StringBuilder sb, bool desiredOk, string what)
    {
        if (desiredOk)
        {
            sb.AppendLine($"{P}   [B] ok  {what}（当前已符合期望，待 P0-C 确认后可升级为 A）");
            _bAlreadyOk++;
        }
        else
        {
            sb.AppendLine($"{P}   [B] expected-fail  {what}（现状不符，已登记；P0-C 修复后应转 A）");
            _bExpectedFail++;
        }
    }

    // ── 基础设施 ─────────────────────────────────────────────

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>文件快照（字节 + 原始写入时间），用于「跑完原样还原」。</summary>
    private sealed class FileSnapshot
    {
        private readonly string _path;
        private readonly byte[]? _bytes;
        private readonly DateTime _mtimeUtc;

        public FileSnapshot(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(path))
                {
                    _bytes = File.ReadAllBytes(path);
                    _mtimeUtc = File.GetLastWriteTimeUtc(path);
                }
            }
            catch { }
        }

        public void Restore()
        {
            try
            {
                if (_bytes != null)
                {
                    File.WriteAllBytes(_path, _bytes);
                    File.SetLastWriteTimeUtc(_path, _mtimeUtc);
                }
                else if (File.Exists(_path))
                {
                    File.Delete(_path);      // 原本不存在 → 删掉测试生成的那份
                }
            }
            catch { }
        }
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient http, HttpMethod method, string path, string? token = null, HttpContent? content = null)
    {
        using var req = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrEmpty(token))
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var resp = await http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, string Body, string? Token)> LoginAsync(
        HttpClient http, string user, string pwd, bool remember)
    {
        var payload = "{\"username\":\"" + user + "\",\"password\":\"" + pwd +
                      "\",\"rememberMe\":" + (remember ? "true" : "false") + "}";
        var (st, body) = await SendAsync(http, HttpMethod.Post, "/api/login", null, Json(payload));
        string? token = null;
        if (st == HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("token", out var t)) token = t.GetString();
        }
        return (st, body, token);
    }

    // ── 主流程 ───────────────────────────────────────────────

    public static async Task<string> RunAsync()
    {
        var sb = new StringBuilder();
        _aPass = _bExpectedFail = _bAlreadyOk = 0;

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var tokensSnap = new FileSnapshot(Path.Combine(exeDir, "tokens.json"));
        var schedSnap = new FileSnapshot(Path.Combine(exeDir, "schedule.json"));
        var settingsSnap = new FileSnapshot(Path.Combine(exeDir, "settings.json"));

        string tempUpload = Path.Combine(Path.GetTempPath(), "sj-http-" + Guid.NewGuid().ToString("N")[..8]);
        string? savedUploadDir = null;
        List<Models.TeacherAccount>? savedTeachers = null;
        bool savedAccountsInitialized = false;

        int port = 0;
        try
        {
            sb.AppendLine($"{P} HTTP 端到端自检开始");

            // 隔离：上传目录指到临时目录；注入一个确定的测试账号（不依赖老师的真实账号表）
            savedUploadDir = App.Settings.CustomUploadDirectory;
            savedTeachers = App.Settings.Teachers;
            savedAccountsInitialized = App.Settings.AccountsInitialized;
            Directory.CreateDirectory(tempUpload);
            App.Settings.CustomUploadDirectory = tempUpload;

            var testTeacher = new Models.TeacherAccount
            {
                Username = "httptest",
                DisplayName = "HTTP自检",
                Subject = "测试",
            };
            testTeacher.SetPassword("HttpTest@123");
            App.Settings.Teachers = new List<Models.TeacherAccount> { testTeacher };
            App.Settings.AccountsInitialized = true;
            sb.AppendLine($"{P} 隔离就绪：上传目录 → {tempUpload}；测试账号 httptest");

            port = GetFreePort();
            sb.AppendLine($"{P} 选用临时端口 {port}");
            await Services.HttpServerService.StartAsync($"http://127.0.0.1:{port}");
            CheckA(sb, Services.HttpServerService.IsRunning, "Kestrel 已启动（IsRunning=true）");
            CheckA(sb, Services.HttpServerService.Port == port, $"Port 属性 = {port}（实际 {Services.HttpServerService.Port}）");

            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(15),
            };

            // ── 1. 健康检查（公开）──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/health");
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"status\":\"ok\"", StringComparison.Ordinal),
                    $"GET /api/health → {(int)st} {body}");
            }

            // ── 2. 无 token 访问受保护端点 → 401 ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/schedule");
                CheckA(sb, st == HttpStatusCode.Unauthorized,
                    $"GET /api/schedule 无 token → {(int)st}（期望 401）{body}");
            }

            // ── 3. 伪造 token → 401 ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/schedule", "forged-token-1234");
                CheckA(sb, st == HttpStatusCode.Unauthorized,
                    $"GET /api/schedule 伪造 token → {(int)st}（期望 401）{body}");
            }

            // ── 4. 登录失败（错误密码）→ 401 ──
            {
                var (st, body, _) = await LoginAsync(http, "httptest", "wrong-password", false);
                CheckA(sb, st == HttpStatusCode.Unauthorized && body.Contains("invalid credentials", StringComparison.Ordinal),
                    $"POST /api/login 错误密码 → {(int)st} {body}");
            }

            // ── 5. 登录成功 + rememberMe → tokens.json 落盘 ──
            string token;
            {
                var (st, body, tok) = await LoginAsync(http, "httptest", "HttpTest@123", true);
                CheckA(sb, st == HttpStatusCode.OK && !string.IsNullOrEmpty(tok),
                    $"POST /api/login 正确密码 + rememberMe → {(int)st}，token 非空");
                token = tok!;

                string tokensPath = Path.Combine(exeDir, "tokens.json");
                CheckA(sb, File.Exists(tokensPath), "rememberMe → tokens.json 已落盘");
                string json = File.ReadAllText(tokensPath);
                bool hasToken = false;
                using (var doc = JsonDocument.Parse(json))
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Name == token) { hasToken = true; break; }
                CheckA(sb, hasToken, "tokens.json 回读：包含刚签发的 token（键名一致）");
            }

            // ── 6. 有效 token → 受保护端点通过 ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/schedule", token);
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"ok\":true", StringComparison.Ordinal),
                    $"GET /api/schedule 有效 token → {(int)st}（响应含 ok:true）");
            }

            // ── 7. /api/teachers 公开且不暴露 username ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/teachers");
                CheckA(sb, st == HttpStatusCode.OK, $"GET /api/teachers 无 token → {(int)st}（公开端点）");
                CheckA(sb, !body.Contains("httptest", StringComparison.Ordinal),
                    "GET /api/teachers 不返回 username（防枚举）");
                CheckA(sb, body.Contains("HTTP自检", StringComparison.Ordinal) ||
                           body.Contains("\\u0048", StringComparison.Ordinal) || body.Contains("displayName", StringComparison.Ordinal),
                    "GET /api/teachers 返回 displayName 字段");
            }

            // ── 8. logout 后 token 立即失效 ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Post, "/api/logout", token);
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"ok\":true", StringComparison.Ordinal),
                    $"POST /api/logout → {(int)st} {body}");
                var (st2, _) = await SendAsync(http, HttpMethod.Get, "/api/schedule", token);
                CheckA(sb, st2 == HttpStatusCode.Unauthorized,
                    $"登出后再用该 token → {(int)st2}（期望 401）");
            }

            // ── 9. 重新登录（后续用例要 token）──
            string token2;
            {
                var (st, _, tok) = await LoginAsync(http, "httptest", "HttpTest@123", false);
                CheckA(sb, st == HttpStatusCode.OK && !string.IsNullOrEmpty(tok), "重新登录（非 rememberMe）→ 200");
                token2 = tok!;
            }

            // ── 10. GET /api/schedule 结构 ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/schedule", token2);
                CheckA(sb, st == HttpStatusCode.OK, $"GET /api/schedule 有效 token → {(int)st}");
                CheckA(sb, body.Contains("\"ok\":true", StringComparison.Ordinal) &&
                           body.Contains("\"date\"", StringComparison.Ordinal) &&
                           body.Contains("\"weekday\"", StringComparison.Ordinal) &&
                           body.Contains("\"schedule\"", StringComparison.Ordinal),
                    "GET /api/schedule 含 ok/date/weekday/schedule");
            }

            // ── 11. PUT /api/schedule 合法体（PascalCase，与 AppJsonContext 一致）→ 落盘 + Reload ──
            {
                const string payload =
                    "{\"Entries\":[{\"DayOfWeek\":1,\"Period\":1,\"Subject\":\"HTTPTEST\"," +
                    "\"StartTimeStr\":\"08:00\",\"EndTimeStr\":\"08:45\",\"Type\":0}]," +
                    "\"Exams\":[],\"TimeTemplates\":[],\"DayTimeTemplates\":{},\"MakeupDays\":[]}";
                var (st, resp) = await SendAsync(http, HttpMethod.Put, "/api/schedule", token2, Json(payload));
                CheckA(sb, st == HttpStatusCode.OK && resp.Contains("\"success\":true", StringComparison.Ordinal),
                    $"PUT /api/schedule 合法体 → {(int)st} {resp}");

                string disk = File.ReadAllText(Path.Combine(exeDir, "schedule.json"));
                CheckA(sb, disk.Contains("HTTPTEST", StringComparison.Ordinal),
                    "PUT 后 schedule.json 落盘（含 HTTPTEST）");

                var (st2, body2) = await SendAsync(http, HttpMethod.Get, "/api/schedule", token2);
                CheckA(sb, st2 == HttpStatusCode.OK && body2.Contains("HTTPTEST", StringComparison.Ordinal),
                    "PUT 后 GET 能读到新课表（Reload 生效）");
            }

            // ── 12. PUT /api/schedule 非法体 → 4xx 且原文件不被破坏 ──
            {
                string schedPath = Path.Combine(exeDir, "schedule.json");
                string before = Convert.ToBase64String(File.ReadAllBytes(schedPath));
                var (st, resp) = await SendAsync(http, HttpMethod.Put, "/api/schedule", token2, Json("this is not json"));
                CheckA(sb, st == HttpStatusCode.BadRequest,
                    $"PUT /api/schedule 非法 JSON → {(int)st}（期望 400）{resp}");
                string after = Convert.ToBase64String(File.ReadAllBytes(schedPath));
                CheckA(sb, before == after, "非法体被拒后 schedule.json 字节未变");
            }

            // ── 13. GET /api/status ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/status", token2);
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"ok\":true", StringComparison.Ordinal),
                    $"GET /api/status → {(int)st}");
                CheckA(sb, body.Contains("\"uptimeMinutes\"", StringComparison.Ordinal) &&
                           body.Contains("\"disk\"", StringComparison.Ordinal) &&
                           body.Contains("\"recentUploads\"", StringComparison.Ordinal),
                    "GET /api/status 含 uptimeMinutes/disk/recentUploads");
            }

            // ── 14. GET / PUT /api/upload-dir ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/upload-dir", token2);
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"ok\":true", StringComparison.Ordinal),
                    $"GET /api/upload-dir → {(int)st}");
                CheckA(sb, body.Contains("\"current\"", StringComparison.Ordinal) &&
                           body.Contains("\"presets\"", StringComparison.Ordinal),
                    "GET /api/upload-dir 含 current/presets");

                string alt = Path.Combine(tempUpload, "alt");
                Directory.CreateDirectory(alt);
                var (st2, body2) = await SendAsync(http, HttpMethod.Put, "/api/upload-dir", token2,
                    Json("{\"path\":\"" + alt.Replace("\\", "\\\\") + "\"}"));
                CheckA(sb, st2 == HttpStatusCode.OK && body2.Contains("\"success\":true", StringComparison.Ordinal),
                    $"PUT /api/upload-dir → {(int)st2} {body2}");

                var (st3, body3) = await SendAsync(http, HttpMethod.Get, "/api/upload-dir", token2);
                CheckA(sb, st3 == HttpStatusCode.OK && body3.Contains("alt", StringComparison.Ordinal),
                    "PUT /api/upload-dir 立即生效（GET 已反映新目录）");

                // 还原到临时上传根，避免影响后续上传用例
                App.Settings.CustomUploadDirectory = tempUpload;
            }

            // ── 15. GET / PUT /api/config ──
            {
                var (st, body) = await SendAsync(http, HttpMethod.Get, "/api/config", token2);
                CheckA(sb, st == HttpStatusCode.OK && body.Contains("\"className\"", StringComparison.Ordinal),
                    $"GET /api/config → {(int)st}");

                var (st2, body2) = await SendAsync(http, HttpMethod.Put, "/api/config", token2,
                    Json("{\"className\":\"HTTPTEST-CLASS\"}"));
                CheckA(sb, st2 == HttpStatusCode.OK && body2.Contains("HTTPTEST-CLASS", StringComparison.Ordinal),
                    $"PUT /api/config 改班级名 → {(int)st2}");

                var (st3, body3) = await SendAsync(http, HttpMethod.Get, "/api/config", token2);
                CheckA(sb, st3 == HttpStatusCode.OK && body3.Contains("HTTPTEST-CLASS", StringComparison.Ordinal),
                    "PUT /api/config 立即生效（GET 已反映）");
            }

            // ── 16. [B] 控制台回写路径：把 GET 返回的 schedule 原样 PUT 回去 ──
            //     期望：磁盘课表**不变**（网页端"保存课表"不该清空课表）。
            //     风险点：GET 用 ApiJsonContext（camelCase），PUT 读 AppJsonContext（PascalCase），
            //             若大小写不敏感未开启 → 字段全绑不上 → 存成空课表。
            {
                string schedPath = Path.Combine(exeDir, "schedule.json");
                var (getSt, getBody) = await SendAsync(http, HttpMethod.Get, "/api/schedule", token2);
                string camel = "";
                using (var doc = JsonDocument.Parse(getBody))
                    camel = doc.RootElement.GetProperty("schedule").GetRawText();

                string before = Convert.ToBase64String(File.ReadAllBytes(schedPath));
                var (st, _) = await SendAsync(http, HttpMethod.Put, "/api/schedule", token2, Json(camel));
                string after = Convert.ToBase64String(File.ReadAllBytes(schedPath));

                CheckB(sb, getSt == HttpStatusCode.OK && st == HttpStatusCode.OK && before == after,
                    $"控制台回写（camelCase 原样 PUT）不改动课表 —— GET {(int)getSt} / PUT {(int)st} / 磁盘{(before == after ? "未变" : "已变")}");

                // 不论结果如何都还原课表，避免污染后续断言
                schedSnap.Restore();
                try { App.ReloadScheduleFromDisk(); } catch { }
            }

            sb.AppendLine($"{P} 分类统计：A 类 {_aPass} 条通过；B 类 expected-fail {_bExpectedFail} 条、已符合 {_bAlreadyOk} 条");
            sb.AppendLine($"{P} 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{P} 分类统计：A 类 {_aPass} 条通过；B 类 expected-fail {_bExpectedFail} 条、已符合 {_bAlreadyOk} 条");
            sb.AppendLine($"{P} 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }
        finally
        {
            try { Services.HttpServerService.Stop(); } catch { }

            // 还原内存中的设置（磁盘文件另由快照还原）
            try { App.Settings.CustomUploadDirectory = savedUploadDir ?? ""; } catch { }
            try { if (savedTeachers is { } st) App.Settings.Teachers = st; } catch { }
            try { App.Settings.AccountsInitialized = savedAccountsInitialized; } catch { }

            tokensSnap.Restore();
            schedSnap.Restore();
            settingsSnap.Restore();
            try { if (Directory.Exists(tempUpload)) Directory.Delete(tempUpload, true); } catch { }
        }

        return sb.ToString();
    }
}
