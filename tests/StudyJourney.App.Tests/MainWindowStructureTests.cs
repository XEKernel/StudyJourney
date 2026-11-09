using System.Xml.Linq;
using Xunit;

namespace StudyJourney.App.Tests;

/// <summary>
/// 主窗结构守卫（CORE_ARCHITECTURE §6 硬约束 3 / D-5 的 4 个槽位）。
///
/// 这条测试的价值不在"现在对"，而在"以后有人往 MainWindow 里加菜单/按钮/业务文本时会红"。
/// 它直接解析 axaml（XML），不启动 Avalonia —— 结构断言不需要 UI 线程。
///
/// M1-A 起允许的只有**四个槽位宿主**（ContentControl）；托盘/菜单/热键/业务控件仍然禁止：
/// 壳的内容一律由插件经 <c>IShellService.RegisterSlotContent</c> 填进来，壳自己不写业务。
/// </summary>
public sealed class MainWindowStructureTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>槽位宿主控件的 x:Name ↔ 槽位 ID（顺序即布局顺序）。</summary>
    private static readonly (string Name, string SlotId)[] ExpectedSlots =
    [
        ("PrimarySlotHost", "main.primary"),
        ("SecondarySlotHost", "main.secondary"),
        ("CornerSlotHost", "main.corner"),
        ("FooterSlotHost", "main.footer"),
    ];

    private static XElement LoadWindow()
    {
        var path = RepoRoot.Combine("src", "StudyJourney.App", "MainWindow.axaml");
        Assert.True(File.Exists(path), $"主窗文件不存在：{path}");
        return XDocument.Load(path).Root!;
    }

    private static List<XElement> SlotHosts()
        => LoadWindow().Descendants().Where(e => e.Name.LocalName == "ContentControl").ToList();

    /// <summary>取控件名：Avalonia 允许 <c>Name</c> 与 <c>x:Name</c> 两种写法，都要认。</summary>
    private static string? SlotName(XElement e)
        => e.Attribute("Name")?.Value ?? e.Attribute(XamlNs + "Name")?.Value;

    [Fact(DisplayName = "M1-01 MainWindow 根是 Window，内容恰好是一个 Grid")]
    public void MainWindow_Content_IsSingleGrid()
    {
        var root = LoadWindow();

        Assert.Equal("Window", root.Name.LocalName);

        var content = Assert.Single(root.Elements());
        Assert.Equal("Grid", content.Name.LocalName);
    }

    [Fact(DisplayName = "M1-02 主窗恰好有 4 个槽位宿主，Name 与 D-5 的槽位一一对应，无多余控件")]
    public void MainWindow_HasExactlyFourSlotHosts()
    {
        var hosts = SlotHosts();

        Assert.Equal(ExpectedSlots.Length, hosts.Count);
        Assert.Equal(
            ExpectedSlots.Select(s => s.Name),
            hosts.Select(SlotName));
    }

    [Fact(DisplayName = "M1-03 次区与角标默认隐藏（空槽不留空框）；主区与底栏常驻")]
    public void MainWindow_EmptySlotsHiddenExceptPrimaryAndFooter()
    {
        var visibility = SlotHosts().ToDictionary(e => SlotName(e)!, e => e.Attribute("IsVisible")?.Value);

        Assert.Null(visibility["PrimarySlotHost"]);              // 主区默认可见
        Assert.Equal("False", visibility["SecondarySlotHost"]);  // 空置隐藏
        Assert.Equal("False", visibility["CornerSlotHost"]);     // 空置隐藏
        Assert.Null(visibility["FooterSlotHost"]);               // 底栏常驻
    }

    [Fact(DisplayName = "M1-04 全文件没有任何菜单/托盘/热键/业务控件（壳自己不许写业务）")]
    public void MainWindow_ContainsNoShellOrBusinessControls()
    {
        var forbidden = new[]
        {
            "Menu", "MenuItem", "TrayIcon", "NativeMenu", "ContextMenu",
            "Button", "TextBox", "StackPanel", "DockPanel", "UserControl",
            "Image", "ListBox", "DataGrid", "TextBlock",
        };

        var hits = LoadWindow().Descendants()
            .Select(e => e.Name.LocalName)
            .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(hits.Count == 0, "MainWindow.axaml 出现了壳/业务元素（应由插件经槽位填）：" + string.Join(", ", hits));
    }

    [Fact(DisplayName = "M1-05 测试里的槽位常量与 ShellSlots 一致（改常量必须同步改 axaml 与这条表）")]
    public void SlotIdsMatchShellSlots()
    {
        Assert.Equal(4, StudyJourney.Sdk.ShellSlots.AllSlots.Count);
        Assert.Equal(ExpectedSlots.Select(s => s.SlotId), StudyJourney.Sdk.ShellSlots.AllSlots);
    }
}
