using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Services;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class AboutPage : UserControl, ISettingsPage
{
    private const string RepoOwner = "XEKernel";
    private const string RepoName = "StudyJourney";

    /// <summary>本页有未保存修改（#8 契约）。
    /// ⚠ 2026-10-10 补：本页此前**没有实现 IsDirty** → 用接口默认的 false，
    ///   而「启动时自动检查更新 / 加速镜像开关 / 镜像前缀」三项**只在 Apply 时**才写回 AppSettings，
    ///   设置窗口的兜底 `HasUnsavedSettings()`（比 `App.Settings` 序列化）因此看不出控件里的改动
    ///   → **改完切页/关窗会静默丢弃**。与 ServerPage 早已修过的坑完全同源。</summary>
    private bool _dirty;

    /// <summary>Load 期间给控件赋初值会触发 Changed → 必须挡住，否则一进页面就被判成「已修改」</summary>
    private bool _ready;

    public bool IsDirty => _dirty;

    private void MarkDirty()
    {
        if (_ready) _dirty = true;
    }

    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>页面加载时由 SettingsWindow 调用</summary>
    public void Load(AppSettings s)
    {
        _ready = false;
        try
        {
            AutoCheckUpdateCheck.IsChecked = s.AutoCheckUpdate;

            UseProxyCheck.IsChecked = s.UpdateUseProxy;
            ProxyPrefixBox.Text = s.UpdateProxyPrefix;
            ProxyBoxPanel.IsVisible = s.UpdateUseProxy;

            // 诊断与记录（2026-09-24）
            RecordActivityCheck.IsChecked = s.RecordActivity;

            // 诊断包选项：回显（用 _loadingDiagOptions 挡住赋值自身触发的事件，免得 Load 时就写盘）
            _loadingDiagOptions = true;
            try
            {
                int idx = Array.IndexOf(DiagDepths, s.DiagDesktopTreeDepth);
                DiagDepthCombo.SelectedIndex = idx >= 0 ? idx : 1;      // 认不出就用「5 层」
                DiagCoursewareTreeCheck.IsChecked = s.DiagIncludeCoursewareTree;
                DiagExtraDirsBox.Text = s.DiagExtraDirs ?? "";
            }
            finally { _loadingDiagOptions = false; }

            var ver = UpdateService.CurrentVersion;
            bool isPre = System.Text.RegularExpressions.Regex.IsMatch(
                ver, @"(alpha|beta|rc|pre)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            VersionTb.Text = isPre ? $"版本 {ver}（预发布测试版）" : $"版本 {ver}";
        }
        finally
        {
            _ready = true;
            _dirty = false;   // 刚载入 = 干净，与新基线对齐
        }
    }

    public void Apply(AppSettings s)
    {
        s.AutoCheckUpdate = AutoCheckUpdateCheck.IsChecked == true;
        s.UpdateUseProxy = UseProxyCheck.IsChecked == true;
        s.UpdateProxyPrefix = ProxyPrefixBox.Text?.Trim() ?? "";

        // 立即生效：设置页保存后不用重启就按新通道下载
        UpdateService.ProxyPrefix = s.UpdateUseProxy ? s.UpdateProxyPrefix : "";

        // 诊断与记录：开关立即生效（开 → 马上开始记录；关 → 停止）
        s.RecordActivity = RecordActivityCheck.IsChecked == true;
        if (s.RecordActivity) Services.ActivityRecorder.Start();
        else Services.ActivityRecorder.Stop();

        // 诊断包选项也一并写回（即时生效那条路径之外的双保险）
        s.DiagDesktopTreeDepth = SelectedDepth();
        s.DiagIncludeCoursewareTree = DiagCoursewareTreeCheck.IsChecked != false;
        s.DiagExtraDirs = DiagExtraDirsBox.Text ?? "";

        _dirty = false;   // 已写回设置 → 干净（否则关窗还会再问一次）
    }

    // ── 未保存标记（#8）：三项「只在 Apply 时写回」的控件必须各自通知 ──
    private void AutoCheckUpdate_Changed(object? sender, RoutedEventArgs e) => MarkDirty();

    // ── 诊断包选项（2026-09-24）─────────────────────────────
    // 这几个值只是"生成诊断包时的参数"，改了就该生效 —— 让老师还要记得点「保存」才生效，
    // 是最容易出错的设计。所以这里改动即写盘（与 RecordActivity 同一思路）。
    private static readonly int[] DiagDepths = { 3, 5, 8, 12 };
    private bool _loadingDiagOptions;

    private int SelectedDepth() => DiagDepths[Math.Clamp(DiagDepthCombo.SelectedIndex, 0, DiagDepths.Length - 1)];

    private void DiagOption_Changed(object? sender, RoutedEventArgs e) => SaveDiagOptions();

    private void DiagExtraDirs_LostFocus(object? sender, RoutedEventArgs e) => SaveDiagOptions();

    private void SaveDiagOptions()
    {
        // Load 期间给控件赋初值也会触发事件 → 必须挡住，否则一进页面就写一次设置
        if (_loadingDiagOptions) return;
        try
        {
            App.Settings.DiagDesktopTreeDepth = SelectedDepth();
            App.Settings.DiagIncludeCoursewareTree = DiagCoursewareTreeCheck.IsChecked != false;
            App.Settings.DiagExtraDirs = DiagExtraDirsBox.Text ?? "";
            App.SaveSettings();
            Helpers.AppLogger.Info($"[诊断包] 选项已更新：深度 {App.Settings.DiagDesktopTreeDepth}、" +
                                   $"课件目录树 {(App.Settings.DiagIncludeCoursewareTree ? "开" : "关")}、" +
                                   $"额外目录 {App.Settings.DiagExtraDirs.Length} 字符");
        }
        catch (Exception ex) { Helpers.AppLogger.Warn($"[诊断包] 保存选项失败：{ex.Message}"); }
    }

    private void RecordActivity_Changed(object? sender, RoutedEventArgs e)
    {
        // 勾选/取消立即生效，不必等"保存"——记录开关是行为开关，用户预期是马上生效
        try
        {
            bool on = RecordActivityCheck.IsChecked == true;
            if (on) Services.ActivityRecorder.Start(); else Services.ActivityRecorder.Stop();
        }
        catch (Exception ex) { Helpers.AppLogger.Warn($"[记录] 切换开关失败: {ex.Message}"); }
    }

    /// <summary>
    /// 生成诊断包（2026-09-24）：把活动记录 / 课件清单（带解析序号）/ 桌面文件树 /
    /// 配置快照（脱敏）/ 系统信息 / 日志尾部打成一个 zip 放到桌面。
    /// ⚠ 同步做会卡 UI（要遍历桌面与课件目录）→ 丢到后台线程。
    /// </summary>
    private async void DiagPackBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DiagPackBtn.IsEnabled = false;
            DiagStatusTb.Text = "正在收集…（桌面文件多时可能要几秒）";

            string? zip = await System.Threading.Tasks.Task.Run(() => Services.DiagnosticPackager.Create());

            DiagStatusTb.Text = zip == null
                ? "生成失败，详情见 logs/app.log"
                : $"已生成到桌面：{System.IO.Path.GetFileName(zip)}";
            Helpers.AppLogger.Info($"[诊断包] 用户操作结果: {zip ?? "失败"}");
        }
        catch (Exception ex)
        {
            DiagStatusTb.Text = "生成失败：" + ex.Message;
            Helpers.AppLogger.Error("[诊断包] 生成异常", ex);
        }
        finally
        {
            DiagPackBtn.IsEnabled = true;
        }
    }

    private void UseProxyCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (ProxyBoxPanel == null) return;
        ProxyBoxPanel.IsVisible = UseProxyCheck.IsChecked == true;
        MarkDirty();   // #8：这一项只在 Apply 时写回 → 必须记脏，否则切页静默丢弃
    }

    /// <summary>镜像地址清空时给回默认值，避免老师误删后更新直接失败</summary>
    private void ProxyPrefixBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ProxyPrefixBox == null) return;
        var t = ProxyPrefixBox.Text?.Trim() ?? "";
        if (t.Length == 0 && UseProxyCheck.IsChecked == true)
            ProxyPrefixBox.Text = UpdateService.DefaultProxyPrefix;
        MarkDirty();   // #8：同上（递归只发生一次：补完默认值后文本非空）
    }

    private void GitHubRepoBtn_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo($"https://github.com/{RepoOwner}/{RepoName}") { UseShellExecute = true }); }
        catch (Exception ex) { Helpers.AppLogger.Error("打开 GitHub 失败", ex); }
    }

    private async void CheckUpdateBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var old = btn.Content;
        btn.Content = "检查中…";
        btn.IsEnabled = false;
        try
        {
            var info = await UpdateService.CheckAsync(RepoOwner, RepoName);

            if (info.HasUpdate)
            {
                // ⚠ 用三态显示名（2026-10-01）：原来的二选一在 AOT 版上会显示成「框架依赖版」（错的）
                var mode = UpdateService.FormDisplayName(info.Form);

                // 必经门槛版本（2026-10-01）：本次实际装的是门槛版本，装好后会自动再升到最新
                UpdateStatusTb.Text = info.WaypointVersion.Length > 0
                    ? $"发现新版本 v{info.LatestVersion}（当前 v{UpdateService.CurrentVersion}）。\n" +
                      $"按升级要求，将**先安装 v{info.WaypointVersion}**（必要的中间版本），随后自动升级到 v{info.LatestVersion}。"
                    : $"发现新版本 v{info.LatestVersion}（当前 v{UpdateService.CurrentVersion}）。\n" +
                      $"{mode}可下载。";
                UpdateStatusTb.IsVisible = true;

                // 原来这里只显示文字、没有任何办法真的更新（只有启动时的自动检查能更新）。
                // 手动检查出来后直接问一句，让"手动检查"也能完成更新。
                btn.Content = old;
                btn.IsEnabled = true;

                bool ok = await Helpers.DialogHelper.ShowConfirmAsync(
                    TopLevel.GetTopLevel(this) as Window,
                    "学程 — 发现新版本",
                    $"新版本 v{info.LatestVersion} 可用！（当前 v{UpdateService.CurrentVersion}）\n" +
                    $"将自动下载 {mode}\n\n是否立即更新？",
                    "立即更新", "稍后");
                if (ok) await App.RunUpdateWithWindowAsync(info);
                return;
            }

            UpdateStatusTb.Text = $"已是最新版本 v{UpdateService.CurrentVersion}。";
            UpdateStatusTb.IsVisible = true;
        }
        catch (Exception ex)
        {
            UpdateStatusTb.Text = $"检查更新失败：{ex.Message}";
            UpdateStatusTb.IsVisible = true;
        }
        finally
        {
            btn.Content = old;
            btn.IsEnabled = true;
        }
    }
}
