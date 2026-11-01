namespace StudyJourney.Sdk;

/// <summary>
/// 设置页契约（CORE_ARCHITECTURE §2.14，24 行）。插件的 <c>Control</c> 实现它，
/// 核心设置窗就自动获得"切页提示未保存"的能力。
///
/// <para>
/// <b>三条隐含语义（v3.3.0 用事故换来的，不是风格偏好）：</b>
/// </para>
/// <list type="number">
/// <item>
/// <b><see cref="Load"/> 与 <see cref="Apply"/> 必须作用于同一个配置实例。</b>
/// 违反症状：用户在页面上改了什么、点保存没生效；或"保存了但下次打开又是旧值"。
/// （v3.2.2 之前关于页三处开关就是这样静默丢弃的。）
/// </item>
/// <item>
/// <b>每个可编辑页必须实现 <see cref="IsDirty"/>，不许恒返回 false 蒙混。</b>
/// 违反症状：切页 / 关窗时改动被静默丢弃。
/// </item>
/// <item>
/// <b><see cref="IsDirty"/> 必须比较"控件当前值 vs 进页时快照"，不能只比"模型是否被改过"。</b>
/// 违反症状：控件里的改动还没回写模型，因此 IsDirty 永远是 false。
/// </item>
/// </list>
/// </summary>
public interface ISettingsPage
{
    /// <summary>从配置读值填进控件。进页时由核心调用一次。</summary>
    /// <remarks>消费者：设置窗（切换到该页时）</remarks>
    void Load();

    /// <summary>把控件值写回配置。点"确定/应用"时由核心调用。</summary>
    /// <returns>失败（如校验不过）→ 核心不关闭设置窗并提示。</returns>
    /// <remarks>消费者：设置窗（保存）</remarks>
    Result Apply();

    /// <summary>控件当前值是否与进页时快照不同。切页 / 关窗 / 更新重启前都会被问。</summary>
    /// <remarks>消费者：设置窗（切页提示）、IUnsavedWork（窗口级）、更新编排（重启前）</remarks>
    bool IsDirty { get; }
}
