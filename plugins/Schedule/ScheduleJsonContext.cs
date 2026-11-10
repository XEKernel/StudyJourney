using System.Text.Json.Serialization;
using StudyJourney.Plugin.Schedule.Models;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// <c>schedule.json</c> 的 System.Text.Json **源生成**上下文。
///
/// 为什么不用反射序列化：本项目按 NativeAOT 分发（B1），反射元数据会被裁掉，
/// 运行期表现为"能读能存但字段全是默认值"，而且只在发布版复现。
///
/// ⚠ 必须是 <c>internal</c>/同程序集可见：裁剪后跨程序集拿不到 <c>Default</c> 属性。
/// ⚠ 只声明根类型即可，<c>Dictionary&lt;int, List&lt;TimeTemplate&gt;&gt;</c>
/// 这种嵌套结构会被生成器递归带上（int 键会写成 JSON 字符串）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ScheduleData))]
internal sealed partial class ScheduleJsonContext : JsonSerializerContext
{
}
