using System.Xml.Linq;
using Xunit;

namespace StudyJourney.App.Tests;

/// <summary>
/// S10 / 硬约束 3：**主窗只有空 Grid**（CORE_ARCHITECTURE §6 第 3 条）。
///
/// 这条测试的价值不在"现在对"，而在"以后有人往 MainWindow 里加菜单/托盘/槽位内容时会红"。
/// 它直接解析 axaml（XML），不启动 Avalonia —— 结构断言不需要 UI 线程。
/// </summary>
public sealed class MainWindowStructureTests
{
    private static XElement LoadWindow()
    {
        var path = RepoRoot.Combine("src", "StudyJourney.App", "MainWindow.axaml");
        Assert.True(File.Exists(path), $"主窗文件不存在：{path}");
        return XDocument.Load(path).Root!;
    }

    [Fact(DisplayName = "S10-01 MainWindow 根是 Window，内容恰好是一个 Grid")]
    public void MainWindow_Content_IsSingleGrid()
    {
        var root = LoadWindow();

        Assert.Equal("Window", root.Name.LocalName);

        var children = root.Elements().ToList();
        var content = Assert.Single(children);
        Assert.Equal("Grid", content.Name.LocalName);
    }

    [Fact(DisplayName = "S10-02 那个 Grid 是空的：没有子元素、没有属性内容")]
    public void MainWindow_Grid_IsEmpty()
    {
        var grid = LoadWindow().Elements().Single();

        Assert.Empty(grid.Elements());
        Assert.Empty(grid.Descendants());
    }

    [Fact(DisplayName = "S10-03 全文件没有任何菜单/托盘/热键/槽位/业务控件")]
    public void MainWindow_ContainsNoShellOrBusinessControls()
    {
        var root = LoadWindow();
        var forbidden = new[]
        {
            "Menu", "MenuItem", "TrayIcon", "NativeMenu", "ContextMenu",
            "Button", "TextBlock", "TextBox", "StackPanel", "DockPanel",
            "UserControl", "ContentPresenter",
        };

        var hits = root.Descendants()
            .Select(e => e.Name.LocalName)
            .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(hits.Count == 0, "MainWindow.axaml 出现了 M1 才允许的壳/业务元素：" + string.Join(", ", hits));
    }
}
