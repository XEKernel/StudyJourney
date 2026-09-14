using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 「屏幕即将关闭」提示条（2.5.7c）。
///
/// 为什么需要它：闲置熄屏改为全天候生效后（上课也熄），老师放 PPT/视频时长时间不碰鼠标，
/// 到点会被突然黑屏。这里先给 N 秒倒计时 + 「取消」，超时无人理会才真正熄屏。
///
/// 轻量非模态：不阻塞、不抢焦点、右下角显示；关闭窗口 = 取消。
/// </summary>
public sealed class ScreenOffPromptWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<bool> _tcs = new();
    private readonly TextBlock _countTb;
    private readonly TextBlock _hintTb;
    private int _left;

    private ScreenOffPromptWindow(int seconds)
    {
        _left = Math.Max(seconds, 3);

        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _countTb = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
        };
        _hintTb = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            Text = "放课件时不希望熄屏？点「取消」即可；之后鼠标或触屏一动会重新计时。",
        };

        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(18, 6) };
        cancelBtn.Click += (_, _) => Finish(false);

        var offBtn = new Button { Content = "立即关闭屏幕", Padding = new Thickness(18, 6), Margin = new Thickness(8, 0, 0, 0) };
        offBtn.Click += (_, _) => Finish(true);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1B, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "💡 屏幕即将关闭",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    },
                    _countTb,
                    _hintTb,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 4, 0, 0),
                        Children = { cancelBtn, offBtn },
                    },
                },
            },
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0) Finish(true);
            else UpdateText();
        };
        UpdateText();

        Opened += (_, _) =>
        {
            // 布局完成后再定位（Show() 返回时 Bounds 才可用）
            Dispatcher.UIThread.Post(PlaceBottomRight, DispatcherPriority.Loaded);
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            // 健壮性：窗口若因非按钮原因被关闭（系统注销 / 显示器变更 / Alt+F4 等），
            // 必须让 TCS 落地，否则 RunScreenOffPromptAsync 的 await 永不返回 →
            // finally 不执行 → _screenOffPromptShowing 永久为 true → 之后所有闲置熄屏被静默拦掉。
            if (!_tcs.Task.IsCompleted) _tcs.TrySetResult(false);
        };
    }

    private void UpdateText() => _countTb.Text = $"{_left} 秒后自动关闭屏幕";

    private void PlaceBottomRight()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            int w = (int)Math.Ceiling(Bounds.Width);
            int h = (int)Math.Ceiling(Bounds.Height);
            if (w <= 0) w = 420;
            if (h <= 0) h = 130;
            Position = new PixelPoint(wa.X + wa.Width - w - 36, wa.Y + wa.Height - h - 56);
        }
        catch { /* 定位失败不影响功能，用系统默认位置 */ }
    }

    private void Finish(bool confirmed)
    {
        _timer.Stop();
        if (!_tcs.Task.IsCompleted) _tcs.TrySetResult(confirmed);
        try { Close(); } catch { }
    }

    /// <summary>显示提示并等待结果：true = 到点/用户点了"立即关闭"，false = 用户取消。必须在 UI 线程调用</summary>
    public static Task<bool> AskAsync(int seconds)
    {
        var w = new ScreenOffPromptWindow(seconds);
        w.Show();
        return w._tcs.Task;
    }
}
