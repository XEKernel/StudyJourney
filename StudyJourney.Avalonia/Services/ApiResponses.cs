using System.Text.Json.Serialization;

namespace StudyJourney.Avalonia.Services;

// ════════════════════════════════════════════════════════════════════════
//  远程控制台 API 的响应 DTO（2026-09-20 起逐步替换匿名类型）
//
//  **为什么要换掉 `Results.Json(new { ... })`**：
//  匿名类型无法被 System.Text.Json 源生成器覆盖，源生成器不接受 `[JsonSerializable]` 指向
//  匿名类型 → AOT 下这些响应只能走反射序列化，而 AOT 已禁用反射 → 直接失败。
//  这是 NativeAOT 目前**唯一剩下的阻塞点**（域模型已于 v2.13.0 全部走源生成器）。
//
//  **改这里的铁律**：
//  1. `[JsonPropertyName]` 必须与原来匿名类型里的属性名**逐字一致** ——
//     老师端网页（console JS）按这些名字取值，改错了就是"控制台白屏/功能失灵"。
//  2. 不要给这些 DTO 加 `[JsonIgnore]` 之外的字段裁剪 —— 网页可能依赖某些字段存在。
//  3. 自检 `SJ_SELFTEST=api` 会断言每个 DTO 实际写出的键集合 == 期望集合（防改名/漏字段）。
//
//  **进度**：已覆盖 23/35 处响应（扁平形状）。
//  剩 12 处含嵌套匿名对象或 `List<object>`，需要先给那些成员定义真实类型 —— 见 PLANNING 待办。
// ════════════════════════════════════════════════════════════════════════

/// <summary>`{ success, message }` —— 用得最多（约 20 处：保存/上传/删除等操作的结果反馈）</summary>
internal sealed class ApiMsg
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>`{ ok, error }` —— 认证失败（凭据错误 / 限速）</summary>
internal sealed class ApiOkError
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; } = "";
}

/// <summary>`{ ok }` —— 只有成功标志（如删除成功）</summary>
internal sealed class ApiOkOnly
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
}

/// <summary>`{ status }` —— 健康探针 GET /api/health</summary>
internal sealed class ApiStatus
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

/// <summary>`{ success, message, path }` —— 上传目录保存</summary>
internal sealed class ApiMsgPath
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>
/// API 响应的 JSON 源生成上下文。
/// ⚠ 与域模型的 <see cref="Models.AppJsonContext"/> 分开：这边**不写缩进**（省流量、
///   与原先 `Results.Json(new {...})` 的输出一致），那边的本地配置文件才需要缩进便于人工查看。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ApiMsg))]
[JsonSerializable(typeof(ApiOkError))]
[JsonSerializable(typeof(ApiOkOnly))]
[JsonSerializable(typeof(ApiStatus))]
[JsonSerializable(typeof(ApiMsgPath))]
internal partial class ApiJsonContext : JsonSerializerContext
{
}
