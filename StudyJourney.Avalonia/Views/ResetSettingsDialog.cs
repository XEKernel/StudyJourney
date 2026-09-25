using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;   // ScrollBarVisibility
using Avalonia.Layout;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 「恢复默认设置」确认框（2026-09-25，规划 2.7 ①）。
///
/// 为什么不用通用确认框：影响范围清单一共十几行，通用框固定 420×200、无滚动 → 会被裁掉，
/// 等于又回到"看不全就按了"。这里给出**可滚动**的完整清单 + 一个必须勾选的确认项。
///
/// 用法：<c>var ok = await new ResetSettingsDialog(msg).ShowDialog&lt;bool&gt;(owner);</c>
/// </summary>
public class ResetSettingsDialog : Window
{
    public ResetSettingsDialog(string message)
    {
        Title = "恢复默认设置";
        Icon = App.AppIcon;
        Width = 600;
        Height = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;   // 与 DialogHelper 同一约定：防 Win10 层级问题把弹窗盖在后面

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24)
        };

        // ── 标题 ──
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock
        {
            Text = "⚠  恢复默认设置",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "此操作不可撤销。请先看清下面三段（会清除 / 会恢复 / 不受影响）再决定。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FFFFC24B"))
        });
        header.Children.Add(new TextBlock
        {
            Text = "重置前会自动把当前 settings.json 备份到程序目录的 backups 文件夹（最近 5 份）。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FF9AA0AA")),
            Margin = new Thickness(0, 2, 0, 10)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── 清单（可滚）──
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse("#14FFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1FFFFFFF")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12),
                Child = new TextBlock
                {
                    Text = message,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                }
            }
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // ── 确认 + 按钮 ──
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };

        var confirmCheck = new CheckBox
        {
            Content = "我已知晓上述影响，确认恢复默认设置",
            FontSize = 13
        };
        footer.Children.Add(confirmCheck);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancelBtn = new Button { Content = "取消", MinWidth = 88 };
        var okBtn = new Button
        {
            Content = "恢复默认设置",
            Classes = { "accent" },
            MinWidth = 130,
            IsEnabled = false            // 未勾选确认前不允许按（防误触）
        };
        confirmCheck.IsCheckedChanged += (_, _) => okBtn.IsEnabled = confirmCheck.IsChecked == true;
        cancelBtn.Click += (_, _) => Close(false);
        okBtn.Click += (_, _) => Close(true);
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        footer.Children.Add(btnRow);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }
}
