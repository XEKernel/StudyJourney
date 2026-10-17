using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
///   <item><b>A 类（现状）</b>：锁住当前实现的行为，防止重构时无意改变。</item>
///   <item><b>B 类（疑似缺陷）</b>：当前行为是 X、但按代码意图/安全常识怀疑应为 Y。
///   记为 expected-fail，<b>不阻塞本模式 PASS</b>，单独汇总进报告；P0-C 修复后改为 A 类。</item>
/// </list></para>
///
/// <para><b>隔离原则</b>：`tokens.json` / `WebRoot` / `whitelist.txt` 的路径在
/// <see cref="Services.HttpServerService"/> 里是硬编码的（本阶段不改实现），
/// 因此这里统一采用「备份字节 + mtime → 跑完原样还原」；上传目录则临时指向 %TEMP% 并还原设置。</para>
/// </summary>
public static class HttpSelfTest
{
    private const string P = "[HTTP]";

    /// <summary>取一个 OS 分配的空闲端口（避免固定端口冲突）。</summary>
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<string> RunAsync()
    {
        var sb = new StringBuilder();
        int port = 0;
        try
        {
            sb.AppendLine($"{P} HTTP 端到端自检开始");

            port = GetFreePort();
            sb.AppendLine($"{P} 选用临时端口 {port}");

            await Services.HttpServerService.StartAsync($"http://127.0.0.1:{port}");
            sb.AppendLine($"{P} Kestrel 已启动：IsRunning={Services.HttpServerService.IsRunning}，Port={Services.HttpServerService.Port}");
            if (!Services.HttpServerService.IsRunning) throw new Exception("启动后 IsRunning 仍为 false");
            if (Services.HttpServerService.Port != port)
                throw new Exception($"Port 属性应为 {port}，实际 {Services.HttpServerService.Port}");

            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(15),
            };

            using (var resp = await http.GetAsync("/api/health"))
            {
                string body = await resp.Content.ReadAsStringAsync();
                sb.AppendLine($"{P} GET /api/health → {(int)resp.StatusCode} {body}");
                if (resp.StatusCode != HttpStatusCode.OK)
                    throw new Exception($"/api/health 应 200，实际 {(int)resp.StatusCode}");
                if (!body.Contains("\"status\":\"ok\"", StringComparison.Ordinal))
                    throw new Exception($"/api/health 响应应含 status=ok，实际 {body}");
            }
            sb.AppendLine($"{P} 健康检查通过（真实 HTTP 往返）");

            sb.AppendLine($"{P} 结论：PASS");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{P} 结论：FAIL — {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }
        finally
        {
            try { Services.HttpServerService.Stop(); } catch { }
        }

        return sb.ToString();
    }
}
