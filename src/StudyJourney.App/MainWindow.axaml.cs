using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StudyJourney.Sdk;

namespace StudyJourney.App;

/// <summary>
/// 主窗壳。**它不知道任何插件的存在** —— 只知道四个槽位常量（<see cref="ShellSlots"/>），
/// 内容由 <see cref="ShellHost"/> 按插件注册结果填（CORE_ARCHITECTURE §2.5 / D-5）。
///
/// 槽位宿主控件在此暴露给 <see cref="ShellHost"/>；除这四个 ContentControl 外，
/// 本窗不含任何业务控件（<c>MainWindowStructureTests</c> 解析 axaml 守着）。
///
/// ⚠ <b>为什么用 <c>FindControl</c> 而不是 axaml 里 <c>x:Name</c> 生成的字段</b>：
/// Avalonia **不会**像 WPF 那样为 <c>x:Name</c> 自动生成字段（要装 <c>Avalonia.NameGenerator</c> 源生成器才行）。
/// 直接写 <c>PrimarySlotHost</c> 能编译通过，但**运行期恒为 null** —— 症状是"槽位永远挂不上内容，
/// 日志说未知槽位"，而编译 0 错 0 警（PITFALLS §D）。
/// </summary>
public partial class MainWindow : Window
{
    private ContentControl? _primary;
    private ContentControl? _secondary;
    private ContentControl? _corner;
    private ContentControl? _footer;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _primary = this.FindControl<ContentControl>("PrimarySlotHost");
        _secondary = this.FindControl<ContentControl>("SecondarySlotHost");
        _corner = this.FindControl<ContentControl>("CornerSlotHost");
        _footer = this.FindControl<ContentControl>("FooterSlotHost");
    }

    /// <summary>按槽位 ID 取承载控件；未知 ID 返回 <c>null</c>（调用方据此报 slot-unknown）。</summary>
    public ContentControl? SlotHost(string slotId) => slotId switch
    {
        ShellSlots.Primary => _primary,
        ShellSlots.Secondary => _secondary,
        ShellSlots.Corner => _corner,
        ShellSlots.Footer => _footer,
        _ => null,
    };
}
