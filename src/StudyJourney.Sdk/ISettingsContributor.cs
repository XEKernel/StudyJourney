using System.Text.Json.Nodes;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Sdk;

/// <summary>
/// 设置页贡献者（CORE_ARCHITECTURE §2.7）。核心设置窗遍历所有实现拼装侧栏。
///
/// "重置"为什么拆成 <see cref="BuildResetPlan"/> + <see cref="ResetToDefaults"/> 两步（D-1 拍板）：
/// 清单必须在**动手之前**给用户看（"会丢 X、Y，其中 Y 不可恢复"），
/// 合成一个方法就只能"先重置再告诉用户丢了什么"——那不是确认，是事后通知。
/// </summary>
public interface ISettingsContributor
{
    /// <summary>配置节名（逻辑归属名），用于日志与错误提示。**必须与 manifest 的 configFields 指向同一批键**。</summary>
    /// <remarks>消费者：设置窗（错误提示）、SettingsResetBuilder（清单分组标题）</remarks>
    string SectionKey { get; }

    /// <summary>本插件要贡献的设置页（通常 1 个）。每页**每次点击都新建实例**，不复用，避免 Load/Apply 状态残留。</summary>
    /// <remarks>消费者：设置窗（侧栏拼装）</remarks>
    IReadOnlyList<SettingsPageDescriptor> GetPages();

    /// <summary>回答"重置我会丢什么"。**不修改任何状态**。</summary>
    /// <remarks>消费者：核心的 ResetPlanBuilder（拼成三段清单 → 勾选已知悉 → 备份）</remarks>
    ResetPlan BuildResetPlan();

    /// <summary>把自己的配置**就地**重置为默认值（原地修改传入节点，不换引用）。</summary>
    /// <param name="section">按 configFields 摘出来的临时 JsonObject，由核心负责写回。</param>
    /// <remarks>消费者：ResetPlanBuilder（用户勾选已知悉之后才调用）</remarks>
    void ResetToDefaults(JsonNode section);
}
