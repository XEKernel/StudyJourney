using Avalonia.Controls;

namespace StudyJourney.Plugin.Test;

/// <summary>
/// **只声明、不实例化**的 <see cref="UserControl"/> 子类（S11 规格要求的"Avalonia 类型用一次"）。
///
/// 它证明的是：插件可以定义 UI 类型，而该类型继承的 <c>UserControl</c> 来自**与核心同一份**
/// Avalonia 程序集（ALC 白名单）；若白名单失效，这里会拿到插件目录里的第二份，
/// A0-15 那条断言就会失败。⚠ 不要在 <c>InitializeAsync</c> 里 new 它（UI 线程纪律）。
/// </summary>
public sealed class TestControl : UserControl
{
}
