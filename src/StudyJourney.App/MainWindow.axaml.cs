using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StudyJourney.App;

/// <summary>
/// M0 的空白主窗。**只有壳，没有业务** —— 插件通过 <c>IShellService</c> 往上报内容（M1 起）。
/// 壳不知道任何插件的存在，这是微内核的前提（§2.5）。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
