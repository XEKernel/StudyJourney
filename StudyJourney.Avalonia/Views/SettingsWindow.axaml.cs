using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views.Settings;

namespace StudyJourney.Avalonia.Views;

/// <summary>WinUI 3 风格设置窗口：Mica + NavigationView 导航 + 6 Tab（对齐学程原版）+ 保存到 settings.json</summary>
public partial class SettingsWindow : FluentAvalonia.UI.Windowing.FAAppWindow, IUnsavedWork
{
    private Control? _currentPage;

    // S3 修复：全局"未保存修改"快照 —— 7 个页面未实现 IsDirty（滑块/勾选改动切页或关窗会被静默丢弃），
    // 这里用 AppSettings JSON 快照兜底检测；每次 Load/保存/重置/切页后刷新基线
    private string _baselineJson = "";

    private static string SerializeSettings()
        => JsonSerializer.Serialize(App.Settings, AppJsonContext.Default.AppSettings);

    private bool HasUnsavedSettings() => SerializeSettings() != _baselineJson;

    private void RefreshBaseline() => _baselineJson = SerializeSettings();

    public SettingsWindow()
    {
        InitializeComponent();
        Helpers.WindowBackdropHelper.EnsureBackground(this);   // Win10 无 Mica → 降级不透明背景
        Icon = LoadBitmapIcon();   // FAAppWindow.Icon 是 IImage，需用 PNG（Bitmap 不支持 ico）
        Closing += OnClosing;      // #8：关窗前检查未保存修改
        // 默认显示倒计时页（含数据加载）
        ShowPage(new CountdownPage());
    }

    /// <summary>#8 修复：允许关窗的标记（用户已在确认框选择保存/放弃后置位，避免二次拦截）</summary>
    private bool _closeConfirmed;

    /// <summary>#8 修复：关窗前若当前页有未保存修改 → 三选一（保存并关闭 / 放弃修改 / 取消）</summary>
    /// <summary>设置窗口有未保存的修改（自动更新重启前要问，见 IUnsavedWork）</summary>
    public bool HasUnsavedWork =>
        !_closeConfirmed &&
        ((_currentPage is ISettingsPage sp && sp.IsDirty) || HasUnsavedSettings());

    public string UnsavedWorkHint => "设置页有未保存的修改";

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        bool pageDirty = _currentPage is ISettingsPage sp && sp.IsDirty;
        if (!pageDirty && !HasUnsavedSettings()) return;   // S3：快照兜底，页面未实现 IsDirty 也能拦下

