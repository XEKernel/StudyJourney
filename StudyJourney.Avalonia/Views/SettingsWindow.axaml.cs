using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>WinUI 3 风格设置窗口：Mica + NavigationView 导航 + 6 Tab（对齐学程原版）+ 保存到 settings.json</summary>
public partial class SettingsWindow : FluentAvalonia.UI.Windowing.FAAppWindow
{
    private Control? _currentPage;

    public SettingsWindow()
    {
        InitializeComponent();
        // 默认显示倒计时页（含数据加载）
        ShowPage(new CountdownPage());
    }

    private void NavView_ItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        var tag = (e.InvokedItemContainer as FANavigationViewItem)?.Tag?.ToString();
        ShowPage(tag switch
        {
            "position" => (Control)new PositionPage(),
            "api"      => new ApiPage(),
            "schedule" => new SchedulePage(),
            "exam"     => new ExamPage(),
            "about"    => new AboutPage(),
            _          => new CountdownPage()
        });
    }

    /// <summary>切换页面：Load 当前设置 + 淡入动画</summary>
    private void ShowPage(Control page)
    {
        _currentPage = page;
        if (page is ISettingsPage sp) sp.Load(App.Settings);

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

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage is ISettingsPage sp)
        {
            sp.Apply(App.Settings);
            App.SaveSettings();   // 保存并通知主窗口刷新
        }
        // 提示保存成功（简单处理：短暂改按钮文字）
        if (sender is Button btn)
        {
            var old = btn.Content;
            btn.Content = "✓ 已保存";
            btn.IsEnabled = false;
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1200);
                btn.Content = old;
                btn.IsEnabled = true;
            });
        }
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
