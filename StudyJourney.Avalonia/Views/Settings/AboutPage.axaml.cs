using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        VersionTb.Text = $"版本 {UpdateService.CurrentVersion}";
    }

    public void Apply(AppSettings s)
    {
        s.AutoCheckUpdate = AutoCheckUpdateCheck.IsChecked == true;
    }

    private void GitHubRepoBtn_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo($"https://github.com/{RepoOwner}/{RepoName}") { UseShellExecute = true }); }
        catch (Exception ex) { AppLogger_Error("打开 GitHub 失败", ex); }
    }

    private async void CheckUpdateBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var old = btn.Content;
            btn.Content = "检查中…";
            btn.IsEnabled = false;
            try
            {
                var info = await UpdateService.CheckAsync(RepoOwner, RepoName);
                if (info.HasUpdate)
                {
                    UpdateStatusTb.Text = $"发现新版本 v{info.LatestVersion}（当前 v{UpdateService.CurrentVersion}）。\n" +
                                          $"{(info.IsSelfContained ? "自包含版" : "框架依赖版")}可下载。";
                    UpdateStatusTb.IsVisible = true;
                }
                else
                {
                    UpdateStatusTb.Text = $"已是最新版本 v{UpdateService.CurrentVersion}。";
                    UpdateStatusTb.IsVisible = true;
                }
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

    private static void AppLogger_Error(string msg, Exception ex)
        => Helpers.AppLogger.Error(msg, ex);
}
