namespace StudyJourney.Sdk.Context;

/// <summary>
/// 插件存储句柄（CORE_ARCHITECTURE §2.9 / D5）。
///
/// 生命周期约定：**卸载插件时删除 <c>plugins/&lt;id&gt;/</c>，保留 <c>data/&lt;id&gt;/</c>**——
/// 用户业务数据（成绩库、活动记录）一律不删，老师卸了又装回来数据还在。
/// </summary>
public interface IPluginStorage
{
    /// <summary>业务数据目录 <c>data/&lt;id&gt;/</c> 的绝对路径（已确保存在）。</summary>
    /// <remarks>消费者：成绩（grades.db）、诊断（records/*.jsonl）、自动化（open-state.json）</remarks>
    string DataDirectory { get; }

    /// <summary>插件自身目录 <c>plugins/&lt;id&gt;/</c> 的绝对路径（只读用途：读内置资源）。**更新会覆盖此目录，不得在此写状态。**</summary>
    /// <remarks>消费者：需要读取随包分发的只读资源（模板、示例数据）的插件</remarks>
    string PluginDirectory { get; }

    /// <summary>把相对路径拼到 <see cref="DataDirectory"/> 下。**拒绝 <c>..</c> 越界（抛 ArgumentException）。**</summary>
    /// <remarks>消费者：需要多文件存储的插件（避免自己拼路径拼出越界）</remarks>
    string ResolvePath(string relativePath);
}
