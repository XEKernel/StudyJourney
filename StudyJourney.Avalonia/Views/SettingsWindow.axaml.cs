using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>WinUI 3 风格设置窗口：Mica + NavigationView 左侧导航 + 页面切换</summary>
public partial class SettingsWindow : FluentAvalonia.UI.Windowing.FAAppWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
    }

    private void NavView_ItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        var tag = (e.InvokedItemContainer as FANavigationViewItem)?.Tag?.ToString();
        PageHost.Child = tag switch
        {
            "appearance" => new AppearancePage(),
            "position"   => new PositionPage(),
            "about"      => new AboutPage(),
            _            => new DisplayPage()
        };
    }
}