        e.Cancel = true;   // 先拦下，等用户选择后再真正关闭
        var choice = await Helpers.DialogHelper.ShowChoiceAsync(this,
            "未保存的修改", "当前页面有未保存的修改，关闭前如何处理？",
            "保存并关闭", "放弃修改", "取消");
        if (choice == 1)
        {
            if (_currentPage is ISettingsPage sp2) sp2.Apply(App.Settings);
            App.SaveSettings();
            RefreshBaseline();
            _closeConfirmed = true;
            Close();
        }
        else if (choice == 2) { RefreshBaseline(); _closeConfirmed = true; Close(); }
        // choice == 0 → 留在本页
    }

    /// <summary>FAAppWindow 标题栏图标（IImage，用 PNG）</summary>
    private static IImage? LoadBitmapIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.png"));
            return new Bitmap(stream);
        }
        catch { return null; }
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
            "server"   => new ServerPage(),
            "automation" => new AutomationPage(),
            "about"    => new AboutPage(),
            _          => new CountdownPage()
        });
    }

    /// <summary>切换页面：#8 修复 —— 旧页有未保存修改时先三选一（保存并切换/放弃修改/留在本页），
    /// 避免切页静默丢失；然后 Load 当前设置 + 滑动淡入动画（渲染线程驱动，可用「页面动画」开关关闭）</summary>
    private async void ShowPage(Control page)
    {
        bool unsavedBefore = HasUnsavedSettings();
        if (_currentPage is ISettingsPage oldPage && oldPage != page && (oldPage.IsDirty || unsavedBefore))
        {
            var choice = await Helpers.DialogHelper.ShowChoiceAsync(this,
                "未保存的修改", "当前页面有未保存的修改，如何处理？",
                "保存并切换", "放弃修改", "留在本页");
            if (choice == 0) return;                                  // 留在本页，不切换
            if (choice == 1) { oldPage.Apply(App.Settings); App.SaveSettings(); }  // 保存并切换
            // choice == 2（放弃修改）→ 直接切换，由新页 Load 覆盖
        }

        _currentPage = page;
        if (page is ISettingsPage sp) sp.Load(App.Settings);
        RefreshBaseline();   // 新页已载入 → 以当前设置为新基线

        PageHost.Child = page;

        if (PageAnimationsCheck.IsChecked != true)
        {
            page.Opacity = 1;
            page.RenderTransform = null;
            return;
        }

        // 滑动 + 淡入：新页面从右往左滑入（CubicEaseOut 缓出，非线性）
        var tt = new TranslateTransform(28, 0);
        page.RenderTransform = tt;
        page.Opacity = 0;

        var easing = new CubicEaseOut();
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.XProperty, 28d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.XProperty, 0d) } }
            }
        };
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        };

        try
        {
            await Task.WhenAll(slide.RunAsync(tt), fade.RunAsync(page));
        }
        catch { /* 动画失败则直接显示 */ }

        page.RenderTransform = null;
        page.Opacity = 1;
    }

    /// <summary>恢复默认设置（对齐 WPF ResetButton_Click）：重置为 new AppSettings() 并广播刷新。
    ///
    /// 2026-09-25（规划 2.7 ①）重做，起因是"按下去不知道会清掉什么"（数据安全问题）：
    ///  · 原确认框只有一句"将恢复默认：外观 / 位置 / 提醒 / 考试模式等偏好"，
    ///    实际却会**清空老师账号、删除自定义倒计时、重置选科、抹掉远程控制台配置** —— 真正的业务数据；
    ///  · 原话术里"重置老师账号为内置账号"还**不准确**：`new AppSettings().Teachers` 是**空表**，
    ///    重置后账号列表为空（老师登录会回落到内置账号），并非"列表里有内置账号"（已按实际行为改文案）。
    /// 现在：清单由 `SettingsReset` 按**当前设置的真实内容**生成（带数量），
    /// 动手前**自动备份 settings.json**（可反悔），并要求老师勾选确认才能按。
    /// </summary>
    private async void ResetBtn_Click(object? sender, RoutedEventArgs e)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string settingsPath = System.IO.Path.Combine(baseDir, "settings.json");

        // 先把当前页的改动落盘，再备份 —— 这样备份里是"重置前的完整设置"，不是缺一块的设置
        if (_currentPage is ISettingsPage cur) cur.Apply(App.Settings);
        App.SaveSettings();

        // 预先把时间戳定下来：清单里展示的备份路径 == 备份真正写到的路径
        var now = DateTime.Now;
        string previewPath = System.IO.Path.Combine(
            Helpers.SettingsReset.BackupRoot(baseDir),
            "reset-" + now.ToString("yyyyMMdd_HHmmss"),
            "settings.json");

        var dlg = new ResetSettingsDialog(
            Helpers.SettingsReset.DescribeImpact(App.Settings, previewPath));
        bool ok = await dlg.ShowDialog<bool>(this);
        if (!ok) return;

        // ① 备份（失败也继续重置，但必须明确告诉老师"这次没有备份"）
        string? backupPath = Helpers.SettingsReset.Backup(settingsPath, baseDir, now, out var backupError);

        // ② 重置
        App.Settings = new AppSettings();
        App.SaveSettings();

        if (_currentPage is ISettingsPage sp) sp.Load(App.Settings);
        RefreshBaseline();

        Helpers.AppLogger.Info($"[恢复默认] 已重置全部设置；备份：{backupPath ?? "（未生成：" + backupError + "）"}");

        // ③ 告诉老师结果 + 备份在哪（能直接打开）
        string msg = backupPath != null
            ? $"已恢复默认设置。\n\n重置前的设置已备份到：\n{backupPath}\n\n" +
              "如需找回，用「课表 → 恢复数据」选中这个文件即可。"
            : $"已恢复默认设置。\n\n⚠ 但重置前**未能自动备份**：{backupError}\n" +
              "（设置已重置，此次无法找回之前的内容）";

        var choice = await Helpers.DialogHelper.ShowChoiceAsync(this, "恢复默认设置", msg,
            "打开备份文件夹", "知道了");
        if (choice == 1)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(backupPath)
                          ?? Helpers.SettingsReset.BackupRoot(baseDir);
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex) { Helpers.AppLogger.Warn($"打开备份文件夹失败：{ex.Message}"); }
        }
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage is ISettingsPage sp)
        {
            sp.Apply(App.Settings);
            App.SaveSettings();   // 保存并通知主窗口刷新
            RefreshBaseline();    // S3：保存后对齐基线
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
