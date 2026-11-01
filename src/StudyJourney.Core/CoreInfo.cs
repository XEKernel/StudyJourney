namespace StudyJourney.Core;

/// <summary>
/// 程序集标记类型。
///
/// 存在的两个理由：
/// ① 让测试能以 <c>typeof(CoreInfo)</c> 确认 Core 已被加载（不必用字符串反射）；
/// ② 供 S10 组合根与 <c>--selftest</c> 输出打印核心程序集名。
///
/// ⚠ 刻意**不含任何逻辑**，也刻意**不引用任何 Avalonia 类型**：
///    本阶段要断言的是"Core 没有被污染进桌面层"，
///    若为了让"引用了 Avalonia"这条断言通过而在这里凑一个 Control 引用，就是自己骗自己。
/// </summary>
public static class CoreInfo
{
    /// <summary>核心程序集名，与 csproj 的 AssemblyName 一致。</summary>
    public const string CoreAssemblyName = "StudyJourney.Core";
}
