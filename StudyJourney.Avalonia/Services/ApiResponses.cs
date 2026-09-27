using System.Collections.Generic;
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
//  **进度**：**已全覆盖（2026-09-27）** —— 含嵌套对象 / 列表 / `List<object>` 的响应全部改成具名 DTO，
//  服务端再无 `Result.Json(new {...})` / `WriteAsJsonAsync(new {...})`。
//  改动前先对着**网页端取值**核字段名（console JS 就在 `HttpServerService.cs` 里，搜 `j.subjects` 这类）：
//    · teachers → `t.displayName` / `t.subject`
//    · open-targets → `x.subject` / `hit.files` / `f.path` / `f.name` / `a.path` / `a.name` / `p.kind|path|subject|setBy|setAt`
//    · health → `j.disk.text` / `j.serverTime` / `j.uptimeMinutes` / `j.recentUploads`
//    · upload-dir → `j.presets`（`p.label` / `p.path`）;  上传结果 → `j.openWarning`
//  自检 `SJ_SELFTEST=api` 会逐条断言每个 DTO 实际写出的键集合 == 期望集合。
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

// ── 请求体（POST/PUT 的 body）────────────────────────────────────────────
// ⚠ 这些原来是 `ReadFromJsonAsync<T>()`（ASP.NET 给的是 **Web 默认选项**：大小写不敏感），
//   改用源生成上下文后**不再大小写不敏感** —— 所以这里必须逐字段显式写 [JsonPropertyName]，
//   写错/漏写 = 网页端提交的表单**静默不生效**（比如登录永远"用户名或密码错误"）。

/// <summary>POST /api/login 请求体（网页端发 `{username, password, rememberMe}`）</summary>
internal sealed class LoginRequest
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("rememberMe")] public bool RememberMe { get; set; }
}

/// <summary>PUT /api/upload-dir 请求体：path = 完整目录（空 = 恢复默认）</summary>
internal sealed class UploadDirRequest
{
    [JsonPropertyName("path")] public string? Path { get; set; }
}

/// <summary>PUT /api/config 请求体：班级名称 / 老师显示名（字段可省略）</summary>
internal sealed class ConfigRequest
{
    [JsonPropertyName("className")] public string? ClassName { get; set; }
    [JsonPropertyName("teacherName")] public string? TeacherName { get; set; }
}

/// <summary>POST /api/pending-open 请求体：指定下节课打开的文件或软件（2.5.9B）</summary>
internal sealed class PendingOpenRequest
{
    /// <summary>"file" = 打开文件（课件）/ "app" = 启动软件</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    /// <summary>限定科目（可空 = 不限）</summary>
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>`{ ok, token, username, displayName, rememberMe, expiresAt }` —— 登录成功
/// （网页端读 `j.ok` / `j.token` / `j.displayName` / `j.username`）</summary>
internal sealed class ApiLogin
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("rememberMe")] public bool RememberMe { get; set; }
    [JsonPropertyName("expiresAt")] public System.DateTime ExpiresAt { get; set; }
}

/// <summary>某天课表 GET /api/schedule（网页端读 `j.schedule` 渲染表格）</summary>
internal sealed class ApiScheduleDay
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("weekday")] public int Weekday { get; set; }
    [JsonPropertyName("schedule")] public Models.ScheduleData Schedule { get; set; } = new();
}

/// <summary>班级配置 GET /api/config（网页端读 `j.className` / `j.teacherName`）</summary>
internal sealed class ApiConfig
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("className")] public string ClassName { get; set; } = "";
    [JsonPropertyName("teacherName")] public string TeacherName { get; set; } = "";
    [JsonPropertyName("loginUsername")] public string LoginUsername { get; set; } = "";
    [JsonPropertyName("subjects")] public List<string> Subjects { get; set; } = new();
}

/// <summary>`{ success, message, className, teacherName }` —— 班级配置保存结果</summary>
internal sealed class ApiConfigSaved
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("className")] public string ClassName { get; set; } = "";
    [JsonPropertyName("teacherName")] public string TeacherName { get; set; } = "";
}

/// <summary>`{ error }` —— 中间件直接回绝（IP 白名单 / Token 无效），不走 Results 管道</summary>
internal sealed class ApiError
{
    [JsonPropertyName("error")] public string Error { get; set; } = "";
}

/// <summary>`{ displayName, subject }` —— 老师账号项（**刻意不含 username**，防账号名被枚举）</summary>
internal sealed class ApiTeacherItem
{
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("subject")] public string Subject { get; set; } = "";
}

/// <summary>`{ ok, count, teachers }` —— 登录页下拉用（公开接口）</summary>
internal sealed class ApiTeachers
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("teachers")] public List<ApiTeacherItem> Teachers { get; set; } = new();
}

