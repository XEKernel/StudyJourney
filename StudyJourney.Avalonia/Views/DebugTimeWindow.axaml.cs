using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>时间模拟调试窗口：快速跳到快下课/下课后/快上课等关键时刻观察效果</summary>
public partial class DebugTimeWindow : Window
{
    private DispatcherTimer? _ticker;

    public DebugTimeWindow()
    {
        InitializeComponent();
        Icon = App.AppIcon;
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => RefreshStatus();
        _ticker.Start();
        Closed += (_, _) => _ticker?.Stop();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var off = TimeSimulator.Offset;
        string offText = off == TimeSpan.Zero ? "（实时）"
            : (off.TotalMinutes >= 0 ? "（快进 " : "（回退 ") + FormatSpan(off) + "）";
        NowTb.Text = $"当前模拟时间：{TimeSimulator.FormatNow()}\n实际时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n偏移 {offText}";
    }

    private static string FormatSpan(TimeSpan ts)
    {
        bool neg = ts < TimeSpan.Zero;
        var a = neg ? -ts : ts;
        string s = a.TotalHours >= 1 ? $"{a.Hours} 小时 {a.Minutes} 分" : $"{a.Minutes} 分 {a.Seconds} 秒";
        return (neg ? "-" : "+") + s;
    }

    // ── 目标时间计算（基于模拟时间查询课表）─────────────────
    private bool TryGetCurrent(out ScheduleEntry? cur)
    {
        cur = App.Schedule.GetCurrentEntry(TimeSimulator.Now);
        return cur != null;
    }

    private void ShowHint(string msg) => HintTb.Text = msg;

    /// <summary>上课中：跳到当前课开始后 3 分钟</summary>
    private void JumpInClass_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var cur)) { ShowHint("⚠ 当前模拟时间不在上课时段"); return; }
        var target = cur!.GetStartDateTime(TimeSimulator.Now.Date).TimeOfDay + TimeSpan.FromMinutes(3);
        TimeSimulator.JumpTo(target);
        ShowHint($"已跳到「{cur.Subject}」上课中（{target:hh\\:mm}）");
        RefreshStatus();
    }

    /// <summary>快下课：跳到当前课结束前 1 分钟（触发 ClassEndSoon + 60s 倒计时）</summary>
    private void JumpEndSoon_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var cur)) { ShowHint("⚠ 当前模拟时间不在上课时段"); return; }
        var target = cur!.EndTime - TimeSpan.FromMinutes(1);
        TimeSimulator.JumpTo(target);
        ShowHint($"已跳到「{cur.Subject}」快下课（还剩 1 分钟）→ 触发下课倒计时");
        RefreshStatus();
    }

    /// <summary>已下课：跳到当前课结束后 3 分钟（课间/放学）</summary>
    private void JumpAfterClass_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetCurrent(out var cur)) { ShowHint("⚠ 当前模拟时间不在上课时段"); return; }
        var target = cur!.EndTime + TimeSpan.FromMinutes(3);
        TimeSimulator.JumpTo(target);
        ShowHint($"已跳到「{cur.Subject}」下课后 3 分钟（课间休息）");
        RefreshStatus();
    }

    /// <summary>快上课：跳到下节课开始前 5 分钟（触发 NextClassSoon）</summary>
    private void JumpNextSoon_Click(object? sender, RoutedEventArgs e)
    {
        var next = App.Schedule.GetNextEntry(TimeSimulator.Now);
        if (next == null) { ShowHint("⚠ 今天没有下一节课了"); return; }
        var target = next.GetStartDateTime(TimeSimulator.Now.Date).TimeOfDay - TimeSpan.FromMinutes(5);
        TimeSimulator.JumpTo(target);
        ShowHint($"已跳到「{next.Subject}」课前 5 分钟");
        RefreshStatus();
    }

    /// <summary>自定义偏移（分钟，可正可负）</summary>
    private async void CustomOffset_Click(object? sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "例如：5 表示快进 5 分钟，-10 表示回退 10 分钟" };
        var dlg = new Window
        {
            Title = "自定义时间偏移",
            Icon = App.AppIcon,
            Width = 320, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "输入分钟数（正=快进，负=回退）：", FontSize = 12 },
                    box,
                    new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 72 }
                }
            }
        };
        ((Button)((StackPanel)dlg.Content).Children[2]).Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);

        if (int.TryParse(box.Text, out int minutes))
        {
            TimeSimulator.SetOffset(TimeSpan.FromMinutes(minutes));
            ShowHint($"已设置偏移 {minutes} 分钟");
            RefreshStatus();
        }
    }

    private void ResetBtn_Click(object? sender, RoutedEventArgs e)
    {
        TimeSimulator.Reset();
        ShowHint("已恢复实时时间");
        RefreshStatus();
    }
}
