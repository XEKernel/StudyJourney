using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>WinUI 3 风格设置窗口：Mica + NavigationView 导航 + 页面切换（对齐学程原版 6 Tab）</summary>
public partial class SettingsWindow : FluentAvalonia.UI.Windowing.FAAppWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void NavView_ItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        var tag = (e.InvokedItemContainer as FANavigationViewItem)?.Tag?.ToString();
        Control page = tag switch
        {
            "position"  => new PositionPage(),
            "api"       => new ApiPage(),
            "schedule"  => new SchedulePage(),
            "exam"      => new ExamPage(),
            "about"     => new AboutPage(),
            _           => new CountdownPage()
        };

        // 页面切换淡入动画（WinUI 3 风格过渡）
        PageHost.Opacity = 0;
        PageHost.Child = page;
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } }
            }
        };
        fade.RunAsync(PageHost);
    }
}
