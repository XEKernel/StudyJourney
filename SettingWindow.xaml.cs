using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
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
        private bool _isInitialized = false;   // 防重复初始化
        private ScrollViewer[]? _tabContents;  // 索引 → 内容面板

        public SettingWindow(MainWindow window)
        {
            InitializeComponent();
            _mainWindow = window;
            ContentRendered += SettingWindow_ContentRendered;
            Closed += SettingWindow_Closed;
        }

        // ══════════════════════════════════════════════════════
        //  窗口渲染完成后再加载数据和动画
        // ══════════════════════════════════════════════════════

        private void SettingWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= SettingWindow_ContentRendered;

            // 将初始化延迟到窗口完全加载后执行，避免动画/布局竞态导致卡死
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isInitialized) return;
                _isInitialized = true;

                try
                {
                    // 建立 Tab 索引 → 内容面板映射
                    _tabContents = new[]
                    {
                        ContentAppearance,
                        ContentPosition,
                        ContentDisplay,
                        ContentText,
                        ContentDate,
                        ContentAnimation,
                        ContentApi,
                        ContentSchedule,
                        ContentExam,
                        ContentAbout
                    };

                    PopulateFontFamilies();
                    LoadSettings();

                    // 根据设置应用 / 移除控件动画
                    if (_enableSettingsAnimations)
                        ApplyControlAnimations();
                    else
                        RemoveControlAnimations();

                    // 注册颜色输入框实时预览事件
                    NumberColorBox.TextChanged      += NumberColorBox_TextChanged;
                    TextColorBox.TextChanged        += TextColorBox_TextChanged;
                    ProgressBarColorBox.TextChanged += ProgressBarColorBox_TextChanged;
                    QuoteForegroundBox.TextChanged += QuoteForegroundBox_TextChanged;
                    WeatherCityColorBox.TextChanged += WeatherCityColorBox_TextChanged;
                    WeatherInfoColorBox.TextChanged += WeatherInfoColorBox_TextChanged;
                    WeatherTempColorBox.TextChanged += WeatherTempColorBox_TextChanged;
                    WeatherTimeColorBox.TextChanged += WeatherTimeColorBox_TextChanged;
                    WeatherIconColorBox.TextChanged += WeatherIconColorBox_TextChanged;

                    // 考试模式样式颜色实时预览
                    ExamSubjectColorBox.TextChanged += ExamSubjectColorBox_TextChanged;
                    ExamNameColorBox.TextChanged += ExamNameColorBox_TextChanged;
                    ExamCountdownNormalColorBox.TextChanged += ExamCountdownNormalColorBox_TextChanged;
                    ExamCountdownWarningColorBox.TextChanged += ExamCountdownWarningColorBox_TextChanged;
                    ExamCountdownCriticalColorBox.TextChanged += ExamCountdownCriticalColorBox_TextChanged;
                    ExamDistanceColorBox.TextChanged += ExamDistanceColorBox_TextChanged;
                    ExamInfoColorBox.TextChanged += ExamInfoColorBox_TextChanged;
                    ExamProgressBarColorBox.TextChanged += ExamProgressBarColorBox_TextChanged;

                    // 窗口入场动画
                    if (_enableSettingsAnimations)
                    {
                        AnimateWindowEntrance();
                    }

                    // 允许后续 Tab 切换动画
                    _isInitializing = false;

                    // 手动给第一个已选中的 Tab 做入场（默认从右侧滑入）
                    if (_enableSettingsAnimations && MainTabControl.SelectedIndex >= 0)
                    {
                        double w = ContentHost.ActualWidth > 0 ? ContentHost.ActualWidth : 400;
                        SlideIn(_tabContents[MainTabControl.SelectedIndex], 1, w);
                    }
                }
                catch
                {
                    // 初始化异常静默处理，确保窗口至少可用
                    _isInitializing = false;
                    if (_enableSettingsAnimations)
                        RemoveControlAnimations();
                }
            }), DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════════
        //  窗口关闭清理
        // ══════════════════════════════════════════════════════
        private void SettingWindow_Closed(object? sender, EventArgs e)
        {
            Closed -= SettingWindow_Closed;

            // 停止所有可能的动画
            try
            {
                _outgoingPanel = null;
                if (_tabContents != null)
                {
                    foreach (var sv in _tabContents)
                    {
                        if (sv == null) continue;
                        sv.BeginAnimation(UIElement.OpacityProperty, null);
                        if (sv.RenderTransform is TranslateTransform tt)
                            tt.BeginAnimation(TranslateTransform.XProperty, null);
                    }
                }
                MainGrid.BeginAnimation(UIElement.OpacityProperty, null);

                // 移除控件动画样式（恢复默认 WPF 样式）
                RemoveControlAnimations();
            }
            catch
            {
                // 清理失败静默处理
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
        //  Tab 切换过渡动画（方向感知 — A 出 B 进真正并行平移）
        // ══════════════════════════════════════════════════════
        //
        //  核心思路：
        //  ContentHost 设 ClipToBounds=True，裁掉视口外内容。
        //  新页面起始 X = ±ContentHost.ActualWidth，确保初始在视口外，
        //  旧页面终止 X = ∓ContentHost.ActualWidth，移出视口后再折叠。
        //  两个动画时长完全相同 → 看起来像两页并肩平移，零重影。
        //  不做 Opacity 淡入淡出，避免半透明叠加产生重影。

        private ScrollViewer? _outgoingPanel;   // 正在离场的面板

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (!_enableSettingsAnimations) return;
            if (_tabContents == null) return;

            TabItem? oldTab = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as TabItem : null;
            TabItem? newTab = e.AddedItems.Count   > 0 ? e.AddedItems[0]   as TabItem : null;
            if (newTab == null) return;

            int oldIndex = oldTab != null ? MainTabControl.Items.IndexOf(oldTab) : -1;
            int newIndex = MainTabControl.Items.IndexOf(newTab);
            if (newIndex < 0 || newIndex >= _tabContents.Length) return;

            // 方向：向右切 = +1（新页从右侧滑入，旧页向左滑出）
            int direction = oldIndex < 0 ? 1 : (newIndex > oldIndex ? 1 : -1);

            // 获取容器宽度作为位移距离（保证新页在视口外起步）
            double panelWidth = ContentHost.ActualWidth > 0 ? ContentHost.ActualWidth : 400;

            ScrollViewer newSv = _tabContents[newIndex];

            // 快速切换：立即中止正在离场的面板
            if (_outgoingPanel != null && _outgoingPanel != newSv)
            {
                SnapCollapse(_outgoingPanel);
                _outgoingPanel = null;
            }

            // 旧页滑出
            if (oldIndex >= 0 && oldIndex < _tabContents.Length)
            {
                ScrollViewer oldSv = _tabContents[oldIndex];
                if (oldSv != newSv)
                {
                    _outgoingPanel = oldSv;
                    SlideOut(oldSv, direction, panelWidth);
                }
            }

            // 新页滑入（同步开始，同步时长）
            SlideIn(newSv, direction, panelWidth);
        }

        /// <summary>强制立即折叠并重置面板状态</summary>
        private static void SnapCollapse(ScrollViewer sv)
        {
            sv.BeginAnimation(UIElement.OpacityProperty, null);
            if (sv.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.XProperty, null);
            sv.Visibility = Visibility.Collapsed;
            sv.Opacity    = 1;
            if (sv.RenderTransform is TranslateTransform tt2) tt2.X = 0;
        }

        private static readonly Duration SlideTime = new Duration(TimeSpan.FromSeconds(0.8));
        private static readonly IEasingFunction SlideEase =
            new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };

        /// <summary>新页面滑入：从视口外平移到 X=0</summary>
        private static void SlideIn(ScrollViewer sv, int direction, double width)
        {
            EnsureTranslate(sv);

            // 停旧动画
            sv.BeginAnimation(UIElement.OpacityProperty, null);
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);

            // 从视口外出发
            double startX = direction >= 0 ? width : -width;
            ((TranslateTransform)sv.RenderTransform).X = startX;
            sv.Opacity = 1;
            sv.Visibility = Visibility.Visible;

            ((TranslateTransform)sv.RenderTransform).BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(startX, 0, SlideTime) { EasingFunction = SlideEase });
        }

        /// <summary>旧页面滑出：从 X=0 平移出视口，完成后折叠</summary>
        private void SlideOut(ScrollViewer sv, int direction, double width)
        {
            EnsureTranslate(sv);

            // 停旧动画
            sv.BeginAnimation(UIElement.OpacityProperty, null);
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);

            double endX = direction >= 0 ? -width : width;
            ((TranslateTransform)sv.RenderTransform).X = 0;
            sv.Opacity = 1;

            var xAnim = new DoubleAnimation(0, endX, SlideTime) { EasingFunction = SlideEase };
            xAnim.Completed += (_, _) =>
            {
                if (_outgoingPanel == sv)
                {
                    SnapCollapse(sv);
                    _outgoingPanel = null;
                }
            };
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.XProperty, xAnim);
        }

        private static void EnsureTranslate(ScrollViewer sv)
        {
            if (sv.RenderTransform is not TranslateTransform)
                sv.RenderTransform = new TranslateTransform();
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
            HideDuringClassCheck.IsChecked = _mainWindow.HideDuringClass;

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

            // ── 每日一言 ──────────────────────────────────────
            ShowDailyQuoteCheck.IsChecked      = _mainWindow.ShowDailyQuote;
            QuoteFontSizeSlider.Value          = _mainWindow.QuoteFontSize;
            QuoteFontSizeText.Text             = _mainWindow.QuoteFontSize.ToString("F0");
            QuoteForegroundBox.Text            = _mainWindow.QuoteForegroundHex;
            QuoteItalicCheck.IsChecked         = _mainWindow.QuoteItalic;
            QuoteApiUrlBox.Text                = _mainWindow.QuoteApiUrl;
            QuoteTextFieldNameBox.Text          = _mainWindow.QuoteTextFieldName;
            QuoteRefreshIntervalSlider.Value   = _mainWindow.QuoteAutoRefreshInterval;
            QuoteRefreshIntervalText.Text      = _mainWindow.QuoteAutoRefreshInterval == 0
                ? "关" : $"{_mainWindow.QuoteAutoRefreshInterval}s";

            // ── 课表栏 ────────────────────────────────────────
            ShowScheduleBarCheck.IsChecked         = _mainWindow.ShowScheduleBar;
            ScheduleBarAlwaysOnTopCheck.IsChecked  = _mainWindow.ScheduleBarAlwaysOnTop;
            ScheduleBarClickThroughCheck.IsChecked = _mainWindow.ScheduleBarClickThrough;
            ScheduleBarAutoCollapseCheck.IsChecked = _mainWindow.ScheduleBarAutoCollapse;
            ScheduleBarOpacitySlider.Value         = _mainWindow.ScheduleBarOpacity;
            ScheduleBarOpacityLabel.Text           = $"{_mainWindow.ScheduleBarOpacity * 100:F0}%";
            ScheduleBarWidthBox.Text               = _mainWindow.ScheduleBarWidth.ToString("F0");
            ScheduleBarFontSizeSlider.Value      = _mainWindow.ScheduleBarFontSize;
            ScheduleBarFontSizeText.Text         = _mainWindow.ScheduleBarFontSize.ToString("F0");
            EnableReminderSoundCheck.IsChecked     = _mainWindow.EnableReminderSound;
            ReminderSoundPathBox.Text              = _mainWindow.ReminderSoundPath;
            RemindClassStartCheck.IsChecked        = _mainWindow.RemindClassStart;
            RemindClassMidCheck.IsChecked          = _mainWindow.RemindClassMid;
            RemindClassEndSoonCheck.IsChecked      = _mainWindow.RemindClassEndSoon;
            RemindClassEndCheck.IsChecked          = _mainWindow.RemindClassEnd;
            RemindNextClassSoonCheck.IsChecked     = _mainWindow.RemindNextClassSoon;
            RemindDayEndCheck.IsChecked            = _mainWindow.RemindDayEnd;
            RemindSpecialPeriodCheck.IsChecked     = _mainWindow.RemindSpecialPeriod;

            // ── 考试模式 ──────────────────────────────────────
            EnableExamModeCheck.IsChecked   = _mainWindow.EnableExamMode;
            AutoEnterExamModeCheck.IsChecked = _mainWindow.AutoEnterExamMode;
            ExamModeFontSizeSlider.Value     = _mainWindow.ExamModeFontSize;
            ExamModeFontSizeText.Text        = _mainWindow.ExamModeFontSize.ToString("F0");

            // ── 考试模式样式 ──────────────────────────────────
            ExamSubjectFontSizeSlider.Value     = _mainWindow.ExamSubjectFontSize;
            ExamSubjectFontSizeText.Text        = _mainWindow.ExamSubjectFontSize.ToString("F0");
            ExamNameFontSizeSlider.Value        = _mainWindow.ExamNameFontSize;
            ExamNameFontSizeText.Text           = _mainWindow.ExamNameFontSize.ToString("F0");
            ExamCountdownFontSizeSlider.Value   = _mainWindow.ExamCountdownFontSize;
            ExamCountdownFontSizeText.Text      = _mainWindow.ExamCountdownFontSize.ToString("F0");
            ExamTimeInfoFontSizeSlider.Value    = _mainWindow.ExamTimeInfoFontSize;
            ExamTimeInfoFontSizeText.Text       = _mainWindow.ExamTimeInfoFontSize.ToString("F0");
            ExamNextSubjectFontSizeSlider.Value = _mainWindow.ExamNextSubjectFontSize;
            ExamNextSubjectFontSizeText.Text    = _mainWindow.ExamNextSubjectFontSize.ToString("F0");
            ExamWarningFontSizeSlider.Value     = _mainWindow.ExamWarningFontSize;
            ExamWarningFontSizeText.Text        = _mainWindow.ExamWarningFontSize.ToString("F0");
            ExamEscHintFontSizeSlider.Value     = _mainWindow.ExamEscHintFontSize;
            ExamEscHintFontSizeText.Text        = _mainWindow.ExamEscHintFontSize.ToString("F0");
            ExamProgressBarHeightSlider.Value   = _mainWindow.ExamProgressBarHeight;
            ExamProgressBarHeightText.Text      = _mainWindow.ExamProgressBarHeight.ToString("F0");

            ExamSubjectColorBox.Text           = _mainWindow.ExamSubjectColor;
            ExamNameColorBox.Text              = _mainWindow.ExamNameColor;
            ExamCountdownNormalColorBox.Text   = _mainWindow.ExamCountdownNormalColor;
            ExamCountdownWarningColorBox.Text  = _mainWindow.ExamCountdownWarningColor;
            ExamCountdownCriticalColorBox.Text = _mainWindow.ExamCountdownCriticalColor;
            ExamDistanceColorBox.Text          = _mainWindow.ExamDistanceColor;
            ExamInfoColorBox.Text              = _mainWindow.ExamInfoColor;
            ExamProgressBarColorBox.Text       = _mainWindow.ExamProgressBarColor;
            RefreshColorPreview(ExamSubjectColorBox,          ExamSubjectColorPreview);
            RefreshColorPreview(ExamNameColorBox,             ExamNameColorPreview);
            RefreshColorPreview(ExamCountdownNormalColorBox,  ExamCountdownNormalColorPreview);
            RefreshColorPreview(ExamCountdownWarningColorBox, ExamCountdownWarningColorPreview);
            RefreshColorPreview(ExamCountdownCriticalColorBox,ExamCountdownCriticalColorPreview);
            RefreshColorPreview(ExamDistanceColorBox,         ExamDistanceColorPreview);
            RefreshColorPreview(ExamInfoColorBox,             ExamInfoColorPreview);
            RefreshColorPreview(ExamProgressBarColorBox,      ExamProgressBarColorPreview);

            // 填充课表 DataGrid
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
            {
                PopulateScheduleComboColumns();
                RefreshScheduleGrid();
                RefreshExamGrid();
            }

            // ── 天气 ──────────────────────────────────────────
            WeatherCityBox.Text                 = _mainWindow.WeatherCity;
            WeatherAdcodeBox.Text               = _mainWindow.WeatherAdcode;
            WeatherFontSizeSlider.Value         = _mainWindow.WeatherFontSize;
            WeatherFontSizeText.Text            = _mainWindow.WeatherFontSize.ToString("F0");
            WeatherRefreshIntervalSlider.Value  = _mainWindow.WeatherRefreshInterval;
            WeatherRefreshIntervalText.Text     = _mainWindow.WeatherRefreshInterval == 0
                ? "关" : $"{_mainWindow.WeatherRefreshInterval}min";

            // 天气文字颜色
            WeatherCityColorBox.Text      = _mainWindow.WeatherCityColor;
            WeatherInfoColorBox.Text      = _mainWindow.WeatherInfoColor;
            WeatherTempColorBox.Text      = _mainWindow.WeatherTempColor;
            WeatherTimeColorBox.Text      = _mainWindow.WeatherTimeColor;
            WeatherIconColorBox.Text      = _mainWindow.WeatherIconColor;
            RefreshColorPreview(WeatherCityColorBox,      WeatherCityColorPreview);
            RefreshColorPreview(WeatherInfoColorBox,      WeatherInfoColorPreview);
            RefreshColorPreview(WeatherTempColorBox,      WeatherTempColorPreview);
            RefreshColorPreview(WeatherTimeColorBox,      WeatherTimeColorPreview);
            RefreshColorPreview(WeatherIconColorBox,      WeatherIconColorPreview);
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
            _mainWindow.HideDuringClass = HideDuringClassCheck.IsChecked == true;

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

            // ── 每日一言 ──────────────────────────────────────
            _mainWindow.ShowDailyQuote          = ShowDailyQuoteCheck.IsChecked == true;
            _mainWindow.QuoteFontSize           = QuoteFontSizeSlider.Value;
            _mainWindow.QuoteForegroundHex       = QuoteForegroundBox.Text.Trim();
            _mainWindow.QuoteItalic             = QuoteItalicCheck.IsChecked == true;
            _mainWindow.QuoteApiUrl             = QuoteApiUrlBox.Text.Trim();
            _mainWindow.QuoteTextFieldName      = QuoteTextFieldNameBox.Text.Trim();
            _mainWindow.QuoteAutoRefreshInterval = (int)QuoteRefreshIntervalSlider.Value;

            // 应用样式到主窗口
            _mainWindow.ApplyQuoteStyle();
            // 更新自动切换定时器
            _mainWindow.StartQuoteRefreshTimer();
            // 如果开关打开，立即加载一条
            if (_mainWindow.ShowDailyQuote)
                _ = _mainWindow.RefreshQuoteAsync();

            // ── 天气 ──────────────────────────────────────────
            _mainWindow.WeatherCity          = WeatherCityBox.Text.Trim();
            _mainWindow.WeatherAdcode        = WeatherAdcodeBox.Text.Trim();


            _mainWindow.WeatherFontSize     = WeatherFontSizeSlider.Value;

            _mainWindow.WeatherRefreshInterval = (int)WeatherRefreshIntervalSlider.Value;

            // 天气文字颜色
            if (!TryParseColor(WeatherCityColorBox.Text, out Color wcc))
            {
                WpfMessageBox.Show("城市名颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherInfoColorBox.Text, out Color wic))
            {
                WpfMessageBox.Show("天气信息颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherTempColorBox.Text, out Color wtc))
            {
                WpfMessageBox.Show("温度颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherTimeColorBox.Text, out Color wtc2))
            {
                WpfMessageBox.Show("更新时间颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherIconColorBox.Text, out Color wico))
            {
                WpfMessageBox.Show("天气图标颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _mainWindow.WeatherCityColor  = WeatherCityColorBox.Text.Trim();
            _mainWindow.WeatherInfoColor  = WeatherInfoColorBox.Text.Trim();
            _mainWindow.WeatherTempColor  = WeatherTempColorBox.Text.Trim();
            _mainWindow.WeatherTimeColor  = WeatherTimeColorBox.Text.Trim();
            _mainWindow.WeatherIconColor  = WeatherIconColorBox.Text.Trim();


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

            // ── 课表栏设置 ────────────────────────────────────
            _mainWindow.ShowScheduleBar         = ShowScheduleBarCheck.IsChecked == true;
            _mainWindow.ScheduleBarAlwaysOnTop  = ScheduleBarAlwaysOnTopCheck.IsChecked == true;
            _mainWindow.ScheduleBarClickThrough = ScheduleBarClickThroughCheck.IsChecked == true;
            _mainWindow.ScheduleBarAutoCollapse = ScheduleBarAutoCollapseCheck.IsChecked == true;
            _mainWindow.ScheduleBarOpacity      = ScheduleBarOpacitySlider.Value;
            if (double.TryParse(ScheduleBarWidthBox.Text, out double sbw)) _mainWindow.ScheduleBarWidth = sbw;
            _mainWindow.ScheduleBarFontSize     = ScheduleBarFontSizeSlider.Value;
            _mainWindow.EnableReminderSound     = EnableReminderSoundCheck.IsChecked == true;
            _mainWindow.ReminderSoundPath       = ReminderSoundPathBox.Text.Trim();
            _mainWindow.RemindClassStart        = RemindClassStartCheck.IsChecked == true;
            _mainWindow.RemindClassMid          = RemindClassMidCheck.IsChecked == true;
            _mainWindow.RemindClassEndSoon      = RemindClassEndSoonCheck.IsChecked == true;
            _mainWindow.RemindClassEnd          = RemindClassEndCheck.IsChecked == true;
            _mainWindow.RemindNextClassSoon     = RemindNextClassSoonCheck.IsChecked == true;
            _mainWindow.RemindDayEnd            = RemindDayEndCheck.IsChecked == true;
            _mainWindow.RemindSpecialPeriod     = RemindSpecialPeriodCheck.IsChecked == true;

            // ── 考试模式 ──────────────────────────────────────
            _mainWindow.EnableExamMode    = EnableExamModeCheck.IsChecked == true;
            _mainWindow.AutoEnterExamMode = AutoEnterExamModeCheck.IsChecked == true;
            _mainWindow.ExamModeFontSize  = ExamModeFontSizeSlider.Value;

            // ── 考试模式样式 ──────────────────────────────────
            _mainWindow.ExamSubjectFontSize       = ExamSubjectFontSizeSlider.Value;
            _mainWindow.ExamNameFontSize          = ExamNameFontSizeSlider.Value;
            _mainWindow.ExamCountdownFontSize     = ExamCountdownFontSizeSlider.Value;
            _mainWindow.ExamTimeInfoFontSize      = ExamTimeInfoFontSizeSlider.Value;
            _mainWindow.ExamNextSubjectFontSize   = ExamNextSubjectFontSizeSlider.Value;
            _mainWindow.ExamWarningFontSize       = ExamWarningFontSizeSlider.Value;
            _mainWindow.ExamEscHintFontSize       = ExamEscHintFontSizeSlider.Value;
            _mainWindow.ExamProgressBarHeight     = ExamProgressBarHeightSlider.Value;
            _mainWindow.ExamSubjectColor          = ExamSubjectColorBox.Text.Trim();
            _mainWindow.ExamNameColor             = ExamNameColorBox.Text.Trim();
            _mainWindow.ExamCountdownNormalColor  = ExamCountdownNormalColorBox.Text.Trim();
            _mainWindow.ExamCountdownWarningColor = ExamCountdownWarningColorBox.Text.Trim();
            _mainWindow.ExamCountdownCriticalColor= ExamCountdownCriticalColorBox.Text.Trim();
            _mainWindow.ExamDistanceColor         = ExamDistanceColorBox.Text.Trim();
            _mainWindow.ExamInfoColor             = ExamInfoColorBox.Text.Trim();
            _mainWindow.ExamProgressBarColor      = ExamProgressBarColorBox.Text.Trim();

            // 应用考试模式窗口样式（若已打开）
            _mainWindow.ApplyExamModeStyle();

            // 通知主窗口刷新课表栏
            _mainWindow.ApplyScheduleBarSettings();

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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 直接关闭，不做淡出动画。
            // BeginAnimation 会持有 MainGrid.OpacityProperty，
            // 与 ContentHost 内子 ScrollViewer 的 tab 切换动画冲突。
            Close();
        }

        private void GitHubLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
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
            _mainWindow.HideDuringClass     = defaults.HideDuringClass;
            _mainWindow.GaokaoDateStr       = defaults.GaokaoDateStr;
            _mainWindow.StartDateStr        = defaults.StartDateStr;
            _mainWindow.ProgressDecimalDigits = defaults.ProgressDecimalDigits;
            _mainWindow.EnableAnimations    = defaults.EnableAnimations;
            _enableSettingsAnimations       = true;
            _mainWindow.ShowDailyQuote            = defaults.ShowDailyQuote;
            _mainWindow.QuoteFontSize             = defaults.QuoteFontSize;
            _mainWindow.QuoteForegroundHex        = defaults.QuoteForegroundHex;
            _mainWindow.QuoteItalic               = defaults.QuoteItalic;
            _mainWindow.QuoteApiUrl               = defaults.QuoteApiUrl;
            _mainWindow.QuoteTextFieldName        = defaults.QuoteTextFieldName;
            _mainWindow.QuoteAutoRefreshInterval   = defaults.QuoteAutoRefreshInterval;
            _mainWindow.WeatherCity              = defaults.WeatherCity;
            _mainWindow.WeatherAdcode            = defaults.WeatherAdcode;
            _mainWindow.WeatherFontSize          = defaults.WeatherFontSize;
            _mainWindow.WeatherRefreshInterval   = defaults.WeatherRefreshInterval;
            _mainWindow.WeatherCityColor        = defaults.WeatherCityColor;
            _mainWindow.WeatherInfoColor        = defaults.WeatherInfoColor;
            _mainWindow.WeatherTempColor        = defaults.WeatherTempColor;
            _mainWindow.WeatherTimeColor        = defaults.WeatherTimeColor;
            _mainWindow.WeatherIconColor        = defaults.WeatherIconColor;
            _mainWindow.ScheduleBarFontSize     = defaults.ScheduleBarFontSize;
            _mainWindow.ScheduleBarAutoCollapse = defaults.ScheduleBarAutoCollapse;
            _mainWindow.ExamModeFontSize        = defaults.ExamModeFontSize;
            _mainWindow.ExamSubjectFontSize       = defaults.ExamSubjectFontSize;
            _mainWindow.ExamNameFontSize          = defaults.ExamNameFontSize;
            _mainWindow.ExamCountdownFontSize     = defaults.ExamCountdownFontSize;
            _mainWindow.ExamTimeInfoFontSize      = defaults.ExamTimeInfoFontSize;
            _mainWindow.ExamNextSubjectFontSize   = defaults.ExamNextSubjectFontSize;
            _mainWindow.ExamWarningFontSize       = defaults.ExamWarningFontSize;
            _mainWindow.ExamEscHintFontSize       = defaults.ExamEscHintFontSize;
            _mainWindow.ExamProgressBarHeight     = defaults.ExamProgressBarHeight;
            _mainWindow.ExamSubjectColor          = defaults.ExamSubjectColor;
            _mainWindow.ExamNameColor             = defaults.ExamNameColor;
            _mainWindow.ExamCountdownNormalColor  = defaults.ExamCountdownNormalColor;
            _mainWindow.ExamCountdownWarningColor = defaults.ExamCountdownWarningColor;
            _mainWindow.ExamCountdownCriticalColor= defaults.ExamCountdownCriticalColor;
            _mainWindow.ExamDistanceColor         = defaults.ExamDistanceColor;
            _mainWindow.ExamInfoColor             = defaults.ExamInfoColor;
            _mainWindow.ExamProgressBarColor      = defaults.ExamProgressBarColor;
            _mainWindow.ExamProgressBarBgColor    = defaults.ExamProgressBarBgColor;
            _mainWindow.ExamBackgroundColor       = defaults.ExamBackgroundColor;
            _mainWindow.ExamNextSubjectColor      = defaults.ExamNextSubjectColor;
            _mainWindow.ExamWarningColor          = defaults.ExamWarningColor;
            _mainWindow.ExamProgressPctColor      = defaults.ExamProgressPctColor;
            _mainWindow.ExamCountdownFontFamily   = defaults.ExamCountdownFontFamily;
            _mainWindow.ExamInfoDimColor          = defaults.ExamInfoDimColor;
            _mainWindow.ApplyExamModeStyle();
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
            if (_isInitializing) return;
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

        // ── 控件动画开关 ────────────────────────────────────

        private void ApplyControlAnimations()
        {
            Resources[typeof(RadioButton)] = BuildAnimatedRadioStyle();
            Resources[typeof(CheckBox)]    = BuildAnimatedCheckStyle();
        }

        private void RemoveControlAnimations()
        {
            Resources.Remove(typeof(RadioButton));
            Resources.Remove(typeof(CheckBox));
        }

        // ── 在 C# 中构建动画控件样式（统一 1.5 秒） ──────────

        /// <summary>构建带动画的 RadioButton 样式（全部 1.5s）</summary>
        private static Style BuildAnimatedRadioStyle()
        {
            // ── 外层 Border ──────────────────────────────────────
            var radioOuter = new FrameworkElementFactory(typeof(Border));
            radioOuter.Name = "RadioOuter";
            radioOuter.SetValue(Border.WidthProperty, 18.0);
            radioOuter.SetValue(Border.HeightProperty, 18.0);
            radioOuter.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            radioOuter.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")));
            radioOuter.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));
            radioOuter.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            radioOuter.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));
            radioOuter.SetValue(Border.SnapsToDevicePixelsProperty, true);

            // ── 内点 ────────────────────────────────────────────
            var radioDot = new FrameworkElementFactory(typeof(Ellipse));
            radioDot.Name = "RadioDot";
            radioDot.SetValue(Ellipse.WidthProperty, 8.0);
            radioDot.SetValue(Ellipse.HeightProperty, 8.0);
            radioDot.SetValue(Ellipse.FillProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")));
            radioDot.SetValue(Ellipse.OpacityProperty, 0.0);
            radioDot.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            radioDot.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);
            radioOuter.AppendChild(radioDot);

            // ── ContentPresenter ─────────────────────────────────
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 0, 0));
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            // ── 根 Grid ─────────────────────────────────────────
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Grid.BackgroundProperty, Brushes.Transparent);
            var cd0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var cd1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            root.AppendChild(cd0);
            root.AppendChild(cd1);
            root.AppendChild(radioOuter);
            root.AppendChild(cp);

            // ── ControlTemplate ──────────────────────────────────
            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = root };

            // ── 稳态 Trigger ────────────────────────────────────
            var isCheckedTrigger = new Trigger
            {
                Property = RadioButton.IsCheckedProperty,
                Value = true
            };
            isCheckedTrigger.Setters.Add(new Setter(Ellipse.OpacityProperty, 1.0, "RadioDot"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")), "RadioOuter"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#186688CC")), "RadioOuter"));
            template.Triggers.Add(isCheckedTrigger);

            // MouseOver + !Checked
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(RadioButton.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(RadioButton.IsCheckedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")), "RadioOuter"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66FFFFFF")), "RadioOuter"));
            template.Triggers.Add(hoverTrigger);

            // MouseOver + Checked
            var hoverCheckedTrigger = new MultiTrigger();
            hoverCheckedTrigger.Conditions.Add(new Condition(RadioButton.IsMouseOverProperty, true));
            hoverCheckedTrigger.Conditions.Add(new Condition(RadioButton.IsCheckedProperty, true));
            hoverCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#226688CC")), "RadioOuter"));
            template.Triggers.Add(hoverCheckedTrigger);

            // IsEnabled = false
            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            // ── Checked 动画（1.5s） ─────────────────────────────
            var checkedSB = new Storyboard { FillBehavior = FillBehavior.Stop };

            var dotWAnim = new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            };
            Storyboard.SetTargetName(dotWAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotWAnim, new PropertyPath(Ellipse.WidthProperty));
            checkedSB.Children.Add(dotWAnim);

            var dotHAnim = new DoubleAnimation(0, 8, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            };
            Storyboard.SetTargetName(dotHAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotHAnim, new PropertyPath(Ellipse.HeightProperty));
            checkedSB.Children.Add(dotHAnim);

            var dotOpAnim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.35));
            Storyboard.SetTargetName(dotOpAnim, "RadioDot");
            Storyboard.SetTargetProperty(dotOpAnim, new PropertyPath(Ellipse.OpacityProperty));
            checkedSB.Children.Add(dotOpAnim);

            var checkedET = new EventTrigger(RadioButton.CheckedEvent);
            checkedET.Actions.Add(new BeginStoryboard { Storyboard = checkedSB });
            template.Triggers.Add(checkedET);

            // ── Unchecked 动画（1.5s） ───────────────────────────
            var uncheckedSB = new Storyboard { FillBehavior = FillBehavior.Stop };

            var dotWOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35));
            Storyboard.SetTargetName(dotWOut, "RadioDot");
            Storyboard.SetTargetProperty(dotWOut, new PropertyPath(Ellipse.WidthProperty));
            uncheckedSB.Children.Add(dotWOut);

            var dotHOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35));
            Storyboard.SetTargetName(dotHOut, "RadioDot");
            Storyboard.SetTargetProperty(dotHOut, new PropertyPath(Ellipse.HeightProperty));
            uncheckedSB.Children.Add(dotHOut);

            var dotOpOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.35));
            Storyboard.SetTargetName(dotOpOut, "RadioDot");
            Storyboard.SetTargetProperty(dotOpOut, new PropertyPath(Ellipse.OpacityProperty));
            uncheckedSB.Children.Add(dotOpOut);

            var uncheckedET = new EventTrigger(RadioButton.UncheckedEvent);
            uncheckedET.Actions.Add(new BeginStoryboard { Storyboard = uncheckedSB });
            template.Triggers.Add(uncheckedET);

            // ── Style ───────────────────────────────────────────
            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 12, 3)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCE0E0F0"))));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
        }

        /// <summary>构建带动画的 CheckBox 样式（全部 1.5s）</summary>
        private static Style BuildAnimatedCheckStyle()
        {
            // ── 轨道 ─────────────────────────────────────────────
            var switchTrack = new FrameworkElementFactory(typeof(Border));
            switchTrack.Name = "SwitchTrack";
            switchTrack.SetValue(Border.WidthProperty, 40.0);
            switchTrack.SetValue(Border.HeightProperty, 22.0);
            switchTrack.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            switchTrack.SetValue(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")));
            switchTrack.SetValue(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")));
            switchTrack.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            switchTrack.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));

            // ── 滑块 ────────────────────────────────────────────
            var switchThumb = new FrameworkElementFactory(typeof(Border));
            switchThumb.Name = "SwitchThumb";
            switchThumb.SetValue(Border.WidthProperty, 18.0);
            switchThumb.SetValue(Border.HeightProperty, 18.0);
            switchThumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            switchThumb.SetValue(Border.BackgroundProperty, Brushes.White);
            switchThumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            switchThumb.SetValue(Border.MarginProperty, new Thickness(2, 0, 0, 0));
            var shadow = new DropShadowEffect { ShadowDepth = 0.5, BlurRadius = 3, Opacity = 0.3 };
            switchThumb.SetValue(Border.EffectProperty, shadow);

            // ── ContentPresenter ─────────────────────────────────
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 0, 0));
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

            // ── 根 Grid ─────────────────────────────────────────
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Grid.BackgroundProperty, Brushes.Transparent);
            var cd0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var cd1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            cd1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            root.AppendChild(cd0);
            root.AppendChild(cd1);
            root.AppendChild(switchTrack);
            root.AppendChild(switchThumb);
            root.AppendChild(cp);

            // ── ControlTemplate ──────────────────────────────────
            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };

            // ── 稳态 Trigger ────────────────────────────────────
            var isCheckedTrigger = new Trigger
            {
                Property = CheckBox.IsCheckedProperty,
                Value = true
            };
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#446688CC")), "SwitchTrack"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6688CC")), "SwitchTrack"));
            isCheckedTrigger.Setters.Add(new Setter(Border.MarginProperty,
                new Thickness(20, 0, 0, 0), "SwitchThumb"));
            isCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "SwitchThumb"));
            template.Triggers.Add(isCheckedTrigger);

            // MouseOver + !Checked
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(CheckBox.IsCheckedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")), "SwitchTrack"));
            template.Triggers.Add(hoverTrigger);

            // MouseOver + Checked
            var hoverCheckedTrigger = new MultiTrigger();
            hoverCheckedTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverCheckedTrigger.Conditions.Add(new Condition(CheckBox.IsCheckedProperty, true));
            hoverCheckedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#556688CC")), "SwitchTrack"));
            template.Triggers.Add(hoverCheckedTrigger);

            // IsEnabled = false
            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            // ── Checked 动画（1.5s） ─────────────────────────────
            var checkedSB = new Storyboard { FillBehavior = FillBehavior.Stop };
            var thumbInAnim = new ThicknessAnimation(
                new Thickness(2, 0, 0, 0),
                new Thickness(20, 0, 0, 0),
                TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(thumbInAnim, "SwitchThumb");
            Storyboard.SetTargetProperty(thumbInAnim, new PropertyPath(Border.MarginProperty));
            checkedSB.Children.Add(thumbInAnim);

            var checkedET = new EventTrigger(CheckBox.CheckedEvent);
            checkedET.Actions.Add(new BeginStoryboard { Storyboard = checkedSB });
            template.Triggers.Add(checkedET);

            // ── Unchecked 动画（1.5s） ───────────────────────────
            var uncheckedSB = new Storyboard { FillBehavior = FillBehavior.Stop };
            var thumbOutAnim = new ThicknessAnimation(
                new Thickness(20, 0, 0, 0),
                new Thickness(2, 0, 0, 0),
                TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(thumbOutAnim, "SwitchThumb");
            Storyboard.SetTargetProperty(thumbOutAnim, new PropertyPath(Border.MarginProperty));
            uncheckedSB.Children.Add(thumbOutAnim);

            var uncheckedET = new EventTrigger(CheckBox.UncheckedEvent);
            uncheckedET.Actions.Add(new BeginStoryboard { Storyboard = uncheckedSB });
            template.Triggers.Add(uncheckedET);

            // ── Style ───────────────────────────────────────────
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 7)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCE0E0F0"))));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            return style;
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

        // ══════════════════════════════════════════════════════
        //  课表 Tab 事件处理
        // ══════════════════════════════════════════════════════

        private void ScheduleBarOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScheduleBarOpacityLabel != null)
                ScheduleBarOpacityLabel.Text = $"{e.NewValue * 100:F0}%";
        }

        private void BrowseReminderSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3|所有文件|*.*",
                Title = "选择提醒声音文件"
            };
            if (dlg.ShowDialog() == true)
                ReminderSoundPathBox.Text = dlg.FileName;
        }

        private void ImportScheduleJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件|*.json|所有文件|*.*",
                Title = "导入课表 JSON"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var sm = _mainWindow.GetScheduleManager();
                if (sm == null) { ScheduleStatusTb.Text = "课表服务未初始化"; return; }
                var (ok, msg) = sm.ImportFromJson(json);
                ScheduleStatusTb.Text = msg;
                if (ok)
                {
                    var today = sm.GetTodayEntries();
                    ExamStatusTb.Text = $"考试记录：{sm.Data.Exams.Count} 场";
                }
            }
            catch (Exception ex)
            {
                ScheduleStatusTb.Text = $"读取文件失败：{ex.Message}";
            }
        }

        private void ExportScheduleJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = "schedule.json",
                Title = "导出课表 JSON"
            };
            if (dlg.ShowDialog() != true) return;
            var sm = _mainWindow.GetScheduleManager();
            if (sm == null) return;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(sm.Data, opts);
            System.IO.File.WriteAllText(dlg.FileName, json);
            ScheduleStatusTb.Text = $"已导出到 {dlg.FileName}";
        }

        private void OpenScheduleJson_Click(object sender, RoutedEventArgs e)
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schedule.json");
            if (!System.IO.File.Exists(path))
            {
                // 创建默认空课表
                var empty = new ScheduleData();
                empty.Save();
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        // ══════════════════════════════════════════════════════
        //  考试模式 Tab 事件处理
        // ══════════════════════════════════════════════════════

        private void ImportExamJson_Click(object sender, RoutedEventArgs e)
        {
            // 复用 ImportScheduleJson（考试数据在同一 schedule.json 中）
            ImportScheduleJson_Click(sender, e);
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
                ExamStatusTb.Text = $"已加载 {sm.Data.Exams.Count} 场考试";
        }

        private void EnterExamMode_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.EnterExamMode();
        }

        private void ExitExamMode_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.ExitExamMode();
        }

        // ══════════════════════════════════════════════════════
        //  课表 DataGrid 直编辑
        // ══════════════════════════════════════════════════════

        private static readonly Dictionary<string, int> _weekdays = new()
        {
            { "周一", 1 }, { "周二", 2 }, { "周三", 3 }, { "周四", 4 },
            { "周五", 5 }, { "周六", 6 }, { "周日", 7 }
        };

        private static readonly Dictionary<string, PeriodType> _periodTypes = new()
        {
            { "普通课", PeriodType.Normal },
            { "早自习", PeriodType.Morning },
            { "晚自习", PeriodType.Evening },
            { "晚读",   PeriodType.Reading },
            { "午休",   PeriodType.Noon },
        };

        /// <summary>填充 DataGrid 的 ComboBox 列选项</summary>
        private void PopulateScheduleComboColumns()
        {
            // 星期列
            var dayCol = ScheduleDataGrid.Columns[0] as DataGridComboBoxColumn;
            if (dayCol != null)
                dayCol.ItemsSource = _weekdays.ToList();

            // 类型列（索引 5）
            var typeCol = ScheduleDataGrid.Columns[5] as DataGridComboBoxColumn;
            if (typeCol != null)
                typeCol.ItemsSource = _periodTypes.ToList();
        }

        /// <summary>刷新课表 DataGrid 数据源</summary>
        private void RefreshScheduleGrid()
        {
            ScheduleDataGrid.ItemsSource = null;
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data?.Entries == null) return;
            ScheduleDataGrid.ItemsSource = sm.Data.Entries;
            RefreshScheduleStatus();
        }

        private void RefreshScheduleStatus()
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
            {
                var today = sm.GetTodayEntries();
                ScheduleStatusTb.Text = $"已加载 {sm.Data.Entries.Count} 节课 / 今日 {today.Count} 节课 / {sm.Data.Exams.Count} 场考试";
            }
        }

        private void AddScheduleEntry_Click(object sender, RoutedEventArgs e)
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data == null) return;
            sm.Data.Entries.Add(new ScheduleEntry { DayOfWeek = 1, Period = sm.Data.Entries.Count + 1, Subject = "新课程" });
            RefreshScheduleGrid();
        }

        private void DeleteScheduleEntry_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleDataGrid.SelectedItem is not ScheduleEntry entry) return;
            var sm = _mainWindow.GetScheduleManager();
            sm?.Data?.Entries.Remove(entry);
            RefreshScheduleGrid();
        }

        private void SaveSchedule_Click(object sender, RoutedEventArgs e)
        {
            // DataGrid 双向绑定已生效，直接保存
            var sm = _mainWindow.GetScheduleManager();
            sm?.Save();
            RefreshScheduleStatus();
            ScheduleStatusTb.Text += "  ✅ 已保存";
        }

        // ══════════════════════════════════════════════════════
        //  考试 DataGrid 直编辑
        // ══════════════════════════════════════════════════════

        /// <summary>刷新考试 DataGrid</summary>
        private void RefreshExamGrid()
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data?.Exams == null) return;
            ExamDataGrid.ItemsSource = null;
            ExamDataGrid.ItemsSource = sm.Data.Exams;
            ExamSubjectGrid.ItemsSource = null;
            RefreshExamStatus();
        }

        private void RefreshExamStatus()
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
                ExamStatusTb.Text = $"已加载 {sm.Data.Exams.Count} 场考试";
        }

        /// <summary>选中考试时联动展示其科目列表</summary>
        private void ExamDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExamDataGrid.SelectedItem is ExamEntry exam)
                ExamSubjectGrid.ItemsSource = exam.Subjects;
            else
                ExamSubjectGrid.ItemsSource = null;
        }

        private void AddExam_Click(object sender, RoutedEventArgs e)
        {
            var sm = _mainWindow.GetScheduleManager();
            if (sm?.Data == null) return;
            sm.Data.Exams.Add(new ExamEntry { Name = "新考试", DateStr = DateTime.Today.ToString("yyyy-MM-dd") });
            RefreshExamGrid();
        }

        private void DeleteExam_Click(object sender, RoutedEventArgs e)
        {
            if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;
            var sm = _mainWindow.GetScheduleManager();
            sm?.Data?.Exams.Remove(exam);
            RefreshExamGrid();
        }

        private void AddExamSubject_Click(object sender, RoutedEventArgs e)
        {
            if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;
            exam.Subjects.Add(new ExamSubject { Name = "新科目", StartTimeStr = "09:00", EndTimeStr = "11:00" });
            ExamSubjectGrid.ItemsSource = null;
            ExamSubjectGrid.ItemsSource = exam.Subjects;
        }

        private void DeleteExamSubject_Click(object sender, RoutedEventArgs e)
        {
            if (ExamDataGrid.SelectedItem is not ExamEntry exam) return;
            if (ExamSubjectGrid.SelectedItem is not ExamSubject sub) return;
            exam.Subjects.Remove(sub);
            ExamSubjectGrid.ItemsSource = null;
            ExamSubjectGrid.ItemsSource = exam.Subjects;
        }

        private void SaveExams_Click(object sender, RoutedEventArgs e)
        {
            var sm = _mainWindow.GetScheduleManager();
            sm?.Save();
            RefreshExamStatus();
            ExamStatusTb.Text += "  ✅ 已保存";
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

    public class FontFamilyItem
    {
        public FontFamily FontFamily { get; }
        public FontFamilyItem(FontFamily ff) => FontFamily = ff;
        public override string ToString() => FontFamily.Source;
    }
}
