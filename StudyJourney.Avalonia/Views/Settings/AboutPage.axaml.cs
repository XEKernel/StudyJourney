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

    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>页面加载时由 SettingsWindow 调用</summary>
    public void Load(AppSettings s)
    {
        AutoCheckUpdateCheck.IsChecked = s.AutoCheckUpdate;

        UseProxyCheck.IsChecked = s.UpdateUseProxy;
        ProxyPrefixBox.Text = s.UpdateProxyPrefix;
        ProxyBoxPanel.IsVisible = s.UpdateUseProxy;

        // 诊断与记录（2026-09-24）
        RecordActivityCheck.IsChecked = s.RecordActivity;

        var ver = UpdateService.CurrentVersion;
        bool isPre = System.Text.RegularExpressions.Regex.IsMatch(
            ver, @"(alpha|beta|rc|pre)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        VersionTb.Text = isPre ? $"版本 {ver}（预发布测试版）" : $"版本 {ver}";
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
    }

    /// <summary>镜像地址清空时给回默认值，避免老师误删后更新直接失败</summary>
    private void ProxyPrefixBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ProxyPrefixBox == null) return;
        var t = ProxyPrefixBox.Text?.Trim() ?? "";
        if (t.Length == 0 && UseProxyCheck.IsChecked == true)
            ProxyPrefixBox.Text = UpdateService.DefaultProxyPrefix;
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
                var mode = info.IsSelfContained ? "自包含版" : "框架依赖版";
                UpdateStatusTb.Text = $"发现新版本 v{info.LatestVersion}（当前 v{UpdateService.CurrentVersion}）。\n" +
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
