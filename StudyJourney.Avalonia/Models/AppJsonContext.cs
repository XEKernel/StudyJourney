using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudyJourney.Avalonia.Models;

/// <summary>
/// JSON 源生成上下文（2026-09-18 引入）。
///
/// **为什么要有它**（两个收益）：
///   ① **启动提速**：`System.Text.Json` 首次用**反射**构建类型元数据很贵 ——
///      实测 `AppSettings.Load()` 一次要 ~475 ms（AppSettings 93 个属性 + 嵌套集合），
///      这是启动耗时里除运行时自举之外最大的一块。源生成器把元数据变成**编译期产物**，
///      首次调用不再需要反射，这几百毫秒直接消失。
///   ② **NativeAOT 的前置条件**：AOT 下反射序列化被禁用
///      （`JsonSerializer.IsReflectionEnabledByDefault = false`）→ 抛 `NotSupportedException`
///      → 会静默退回默认设置（读不到用户配置）。源生成器是 AOT 能用的唯一办法。
///
/// **用法**：把 `JsonSerializer.Serialize(obj, opts)` / `Deserialize&lt;T&gt;(json, opts)`
/// 换成带 `JsonTypeInfo` 的重载：
/// <code>
///   JsonSerializer.Serialize(data, AppJsonContext.Default.ScheduleData)
///   JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)
/// </code>
/// 选项从 `JsonSerializerOptions` 移到 `[JsonSourceGenerationOptions]`（源生成器下运行时
/// 传 Options 会退回反射，等于白做）。
///
/// ⚠ **新增可序列化类型时必须在这里补一行 `[JsonSerializable]`**，否则运行期才会抛错。
/// ⚠ 本上下文只覆盖**域模型/本地文件**。远程 API 的响应（HttpServerService 里那些匿名类型
///    `Results.Json(new { ... })`）**不在**这里 —— 匿名类型无法被源生成器覆盖，
///   需另抽 DTO，尚未做（见 PLANNING 待办）。
/// </summary>
/// ⚠ 声明为 internal：上下文里注册了 internal 类型（如 `HttpServerService.TokenInfo`），
///   若上下文是 public，生成的 public 属性会比其类型更可见 → CS0053 可访问性错误。
///   反正只在同一程序集内使用，internal 足够。
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
// ── 应用设置 ──
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CustomCountdown))]
[JsonSerializable(typeof(TeacherAccount))]
[JsonSerializable(typeof(List<TeacherAccount>))]
// ── 课表 ──
[JsonSerializable(typeof(ScheduleData))]
[JsonSerializable(typeof(MakeupDay))]
[JsonSerializable(typeof(ScheduleEntry))]
[JsonSerializable(typeof(ExamSubject))]
[JsonSerializable(typeof(ExamEntry))]
[JsonSerializable(typeof(TimeTemplate))]
[JsonSerializable(typeof(TimetableRow))]
[JsonSerializable(typeof(CourseSlot))]
// ── 自动化规则 ──
[JsonSerializable(typeof(AutomationSettings))]
[JsonSerializable(typeof(AutomationRule))]
// ── 打开类动作运行期状态 ──
[JsonSerializable(typeof(OpenStateData))]
// ── PDF 续读状态 ──
[JsonSerializable(typeof(PdfReadingStateData))]
// ── 其它（泛型组合）──
[JsonSerializable(typeof(Dictionary<string, Services.HttpServerService.TokenInfo>))]
[JsonSerializable(typeof(Dictionary<string, System.DateTime>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
