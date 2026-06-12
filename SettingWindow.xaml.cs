using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace GaokaoCountdown
{
    public partial class SettingWindow : Window
    {
        private readonly MainWindow _mainWindow;

        // 运行时动画状态
        private bool _enableSettingsAnimations = true;
        private bool _isInitializing = true;   // 抑制初始加载时的 Tab 动画
        private Style? _animatedRadioStyle;
        private Style? _animatedCheckStyle;

        public SettingWindow(MainWindow window)
        {
            InitializeComponent();
            _mainWindow = window;
            ContentRendered += SettingWindow_ContentRendered;
        }

        // ══════════════════════════════════════════════════════
        //  窗口渲染完成后再加载数据和动画
        // ══════════════════════════════════════════════════════

        private void SettingWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= SettingWindow_ContentRendered;

            PopulateFontFamilies();
            LoadSettings();

            // 缓存动画版控件样式
            _animatedRadioStyle = (Style)FindResource("AnimatedRadioStyle");
            _animatedCheckStyle = (Style)FindResource("AnimatedCheckStyle");

            // 根据设置应用 / 移除控件动画
            if (_enableSettingsAnimations)
                ApplyControlAnimations();
            else
                RemoveControlAnimations();

            // 注册颜色输入框实时预览事件
            NumberColorBox.TextChanged      += NumberColorBox_TextChanged;
            TextColorBox.TextChanged        += TextColorBox_TextChanged;
            ProgressBarColorBox.TextChanged += ProgressBarColorBox_TextChanged;

            // 窗口入场动画：对内容容器做淡入（不碰 Window.Opacity，避免无边框窗口渲染死锁）
            if (_enableSettingsAnimations)
            {
                AnimateWindowEntrance();
            }

            // 允许后续 Tab 切换动画
            _isInitializing = false;

            // 手动给第一个已选中的 Tab 做淡入
            if (_enableSettingsAnimations && MainTabControl.SelectedItem is TabItem firstTab)
            {
                FadeInTabContent(firstTab);
            }
        }

        // ══════════════════════════════════════════════════════
        //  窗口入场动画：内容淡入（不碰 Window 属性）
        // ══════════════════════════════════════════════════════

        private void AnimateWindowEntrance()
        {
            try
            {
                MainGrid.Opacity = 0;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new CircleEaseEase { EasingMode = EasingMode.EaseOut }
                };
                MainGrid.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch
            {
                MainGrid.Opacity = 1;
            }
        }

        // ══════════════════════════════════════════════════════
        //  Tab 切换过渡动画
        // ══════════════════════════════════════════════════════

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 初始加载时不触发动画（ContentRendered 中手动处理）
            if (_isInitializing) return;
            if (!_enableSettingsAnimations) return;
            if (e.AddedItems.Count == 0) return;

            if (e.AddedItems[0] is TabItem newTab)
            {
                FadeInTabContent(newTab);
            }
        }

        /// <summary>为选中的 Tab 内容淡入</summary>
        private static void FadeInTabContent(TabItem tabItem)
        {
            try
            {
                if (tabItem.Content is ScrollViewer sv && sv.Content is StackPanel sp)
                {
                    AnimateContainerOpacity(sp);
                }
            }
            catch
            {
                // 动画失败不影响功能
            }
        }

        /// <summary>为容器子控件递归淡入（卡片逐个错落）</summary>
        private static void AnimateContainerOpacity(Panel panel)
        {
            int stagger = 0;
            foreach (UIElement child in panel.Children)
            {
                child.Opacity = 0;
                var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                {
                    BeginTime = TimeSpan.FromMilliseconds(stagger * 70),
                    EasingFunction = new SineEaseEase { EasingMode = EasingMode.EaseOut }
                };
                child.BeginAnimation(UIElement.OpacityProperty, anim);
                stagger++;
            }
        }

        // ══════════════════════════════════════════════════════
        //  字体列表填充
        // ══════════════════════════════════════════════════════

        private void PopulateFontFamilies()
        {
            foreach (FontFamily ff in Fonts.SystemFontFamilies)
                FontFamilyComboBox.Items.Add(new FontFamilyItem(ff));
        }

        // ══════════════════════════════════════════════════════
        //  加载设置到 UI
        // ══════════════════════════════════════════════════════

        private void LoadSettings()
        {
            // ── 文本 ──────────────────────────────────────────
            ChinesePrefixText.Text  = _mainWindow.ChinesePrefix;
            ChineseDaysText.Text    = _mainWindow.ChineseDaysText;
            ChineseHoursText.Text   = _mainWindow.ChineseHoursText;
            ChineseMinutesText.Text = _mainWindow.ChineseMinutesText;
            ChineseSecondsText.Text = _mainWindow.ChineseSecondsText;

            EnglishPrefixText.Text  = _mainWindow.EnglishPrefix;
            EnglishDaysText.Text    = _mainWindow.EnglishDaysText;
            EnglishHoursText.Text   = _mainWindow.EnglishHoursText;
            EnglishMinutesText.Text = _mainWindow.EnglishMinutesText;
            EnglishSecondsText.Text = _mainWindow.EnglishSecondsText;

            // ── 外观 ──────────────────────────────────────────
            FontSizeSlider.Value = _mainWindow.CountdownFontSize;
            FontSizeText.Text    = _mainWindow.CountdownFontSize.ToString();

            OpacitySlider.Value = _mainWindow.OverallOpacity;
            OpacityText.Text    = $"{_mainWindow.OverallOpacity * 100:F0}%";

            NumberColorBox.Text      = ColorToHex(_mainWindow.NumberColor);
            TextColorBox.Text        = ColorToHex(_mainWindow.TextColor);
            ProgressBarColorBox.Text = ColorToHex(_mainWindow.ProgressBarColor);
            RefreshColorPreview(NumberColorBox,      NumberColorPreview);
            RefreshColorPreview(TextColorBox,        TextColorPreview);
            RefreshColorPreview(ProgressBarColorBox, ProgressBarColorPreview);

            foreach (FontFamilyItem item in FontFamilyComboBox.Items)
            {
                if (item.FontFamily.Source == _mainWindow.CountdownFontFamily.Source)
                {
                    FontFamilyComboBox.SelectedItem = item;
                    break;
                }
            }

            // ── 位置 ──────────────────────────────────────────
            switch (_mainWindow.PositionPreset)
            {
                case 0: PosTop.IsChecked         = true; break;
                case 1: PosUpperCenter.IsChecked = true; break;
                case 2: PosCenter.IsChecked      = true; break;
                case 3: PosLowerCenter.IsChecked = true; break;
                case 4: PosBottom.IsChecked      = true; break;
                case 5: PosCustom.IsChecked      = true; break;
                default: PosUpperCenter.IsChecked = true; break;
            }

            CustomXBox.Text = _mainWindow.CustomPositionX.ToString("F0");
            CustomYBox.Text = _mainWindow.CustomPositionY.ToString("F0");
            OffsetYBox.Text = _mainWindow.PositionOffsetY.ToString("F0");
            AlwaysOnTopCheck.IsChecked = _mainWindow.AlwaysOnTop;
            AutoStartCheck.IsChecked   = MainWindow.GetAutoStartFromRegistry();
            HideWhenMaximizedCheck.IsChecked = _mainWindow.HideWhenMaximized;

            // ── 显示 ──────────────────────────────────────────
            ShowEnglishCheck.IsChecked      = _mainWindow.ShowEnglishLine;
            ShowProgressBarCheck.IsChecked  = _mainWindow.ShowProgressBar;
            ShowProgressTextCheck.IsChecked = _mainWindow.ShowProgressText;
            ShowDaysCheck.IsChecked         = _mainWindow.ShowDays;
            ShowHoursCheck.IsChecked        = _mainWindow.ShowHours;
            ShowMinutesCheck.IsChecked      = _mainWindow.ShowMinutes;
            ShowSecondsCheck.IsChecked      = _mainWindow.ShowSeconds;
            DecimalSlider.Value = _mainWindow.ProgressDecimalDigits;
            DecimalText.Text    = _mainWindow.ProgressDecimalDigits.ToString();

            // ── 日期 ──────────────────────────────────────────
            GaokaoDateBox.Text = _mainWindow.GaokaoDateStr;
            StartDateBox.Text  = _mainWindow.StartDateStr;

            // ── 动画 ──────────────────────────────────────────
            EnableAnimationsCheck.IsChecked = _mainWindow.EnableAnimations;
            var settingsAnim = _mainWindow.EnableAnimations;
            _enableSettingsAnimations = settingsAnim;
            EnableSettingsAnimationsCheck.IsChecked = settingsAnim;
        }

        // ══════════════════════════════════════════════════════
        //  应用 / 保存
        // ══════════════════════════════════════════════════════

        private void ApplySettings()
        {
            // ── 文本 ──────────────────────────────────────────
            _mainWindow.ChinesePrefix      = ChinesePrefixText.Text;
            _mainWindow.ChineseDaysText    = ChineseDaysText.Text;
            _mainWindow.ChineseHoursText   = ChineseHoursText.Text;
            _mainWindow.ChineseMinutesText = ChineseMinutesText.Text;
            _mainWindow.ChineseSecondsText = ChineseSecondsText.Text;

            _mainWindow.EnglishPrefix      = EnglishPrefixText.Text;
            _mainWindow.EnglishDaysText    = EnglishDaysText.Text;
            _mainWindow.EnglishHoursText   = EnglishHoursText.Text;
            _mainWindow.EnglishMinutesText = EnglishMinutesText.Text;
            _mainWindow.EnglishSecondsText = EnglishSecondsText.Text;

            // ── 字体 ──────────────────────────────────────────
            _mainWindow.CountdownFontSize = (int)FontSizeSlider.Value;
            if (FontFamilyComboBox.SelectedItem is FontFamilyItem selectedFont)
                _mainWindow.CountdownFontFamily = selectedFont.FontFamily;

            // ── 透明度 ────────────────────────────────────────
            _mainWindow.OverallOpacity = OpacitySlider.Value;

            // ── 颜色 ──────────────────────────────────────────
            if (!TryParseColor(NumberColorBox.Text, out Color nc))
            {
                WpfMessageBox.Show("数字颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(TextColorBox.Text, out Color tc))
            {
                WpfMessageBox.Show("文字颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(ProgressBarColorBox.Text, out Color pc))
            {
                WpfMessageBox.Show("进度条颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _mainWindow.NumberColor      = nc;
            _mainWindow.TextColor        = tc;
            _mainWindow.ProgressBarColor = pc;

            // ── 位置 ──────────────────────────────────────────
            _mainWindow.PositionPreset =
                PosTop.IsChecked == true         ? 0 :
                PosUpperCenter.IsChecked == true ? 1 :
                PosCenter.IsChecked == true      ? 2 :
                PosLowerCenter.IsChecked == true ? 3 :
                PosBottom.IsChecked == true      ? 4 :
                PosCustom.IsChecked == true      ? 5 : 1;

            if (double.TryParse(CustomXBox.Text, out double cx)) _mainWindow.CustomPositionX = cx;
            if (double.TryParse(CustomYBox.Text, out double cy)) _mainWindow.CustomPositionY = cy;
            if (double.TryParse(OffsetYBox.Text, out double oy)) _mainWindow.PositionOffsetY = oy;

            _mainWindow.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
            // AutoStart 在 CheckBox 事件中实时写注册表，此处同步 settings 字段即可
            _mainWindow.AutoStart   = AutoStartCheck.IsChecked == true;
            // HideWhenMaximized 在 CheckBox 事件中实时生效，此处同步 settings 字段
            _mainWindow.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;

            // ── 显示 ──────────────────────────────────────────
            _mainWindow.ShowEnglishLine       = ShowEnglishCheck.IsChecked == true;
            _mainWindow.ShowProgressBar       = ShowProgressBarCheck.IsChecked == true;
            _mainWindow.ShowProgressText      = ShowProgressTextCheck.IsChecked == true;
            _mainWindow.ShowDays              = ShowDaysCheck.IsChecked == true;
            _mainWindow.ShowHours             = ShowHoursCheck.IsChecked == true;
            _mainWindow.ShowMinutes           = ShowMinutesCheck.IsChecked == true;
            _mainWindow.ShowSeconds           = ShowSecondsCheck.IsChecked == true;
            _mainWindow.ProgressDecimalDigits = (int)DecimalSlider.Value;

            // ── 动画 ──────────────────────────────────────────
            _mainWindow.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
            _enableSettingsAnimations    = EnableSettingsAnimationsCheck.IsChecked == true;

            // ── 日期 ──────────────────────────────────────────
            if (!DateTime.TryParse(GaokaoDateBox.Text, out _))
            {
                WpfMessageBox.Show("高考日期格式不正确，请使用 yyyy-MM-dd HH:mm:ss 格式。",
                                   "日期格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!DateTime.TryParse(StartDateBox.Text, out _))
            {
                WpfMessageBox.Show("起算日期格式不正确，请使用 yyyy-MM-dd 格式。",
                                   "日期格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _mainWindow.GaokaoDateStr = GaokaoDateBox.Text.Trim();
            _mainWindow.StartDateStr  = StartDateBox.Text.Trim();
            _mainWindow.RefreshDateFields();

            // ── 应用窗口层级 ──────────────────────────────────
            _mainWindow.ApplyWindowLayer();

            // ── 刷新主窗口显示 ────────────────────────────────
            _mainWindow.UpdateCountdownDisplay();

            // ── 保存 ──────────────────────────────────────────
            _mainWindow.SaveSettings();
        }

        // ══════════════════════════════════════════════════════
        //  按钮事件
        // ══════════════════════════════════════════════════════

        private void ApplyButton_Click(object sender, RoutedEventArgs e) => ApplySettings();

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void GitHubLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/SYSTEM-MEMZ-XEK/GaokaoCountdown",
                    UseShellExecute = true
                });
            }
            catch { /* 忽略浏览器打开失败 */ }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = WpfMessageBox.Show(
                "确定要将所有设置恢复为默认值吗？",
                "重置确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var defaults = new AppSettings();
            _mainWindow.ChinesePrefix      = defaults.ChinesePrefix;
            _mainWindow.ChineseDaysText    = defaults.ChineseDaysText;
            _mainWindow.ChineseHoursText   = defaults.ChineseHoursText;
            _mainWindow.ChineseMinutesText = defaults.ChineseMinutesText;
            _mainWindow.ChineseSecondsText = defaults.ChineseSecondsText;
            _mainWindow.EnglishPrefix      = defaults.EnglishPrefix;
            _mainWindow.EnglishDaysText    = defaults.EnglishDaysText;
            _mainWindow.EnglishHoursText   = defaults.EnglishHoursText;
            _mainWindow.EnglishMinutesText = defaults.EnglishMinutesText;
            _mainWindow.EnglishSecondsText = defaults.EnglishSecondsText;
            _mainWindow.CountdownFontFamily = new FontFamily(defaults.FontFamily);
            _mainWindow.CountdownFontSize   = defaults.FontSize;
            _mainWindow.NumberColor         = defaults.NumberColor;
            _mainWindow.TextColor           = defaults.TextColor;
            _mainWindow.ProgressBarColor    = defaults.ProgressBarColor;
            _mainWindow.OverallOpacity      = defaults.OverallOpacity;
            _mainWindow.ShowEnglishLine     = defaults.ShowEnglishLine;
            _mainWindow.ShowProgressBar     = defaults.ShowProgressBar;
            _mainWindow.ShowProgressText    = defaults.ShowProgressText;
            _mainWindow.ShowDays            = defaults.ShowDays;
            _mainWindow.ShowHours           = defaults.ShowHours;
            _mainWindow.ShowMinutes         = defaults.ShowMinutes;
            _mainWindow.ShowSeconds         = defaults.ShowSeconds;
            _mainWindow.PositionPreset      = defaults.PositionPreset;
            _mainWindow.CustomPositionX     = defaults.CustomPositionX;
            _mainWindow.CustomPositionY     = defaults.CustomPositionY;
            _mainWindow.PositionOffsetY     = defaults.PositionOffsetY;
            _mainWindow.AlwaysOnTop         = defaults.AlwaysOnTop;
            _mainWindow.AutoStart           = defaults.AutoStart;  // 默认 false → 删除注册表项
            _mainWindow.HideWhenMaximized   = defaults.HideWhenMaximized;
            _mainWindow.GaokaoDateStr       = defaults.GaokaoDateStr;
            _mainWindow.StartDateStr        = defaults.StartDateStr;
            _mainWindow.ProgressDecimalDigits = defaults.ProgressDecimalDigits;
            _mainWindow.EnableAnimations    = defaults.EnableAnimations;
            _enableSettingsAnimations       = true;
            _mainWindow.RefreshDateFields();
            _mainWindow.ApplyWindowLayer();
            _mainWindow.UpdateCountdownDisplay();
            _mainWindow.SaveSettings();

            LoadSettings();
        }

        // ══════════════════════════════════════════════════════
        //  动画 CheckBox 事件
        // ══════════════════════════════════════════════════════

        private void EnableAnimationsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // 主窗口动画开关，在 Apply 时生效
        }

        private void EnableSettingsAnimationsCheck_Changed(object sender, RoutedEventArgs e)
        {
            _enableSettingsAnimations = EnableSettingsAnimationsCheck.IsChecked == true;
            if (_enableSettingsAnimations)
                ApplyControlAnimations();
            else
                RemoveControlAnimations();
        }

        private void AutoStartCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool enable = AutoStartCheck.IsChecked == true;
            MainWindow.ApplyAutoStart(enable);
            _mainWindow.AutoStart = enable;
        }

        private void HideWhenMaximizedCheck_Changed(object sender, RoutedEventArgs e)
        {
            _mainWindow.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
        }

        // ══════════════════════════════════════════════════════
        //  控件动画开关（RadioButton / CheckBox）
        // ══════════════════════════════════════════════════════

        private void ApplyControlAnimations()
        {
            if (_animatedRadioStyle != null)
                Resources[typeof(RadioButton)] = _animatedRadioStyle;
            if (_animatedCheckStyle != null)
                Resources[typeof(CheckBox)] = _animatedCheckStyle;
        }

        private void RemoveControlAnimations()
        {
            Resources.Remove(typeof(RadioButton));
            Resources.Remove(typeof(CheckBox));
        }

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

        // ── 拖动窗口 ──────────────────────────────────────────
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not TextBox && e.OriginalSource is not ComboBox)
                DragMove();
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

        private static string ColorToHex(Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static void RefreshColorPreview(TextBox box, System.Windows.Shapes.Rectangle rect)
        {
            if (rect == null) return;
            if (TryParseColor(box.Text, out Color c))
                rect.Fill = new SolidColorBrush(c);
        }

        private static bool PickColor(string initial, out Color picked)
        {
            picked = Colors.White;
            var dlg = new Forms.ColorDialog();
            if (TryParseColor(initial, out Color init))
                dlg.Color = System.Drawing.Color.FromArgb(init.R, init.G, init.B);

            if (dlg.ShowDialog() == Forms.DialogResult.OK)
            {
                picked = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                return true;
            }
            return false;
        }
    }

    // ══════════════════════════════════════════════════════
    //  自定义缓动函数
    // ══════════════════════════════════════════════════════

    public class CircleEaseEase : IEasingFunction
    {
        public EasingMode EasingMode { get; set; }
        public double Ease(double t)
        {
            return 1 - Math.Sqrt(1 - t * t);
        }
    }

    public class SineEaseEase : IEasingFunction
    {
        public EasingMode EasingMode { get; set; }
        public double Ease(double t)
        {
            return Math.Sin(t * Math.PI / 2);
        }
    }

    public class FontFamilyItem
    {
        public FontFamily FontFamily { get; }
        public FontFamilyItem(FontFamily ff) => FontFamily = ff;
        public override string ToString() => FontFamily.Source;
    }
}
