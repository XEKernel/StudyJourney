using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StudyJourney.Avalonia.Services;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 更新下载进度窗（2026-09-15 新增）。
///
/// 为什么需要它：更新包 38~84 MB，走镜像也要几分钟。原实现点完"立即更新"之后
/// 界面**没有任何反馈**，老师会以为程序卡死/点了没反应。这里给一个明确的进度条 + 取消按钮，
/// 也让"取消"变成可行的选择（原实现一旦开始下载就只能等）。
/// </summary>
public sealed class UpdateProgressWindow : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _phaseTb;
    private readonly TextBlock _detailTb;
    private readonly Button _cancelBtn;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>传给下载流程的取消令牌</summary>
    public CancellationToken Token => _cts.Token;

    public UpdateProgressWindow()
    {
        Title = "学程 — 正在更新";
        Width = 440;
        Height = 200;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _phaseTb = new TextBlock
        {
            Text = "正在下载更新包…",
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
        };

        _detailTb = new TextBlock
        {
            Text = "正在连接下载通道…",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
        };

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 6,
            IsIndeterminate = true,
        };

        _cancelBtn = new Button
        {
            Content = "取消",
            MinWidth = 88,
            Height = 34,
            CornerRadius = new CornerRadius(0),      // 直角：项目统一视觉约定
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _cancelBtn.Click += (_, _) => Cancel();

        var panel = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 14,
            Children =
            {
                _phaseTb,
                _detailTb,
                _bar,
                new TextBlock
                {
                    Text = "下载完成后程序会自动重启并完成替换，请不要关闭电源。",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                },
                _cancelBtn,
            },
        };
        Content = panel;

        // 关窗（Alt+F4 / 标题栏 X）= 取消，避免下载还在跑却没人接收结果
        Closing += (_, _) => { if (!_cts.IsCancellationRequested) _cts.Cancel(); };
    }

    /// <summary>切换阶段文字（正在下载 / 正在解压 / 重启）</summary>
    public void Phase(string text)
    {
        try { _phaseTb.Text = text; } catch { /* 窗口已关闭 */ }
    }

    /// <summary>进度回调（在 UI 线程调用）</summary>
    public void Report(UpdateProgress p)
    {
        try
        {
            if (p.Fraction >= 0)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = p.Fraction;
                _detailTb.Text = $"{p.Describe()}（{p.Fraction * 100:0}%）";
            }
            else
            {
                _bar.IsIndeterminate = true;
                _detailTb.Text = $"{p.Describe()}…";
            }
        }
        catch { /* 窗口已关闭 */ }
    }

    /// <summary>切换到"下载完成，正在启动更新程序"状态</summary>
    public void SwitchToInstalling()
    {
        try
        {
            _phaseTb.Text = "下载完成，正在启动更新程序…";
            _detailTb.Text = "程序即将自动关闭并替换为新版本。";
            _bar.IsIndeterminate = false;
            _bar.Value = 1;
            _cancelBtn.IsEnabled = false;
        }
        catch { }
    }

    public void Cancel()
    {
        try
        {
            if (!_cts.IsCancellationRequested) _cts.Cancel();
            _phaseTb.Text = "已取消";
            _detailTb.Text = "可以关闭本窗口了。";
            _cancelBtn.IsEnabled = false;
            _bar.IsIndeterminate = false;
        }
        catch { }
    }
}
