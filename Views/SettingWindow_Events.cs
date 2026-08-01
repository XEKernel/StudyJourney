using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class SettingWindow : Window
    {
        // ══════════════════════════════════════════════════════
        //  控件事件
        // ══════════════════════════════════════════════════════

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeText != null)
                FontSizeText.Text = ((int)FontSizeSlider.Value).ToString();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityText != null)
                OpacityText.Text = $"{OpacitySlider.Value * 100:F0}%";
        }

        private void DecimalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DecimalText != null)
                DecimalText.Text = ((int)DecimalSlider.Value).ToString();
        }

        private void QuoteFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QuoteFontSizeText != null)
                QuoteFontSizeText.Text = ((int)QuoteFontSizeSlider.Value).ToString();
        }

        private void QuoteRefreshIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QuoteRefreshIntervalText != null)
            {
                int val = (int)QuoteRefreshIntervalSlider.Value;
                QuoteRefreshIntervalText.Text = val == 0 ? "关" : $"{val}s";
            }
        }

        private void WeatherRefreshIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WeatherRefreshIntervalText != null)
            {
                int val = (int)WeatherRefreshIntervalSlider.Value;
                WeatherRefreshIntervalText.Text = val == 0 ? "关" : $"{val}min";
            }
        }

        private void WeatherFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (WeatherFontSizeText != null)
                WeatherFontSizeText.Text = $"{(int)WeatherFontSizeSlider.Value}";
        }

        private void ScheduleBarFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScheduleBarFontSizeText != null)
                ScheduleBarFontSizeText.Text = $"{(int)ScheduleBarFontSizeSlider.Value}";
        }

        private void ExamModeFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ExamModeFontSizeText != null)
                ExamModeFontSizeText.Text = $"{(int)ExamModeFontSizeSlider.Value}";
        }

        private void ExamSubjectFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamSubjectFontSizeText != null) ExamSubjectFontSizeText.Text = $"{(int)ExamSubjectFontSizeSlider.Value}"; }
        private void ExamNameFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamNameFontSizeText != null) ExamNameFontSizeText.Text = $"{(int)ExamNameFontSizeSlider.Value}"; }
        private void ExamCountdownFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamCountdownFontSizeText != null) ExamCountdownFontSizeText.Text = $"{(int)ExamCountdownFontSizeSlider.Value}"; }
        private void ExamTimeInfoFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamTimeInfoFontSizeText != null) ExamTimeInfoFontSizeText.Text = $"{(int)ExamTimeInfoFontSizeSlider.Value}"; }
        private void ExamNextSubjectFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamNextSubjectFontSizeText != null) ExamNextSubjectFontSizeText.Text = $"{(int)ExamNextSubjectFontSizeSlider.Value}"; }
        private void ExamWarningFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamWarningFontSizeText != null) ExamWarningFontSizeText.Text = $"{(int)ExamWarningFontSizeSlider.Value}"; }
        private void ExamEscHintFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamEscHintFontSizeText != null) ExamEscHintFontSizeText.Text = $"{(int)ExamEscHintFontSizeSlider.Value}"; }
        private void ExamProgressBarHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { if (ExamProgressBarHeightText != null) ExamProgressBarHeightText.Text = $"{(int)ExamProgressBarHeightSlider.Value}"; }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void PosCustom_Checked(object sender, RoutedEventArgs e)
        {
            if (CustomPosPanel != null)
            {
                CustomPosPanel.IsEnabled = true;
                CustomPosPanel.Opacity   = 1.0;
            }
        }

        private void PosCustom_Unchecked(object sender, RoutedEventArgs e)
        {
            if (CustomPosPanel != null)
            {
                CustomPosPanel.IsEnabled = false;
                CustomPosPanel.Opacity   = 0.5;
            }
        }

        // ── 颜色输入实时预览 ──────────────────────────────────
        private void NumberColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(NumberColorBox, NumberColorPreview);

        private void TextColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(TextColorBox, TextColorPreview);

        private void ProgressBarColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ProgressBarColorBox, ProgressBarColorPreview);

        private void QuoteForegroundBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(QuoteForegroundBox, QuoteForegroundPreview);

        private void WeatherCityColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(WeatherCityColorBox, WeatherCityColorPreview);

        private void WeatherInfoColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(WeatherInfoColorBox, WeatherInfoColorPreview);

        private void WeatherTempColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(WeatherTempColorBox, WeatherTempColorPreview);

        private void WeatherTimeColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(WeatherTimeColorBox, WeatherTimeColorPreview);

        private void WeatherIconColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(WeatherIconColorBox, WeatherIconColorPreview);

        private void ExamSubjectColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamSubjectColorBox, ExamSubjectColorPreview);
        private void ExamNameColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamNameColorBox, ExamNameColorPreview);
        private void ExamCountdownNormalColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamCountdownNormalColorBox, ExamCountdownNormalColorPreview);
        private void ExamCountdownWarningColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamCountdownWarningColorBox, ExamCountdownWarningColorPreview);
        private void ExamCountdownCriticalColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamCountdownCriticalColorBox, ExamCountdownCriticalColorPreview);
        private void ExamDistanceColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamDistanceColorBox, ExamDistanceColorPreview);
        private void ExamInfoColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamInfoColorBox, ExamInfoColorPreview);
        private void ExamProgressBarColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamProgressBarColorBox, ExamProgressBarColorPreview);
        private void ExamBackgroundColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamBackgroundColorBox, ExamBackgroundColorPreview);
        private void ExamProgressBarBgColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamProgressBarBgColorBox, ExamProgressBarBgColorPreview);
        private void ExamNextSubjectColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamNextSubjectColorBox, ExamNextSubjectColorPreview);
        private void ExamWarningColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamWarningColorBox, ExamWarningColorPreview);
        private void ExamProgressPctColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamProgressPctColorBox, ExamProgressPctColorPreview);
        private void ExamInfoDimColorBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshColorPreview(ExamInfoDimColorBox, ExamInfoDimColorPreview);

        // ── 颜色选择对话框 ────────────────────────────────────
        private void SelectNumberColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(NumberColorBox.Text, out Color picked))
            {
                NumberColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(NumberColorBox, NumberColorPreview);
            }
        }

        private void SelectTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(TextColorBox.Text, out Color picked))
            {
                TextColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(TextColorBox, TextColorPreview);
            }
        }

        private void SelectProgressBarColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(ProgressBarColorBox.Text, out Color picked))
            {
                ProgressBarColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(ProgressBarColorBox, ProgressBarColorPreview);
            }
        }

        private void SelectQuoteForeground_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(QuoteForegroundBox.Text, out Color picked))
            {
                QuoteForegroundBox.Text = ColorToHex(picked);
                RefreshColorPreview(QuoteForegroundBox, QuoteForegroundPreview);
            }
        }

        private void SelectWeatherCityColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(WeatherCityColorBox.Text, out Color picked))
            {
                WeatherCityColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(WeatherCityColorBox, WeatherCityColorPreview);
            }
        }

        private void SelectWeatherInfoColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(WeatherInfoColorBox.Text, out Color picked))
            {
                WeatherInfoColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(WeatherInfoColorBox, WeatherInfoColorPreview);
            }
        }

        private void SelectWeatherTempColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(WeatherTempColorBox.Text, out Color picked))
            {
                WeatherTempColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(WeatherTempColorBox, WeatherTempColorPreview);
            }
        }

        private void SelectWeatherTimeColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(WeatherTimeColorBox.Text, out Color picked))
            {
                WeatherTimeColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(WeatherTimeColorBox, WeatherTimeColorPreview);
            }
        }

        private void SelectWeatherIconColor_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(WeatherIconColorBox.Text, out Color picked))
            {
                WeatherIconColorBox.Text = ColorToHex(picked);
                RefreshColorPreview(WeatherIconColorBox, WeatherIconColorPreview);
            }
        }

        /// <summary>通用考试颜色选择（通过 Button.Tag 指定 TextBox 名称）</summary>
        private void SelectExamColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string boxName)
            {
                var box = FindName(boxName) as TextBox;
                if (box == null) return;
                if (PickColor(box.Text, out Color picked))
                {
                    box.Text = ColorToHex(picked);
                    // 预览 Rectangle 名称为 XxxColorPreview（去掉 Box 后缀 + Preview）
                    var prevName = boxName.Substring(0, boxName.Length - 3) + "Preview";
                    var preview = FindName(prevName) as System.Windows.Shapes.Rectangle;
                    if (preview != null) RefreshColorPreview(box, preview);
                }
            }
        }

        // ── 拖动窗口 ──────────────────────────────────────────

        // ── 拖动窗口 ──────────────────────────────────────────
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox
                || e.OriginalSource is ComboBox)
                return;

            // 沿可视化树向上查找：如果点击位于 ScrollBar 内部
            //（Thumb/RepeatButton/Track 等模板子元素），让 ScrollBar 自行处理
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                if (current is ScrollBar)
                    return;
                current = VisualTreeHelper.GetParent(current);
            }

            DragMove();
        }

        private void RefreshCustomCountdownGrid()
        {
            CustomCountdownGrid.ItemsSource = null;
            CustomCountdownGrid.ItemsSource = _mainWindow.CustomCountdowns;
        }

        private void AddCustomCountdown_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.CustomCountdowns.Add(new CustomCountdown { Name = "新目标", DateStr = "2027-01-01" });
            _mainWindow.SaveSettings();
            RefreshCustomCountdownGrid();
        }

        private void DeleteCustomCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (CustomCountdownGrid.SelectedItem is not CustomCountdown cc) return;
            _mainWindow.CustomCountdowns.Remove(cc);
            _mainWindow.SaveSettings();
            RefreshCustomCountdownGrid();
        }

        // ══════════════════════════════════════════════════════
        //  自动更新检查
        // ══════════════════════════════════════════════════════

        private async void CheckUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                var info = await UpdateService.CheckAsync("XEKernel", "StudyJourney");
                if (info.HasUpdate)
                {
                    var modeText = info.IsSelfContained ? "自包含版" : "框架依赖版";
                    var r = MessageBox.Show(
                        $"发现新版本 v{info.LatestVersion}！（当前 v{UpdateService.CurrentVersion}）\n" +
                        $"将自动下载 {modeText}\n\n是否立即更新？",
                        "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (r == MessageBoxResult.Yes)
                    {
                        if (btn != null) btn.IsEnabled = false;
                        var result = await UpdateService.StartUpdateAsync(info.DownloadUrl,
                            Environment.ProcessId);
                        if (result)
                        {
                            // 更新程序已启动，退出应用
                            Environment.Exit(0);
                        }
                        else
                        {
                            MessageBox.Show("更新程序启动失败，请手动下载。\n" + info.DownloadUrl,
                                "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(info.DownloadUrl) { UseShellExecute = true });
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"已是最新版本 v{UpdateService.CurrentVersion}。",
                        "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch
            {
                MessageBox.Show("检查更新失败，请检查网络连接。", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (btn != null) btn.IsEnabled = true;
        }

        // ══════════════════════════════════════════════════════
        //  数据备份 / 还原
        // ══════════════════════════════════════════════════════

        private void BackupData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择备份目标文件夹"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string destDir = System.IO.Path.Combine(dlg.SelectedPath,
                    $"学程备份_{DateTime.Now:yyyyMMdd_HHmmss}");
                System.IO.Directory.CreateDirectory(destDir);

                foreach (var file in new[] { "settings.json", "schedule.json" })
                {
                    var src = System.IO.Path.Combine(baseDir, file);
                    if (System.IO.File.Exists(src))
                        System.IO.File.Copy(src, System.IO.Path.Combine(destDir, file), true);
                }
                MessageBox.Show($"数据已备份到：\n{destDir}", "备份成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"备份失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含 settings.json 和 schedule.json 的备份文件夹"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var r = MessageBox.Show(
                "将用备份文件覆盖当前所有配置和课表数据，确定继续吗？",
                "还原确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var file in new[] { "settings.json", "schedule.json" })
                {
                    var src = System.IO.Path.Combine(dlg.SelectedPath, file);
                    if (System.IO.File.Exists(src))
                        System.IO.File.Copy(src, System.IO.Path.Combine(baseDir, file), true);
                }
                MessageBox.Show("数据已还原，请重启应用使设置生效。", "还原成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"还原失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════
        //  工具方法
        // ══════════════════════════════════════════════════════

        private static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = Colors.White;
                return false;
            }
        }

        /// <summary>验证颜色格式，无效时弹窗提醒</summary>
        private bool ValidateExamColor(string hex, string label)
        {
            if (TryParseColor(hex, out _)) return true;
            MessageBox.Show($"{label}格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                               "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static string ColorToHex(Color c)
            => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                          : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private static void RefreshColorPreview(TextBox box, System.Windows.Shapes.Rectangle rect)
        {
            if (rect == null) return;
            if (TryParseColor(box.Text, out Color c))
                rect.Fill = new SolidColorBrush(c);
        }

        private static bool PickColor(string initial, out Color picked)
        {
            picked = Colors.White;
            var result = DialogHelper.ShowColorPicker(initial);
            if (result.HasValue) { picked = result.Value; return true; }
            return false;
        }

    }
}
