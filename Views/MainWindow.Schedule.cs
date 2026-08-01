using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using GaokaoCountdown.Helpers;
using Hardcodet.Wpf.TaskbarNotification;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class MainWindow : Window
    {
        // ── 课表服务初始化 ─────────────────────────────────────
        private void SetupScheduleServices()
        {
            // 加载课表（损坏时备份并提示用户）
            try { _scheduleManager = new ScheduleManager(); }
            catch (Exception ex)
            {
                _scheduleManager = new ScheduleManager(); // 重试（已自动备份，第二次不读文件）
                MessageBox.Show(ex.Message, "学程 — 课表恢复",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }

            _reminderService = new ReminderService(_scheduleManager, settings);

            // 订阅提醒事件
            _reminderService.Reminder += OnReminder;

            if (settings.EnableExamMode || settings.RemindClassStart || settings.ShowScheduleBar)
                _reminderService.Start();

            // 初始化课表栏
            if (settings.ShowScheduleBar)
                ShowScheduleBarWindow();

            // 当天有考试且开启自动进入时，延迟 2 秒进入
            if (settings.AutoEnterExamMode && settings.EnableExamMode)
            {
                var todayExams = _scheduleManager.GetTodayExams();
                if (todayExams.Count > 0)
                {
                    var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    delay.Tick += (s, e) => { delay.Stop(); EnterExamMode(); };
                    delay.Start();
                }
            }

            // 自动检查更新（延迟 5 秒，不阻塞启动）
            if (settings.AutoCheckUpdate)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try
                    {
                        var info = await UpdateService.CheckAsync("XEKernel", "StudyJourney");
                        if (info.HasUpdate)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var mode = info.IsSelfContained ? "自包含版" : "框架依赖版";
                                var r = MessageBox.Show(
                                    $"新版本 v{info.LatestVersion} 可用！（当前 v{UpdateService.CurrentVersion}）\n" +
                                    $"将自动下载 {mode}\n\n是否立即更新？",
                                    "学程 — 发现新版本",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Information);
                                if (r == MessageBoxResult.Yes)
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        var result = await UpdateService.StartUpdateAsync(info.DownloadUrl,
                                            Environment.ProcessId);
                                        if (result) Environment.Exit(0);
                                    });
                                }
                            });
                        }
                    }
                    catch { /* 网络不可用，静默 */ }
                });
            }
        }

        private void OnReminder(object? sender, ReminderEventArgs e)
        {
            // 课表栏自己处理下课倒计时/提示，不再弹右下角提醒窗
            _scheduleBarWindow?.ExpandOnReminder(e.Type);
        }

        // ── 课表栏窗口管理 ────────────────────────────────────
        private void ShowScheduleBarWindow()
        {
            if (_scheduleBarWindow != null) return;
            if (_scheduleManager == null || _reminderService == null) return;
            _scheduleBarWindow = new ScheduleBarWindow(settings, _scheduleManager, _reminderService);
            _scheduleBarWindow.Closed += (s, e) => { _scheduleBarWindow = null; SyncTrayMenu(); };
            _scheduleBarWindow.Show();
            SyncTrayMenu();
        }

        private void HideScheduleBarWindow()
        {
            _scheduleBarWindow?.Close();
            _scheduleBarWindow = null;
            SyncTrayMenu();
        }

        private void SyncTrayMenu()
        {
            if (_trayScheduleItem != null)
                _trayScheduleItem.Header = _scheduleBarWindow != null ? "课表栏 ✓" : "课表栏";
        }

        /// <summary>设置窗口应用设置后调用，刷新课表栏状态</summary>
        public void ApplyScheduleBarSettings()
        {
            // 重启提醒服务（开关可能变化）
            _reminderService?.Stop();
            if (settings.EnableExamMode || settings.RemindClassStart ||
                settings.ShowScheduleBar || settings.RemindClassEnd)
                _reminderService?.Start();

            if (settings.ShowScheduleBar)
            {
                if (_scheduleBarWindow == null)
                    ShowScheduleBarWindow();
                else
                {
                    _scheduleBarWindow.ApplySettings();
                    _scheduleBarWindow.ApplyFontSizes();
                }
            }
            else
            {
                HideScheduleBarWindow();
            }
        }

        // ── 考试模式 ──────────────────────────────────────────
        public void EnterExamMode()
        {
            if (_examModeWindow != null) { _examModeWindow.Activate(); return; }
            if (_scheduleManager == null) return;

            // 检查今天是否有考试
            var todayExams = _scheduleManager.GetTodayExams();
            if (todayExams.Count == 0)
            {
                MessageBox.Show("今天没有安排考试。", "考试模式",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // 检查当前是否在考试时段内（正在考或有下一场未考）
            var now = DateTime.Now;
            var cur  = _scheduleManager.GetCurrentExamSubject(now);
            var next = _scheduleManager.GetNextExamSubject(now);
            if (cur == null && next == null)
            {
                MessageBox.Show("今天的考试已全部结束。", "考试模式",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // 进入考试模式时隐藏课表栏
            if (settings.ShowScheduleBar)
                HideScheduleBarWindow();
            _examModeWindow = new ExamModeWindow(_scheduleManager, settings);
            _examModeWindow.Closed += (s, e) =>
            {
                _examModeWindow = null;
                // 考试窗口关闭时恢复课表栏
                if (settings.ShowScheduleBar && _scheduleBarWindow == null)
                    ShowScheduleBarWindow();
            };
            _examModeWindow.Show();
        }

        public void ExitExamMode()
        {
            _examModeWindow?.Close();
            _examModeWindow = null;
            // 退出考试模式时恢复课表栏
            if (settings.ShowScheduleBar)
                ShowScheduleBarWindow();
        }

        public void RefreshDateFields()
        {
            if (!DateTime.TryParse(settings.GaokaoDateStr, out gaokaoDate))
                gaokaoDate = new DateTime(2027, 6, 7, 9, 0, 0);
            if (!DateTime.TryParse(settings.StartDateStr, out startDate))
                startDate = new DateTime(2024, 8, 24);
        }
    }
}
