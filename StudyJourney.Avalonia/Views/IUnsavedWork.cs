namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 有"一关就丢"的内容的窗口。
///
/// **为什么需要它**（2026-09-22 代码审查发现）：自动更新走 `Environment.Exit(0)`，
/// 它**不触发任何窗口的 Closing 事件**，也不跑 finally —— 于是白板/批注/PDF 的
/// "未保存墨迹"确认、课表编辑器的"未保存修改"确认、设置窗口的三选一**全部被静默跳过**，
/// 老师的东西就这么没了。
///
/// 所以更新前要主动遍历所有窗口问一遍（见 `App.IsSafeToRestartForUpdate`），
/// 有任何一处"有未保存内容"就**不重启**，等老师处理完再说。
/// 由各窗口自己实现，避免 App 里硬编码窗口清单（新增窗口时容易漏）。
/// </summary>
public interface IUnsavedWork
{
    /// <summary>有未保存 / 未导出的内容吗</summary>
    bool HasUnsavedWork { get; }

    /// <summary>给用户看的一句话，如"白板上有未导出的板书"</summary>
    string UnsavedWorkHint { get; }
}
