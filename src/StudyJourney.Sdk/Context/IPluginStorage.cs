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

    /// <summary>
    /// 程序所在目录（exe 旁）。**给"必须留在原位置"的 v3.3.0 遗留数据文件用**：
    /// <c>schedule.json</c> / <c>automations.json</c> / <c>tokens.json</c>。
    /// </summary>
    /// <remarks>
    /// 消费者：课表（`schedule.json` 读写）、自动化（`automations.json`）、远程（`tokens.json`）。
    ///
    /// **为什么不是"把老文件搬进 data/&lt;id&gt;/"**：D 系列已定"数据零迁移"——
    /// 老师机器上已有的 <c>schedule.json</c> 必须原地就被新版本读懂，不能要求用户先搬文件。
    /// 自动复制看似等价，但会造出两份不同步的副本（老程序写 A、新程序写 B），
    /// 而这两份文件在"整文件夹拷贝分发"的分发模式下**都可能被老师手动替换**（用同事的课表覆盖）。
    /// 原地读写是唯一没有分叉的做法。
    ///
    /// ⚠ **只在这里放"历史遗留 + 需与原程序目录共存"的文件**。新建的插件数据一律走
    /// <see cref="DataDirectory"/>——别因为这里有写权限就往程序目录扔新文件。
    /// </remarks>
    string HostDirectory { get; }

    /// <summary>把相对路径拼到 <see cref="DataDirectory"/> 下。**拒绝 <c>..</c> 越界（抛 ArgumentException）。**</summary>
    /// <remarks>消费者：需要多文件存储的插件（避免自己拼路径拼出越界）</remarks>
    string ResolvePath(string relativePath);
}