/// <summary>`{ freeBytes, totalBytes, text }` —— 健康探针里的磁盘信息（嵌套）</summary>
internal sealed class ApiDisk
{
    [JsonPropertyName("freeBytes")] public long FreeBytes { get; set; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

/// <summary>健康探针 GET /api/health（网页端读 `disk.text` / `serverTime` / `uptimeMinutes` / `recentUploads`）</summary>
internal sealed class ApiHealth
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("serverTime")] public string ServerTime { get; set; } = "";
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("osVersion")] public string OsVersion { get; set; } = "";
    [JsonPropertyName("uptimeMinutes")] public int UptimeMinutes { get; set; }
    [JsonPropertyName("disk")] public ApiDisk Disk { get; set; } = new();
    [JsonPropertyName("recentUploads")] public List<string> RecentUploads { get; set; } = new();
}

/// <summary>`{ ok, logDirectory, count, lines }` —— 操作日志（供控制台查看）</summary>
internal sealed class ApiLogs
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("logDirectory")] public string LogDirectory { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = new();
}

/// <summary>`{ label, path }` —— 上传目录预设项</summary>
internal sealed class ApiUploadDirItem
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>`{ ok, current, defaultPath, presets }` —— 上传目录（网页端下拉点选）</summary>
internal sealed class ApiUploadDir
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("current")] public string Current { get; set; } = "";
    [JsonPropertyName("defaultPath")] public string DefaultPath { get; set; } = "";
    [JsonPropertyName("presets")] public List<ApiUploadDirItem> Presets { get; set; } = new();
}

/// <summary>`{ name, path }` —— 课件文件 / 常用软件项（网页端读 `f.path` / `f.name`）</summary>
internal sealed class ApiFileItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>`{ subject, files }` —— 按科目分组的课件（网页端读 `x.subject` / `hit.files`）</summary>
internal sealed class ApiSubjectGroup
{
    [JsonPropertyName("subject")] public string Subject { get; set; } = "";
    [JsonPropertyName("files")] public List<ApiFileItem> Files { get; set; } = new();
}

/// <summary>待打开指定（一次性）：网页端读 `p.kind|path|subject|setBy|setAt`</summary>
internal sealed class ApiPendingOpen
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("subject")] public string Subject { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("setBy")] public string SetBy { get; set; } = "";
    [JsonPropertyName("setAt")] public string SetAt { get; set; } = "";
}

/// <summary>下节课打开什么 GET /api/open-targets（网页端读 subjects/apps/pending）</summary>
internal sealed class ApiOpenTargets
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("coursewareRoot")] public string CoursewareRoot { get; set; } = "";
    [JsonPropertyName("subjects")] public List<ApiSubjectGroup> Subjects { get; set; } = new();
    [JsonPropertyName("apps")] public List<ApiFileItem> Apps { get; set; } = new();
    /// <summary>无指定时为 null（网页端据此判断"将按上次用过的课件自动打开"）</summary>
    [JsonPropertyName("pending")] public ApiPendingOpen? Pending { get; set; }
}

/// <summary>`{ success, fileName, path, openWarning }` —— 课件上传结果（openWarning 可空）</summary>
internal sealed class ApiUploadResult
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    /// <summary>保存成功但"自动打开"失败时的提示（此时为 null）/ 或提示文案</summary>
    [JsonPropertyName("openWarning")] public string? OpenWarning { get; set; }
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
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiTeacherItem))]
[JsonSerializable(typeof(ApiTeachers))]
[JsonSerializable(typeof(ApiDisk))]
[JsonSerializable(typeof(ApiHealth))]
[JsonSerializable(typeof(ApiLogs))]
[JsonSerializable(typeof(ApiUploadDirItem))]
[JsonSerializable(typeof(ApiUploadDir))]
[JsonSerializable(typeof(ApiFileItem))]
[JsonSerializable(typeof(ApiSubjectGroup))]
[JsonSerializable(typeof(ApiPendingOpen))]
[JsonSerializable(typeof(ApiOpenTargets))]
[JsonSerializable(typeof(ApiUploadResult))]
[JsonSerializable(typeof(ApiLogin))]
[JsonSerializable(typeof(ApiScheduleDay))]
[JsonSerializable(typeof(ApiConfig))]
[JsonSerializable(typeof(ApiConfigSaved))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(UploadDirRequest))]
[JsonSerializable(typeof(ConfigRequest))]
[JsonSerializable(typeof(PendingOpenRequest))]
internal partial class ApiJsonContext : JsonSerializerContext
{
}
