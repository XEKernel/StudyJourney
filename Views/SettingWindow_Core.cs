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
using MessageBox = GaokaoCountdown.Views.DialogHelper; // 自定义主题对话框，替代 Win32 样式

using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
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
                        ContentCountdown,
                        ContentPosition,
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
                    ExamBackgroundColorBox.TextChanged += ExamBackgroundColorBox_TextChanged;
                    ExamProgressBarBgColorBox.TextChanged += ExamProgressBarBgColorBox_TextChanged;
                    ExamNextSubjectColorBox.TextChanged += ExamNextSubjectColorBox_TextChanged;
                    ExamWarningColorBox.TextChanged += ExamWarningColorBox_TextChanged;
                    ExamProgressPctColorBox.TextChanged += ExamProgressPctColorBox_TextChanged;
                    ExamInfoDimColorBox.TextChanged += ExamInfoDimColorBox_TextChanged;

                    // 窗口入场动画
                    if (_enableSettingsAnimations)
                    {
                        AnimateWindowEntrance();
                    }

                    // 允许后续 Tab 切换动画
                    _isInitializing = false;

                    // 折叠除第一个外的所有页面，防止首次切换重影
                    for (int i = 1; i < _tabContents.Length; i++)
                        SnapCollapse(_tabContents[i]);

                    // 手动给第一个已选中的 Tab 做入场（默认从右侧滑入）
                    if (_enableSettingsAnimations && TabSidebar.SelectedIndex >= 0)
                    {
                        double h = ContentHost.ActualHeight > 0 ? ContentHost.ActualHeight : 600;
                        SlideIn(_tabContents[TabSidebar.SelectedIndex], 1, h);
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
                            tt.BeginAnimation(TranslateTransform.YProperty, null);
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
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
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
        //  新页面起始 Y = ±ContentHost.ActualHeight，确保初始在视口外，
        //  旧页面终止 Y = ∓ContentHost.ActualHeight，移出视口后再折叠。
        //  两个动画时长完全相同 → 看起来像两页并肩平移，零重影。
        //  不做 Opacity 淡入淡出，避免半透明叠加产生重影。

        private ScrollViewer? _outgoingPanel;   // 正在离场的面板

        private void TabSidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (_tabContents == null) return;

            ListBoxItem? oldItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as ListBoxItem : null;
            ListBoxItem? newItem = e.AddedItems.Count   > 0 ? e.AddedItems[0]   as ListBoxItem : null;
            if (newItem == null) return;

            int oldIndex = oldItem != null ? TabSidebar.Items.IndexOf(oldItem) : -1;
            int newIndex = TabSidebar.Items.IndexOf(newItem);
            if (newIndex < 0 || newIndex >= _tabContents.Length) return;

            // 首次点击（无旧选择）：折叠所有其他页面，只展示新页
            if (oldIndex < 0)
            {
                for (int i = 0; i < _tabContents.Length; i++)
                {
                    if (i != newIndex) SnapCollapse(_tabContents[i]);
                    else _tabContents[i].Visibility = Visibility.Visible;
                }
                if (!_enableSettingsAnimations) return;
                // 有动画时仍需做入场
                double h0 = ContentHost.ActualHeight > 0 ? ContentHost.ActualHeight : 600;
                SlideIn(_tabContents[newIndex], 1, h0);
                return;
            }

            // 无动画：直接切换可见性
            if (!_enableSettingsAnimations)
            {
                if (oldIndex >= 0 && oldIndex < _tabContents.Length)
                    _tabContents[oldIndex].Visibility = Visibility.Collapsed;
                _tabContents[newIndex].Visibility = Visibility.Visible;
                return;
            }

            // 方向：向下切 +1（新页从下方滑入，旧页向上滑出）
            int direction = oldIndex < 0 ? 1 : (newIndex > oldIndex ? 1 : -1);

            // 获取容器高度作为位移距离（保证新页在视口外起步）
            double panelHeight = ContentHost.ActualHeight > 0 ? ContentHost.ActualHeight : 600;

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
                    SlideOut(oldSv, direction, panelHeight);
                }
            }

            // 新页滑入（同步开始，同步时长）
            SlideIn(newSv, direction, panelHeight);
        }

        /// <summary>强制立即折叠并重置面板状态</summary>
        private static void SnapCollapse(ScrollViewer sv)
        {
            sv.BeginAnimation(UIElement.OpacityProperty, null);
            if (sv.RenderTransform is TranslateTransform tt)
                tt.BeginAnimation(TranslateTransform.YProperty, null);
            sv.Visibility = Visibility.Collapsed;
            sv.Opacity    = 1;
            if (sv.RenderTransform is TranslateTransform tt2) tt2.Y = 0;
        }

        // 纵向滑动动画（Tab 上下切换）
        private static readonly Duration SlideTime = new Duration(TimeSpan.FromSeconds(0.5));
        private static readonly IEasingFunction SlideEase =
            new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };

        /// <summary>新页面滑入：从视口外纵向平移 + 淡入</summary>
        private static void SlideIn(ScrollViewer sv, int direction, double height)
        {
            EnsureTranslate(sv);

            sv.BeginAnimation(UIElement.OpacityProperty, null);
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);

            double startY = direction >= 0 ? height : -height;
            ((TranslateTransform)sv.RenderTransform).Y = startY;
            sv.Opacity = 0;
            sv.Visibility = Visibility.Visible;

            var yAnim = new DoubleAnimation(startY, 0, SlideTime)
            {
                EasingFunction = SlideEase,
                FillBehavior = FillBehavior.Stop
            };
            var opacityAnim = new DoubleAnimation(0, 1, SlideTime)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            yAnim.Completed += (_, _) =>
            {
                ((TranslateTransform)sv.RenderTransform).Y = 0;
                ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
            };
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.YProperty, yAnim);
            sv.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        /// <summary>旧页面滑出：纵向平移 + 淡出，完成后折叠</summary>
        private void SlideOut(ScrollViewer sv, int direction, double height)
        {
            EnsureTranslate(sv);

            sv.BeginAnimation(UIElement.OpacityProperty, null);
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);

            double endY = direction >= 0 ? -height : height;
            ((TranslateTransform)sv.RenderTransform).Y = 0;
            sv.Opacity = 1;

            var yAnim = new DoubleAnimation(0, endY, SlideTime) { EasingFunction = SlideEase };
            var opacityAnim = new DoubleAnimation(1, 0, SlideTime)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            yAnim.Completed += (_, _) =>
            {
                if (_outgoingPanel == sv)
                {
                    SnapCollapse(sv);
                    _outgoingPanel = null;
                }
            };
            ((TranslateTransform)sv.RenderTransform).BeginAnimation(TranslateTransform.YProperty, yAnim);
            sv.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
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
            HideSubjectsBox.Text = _mainWindow.HideSubjects;

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
            RefreshCustomCountdownGrid();

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
            ScheduleBarFontSizeSlider.Value       = _mainWindow.ScheduleBarFontSize;
            ScheduleBarFontSizeText.Text          = _mainWindow.ScheduleBarFontSize.ToString("F0");

            // 下课倒计时
            for (int i = 0; i < CountdownExpandCb.Items.Count; i++)
            {
                if (CountdownExpandCb.Items[i] is ComboBoxItem item && item.Tag?.ToString() == _mainWindow.CountdownExpandSeconds.ToString())
                {
                    CountdownExpandCb.SelectedIndex = i;
                    break;
                }
            }
            EnableCountdownSoundCheck.IsChecked = _mainWindow.EnableCountdownSound;
            EnableReminderSoundCheck.IsChecked     = _mainWindow.EnableReminderSound;
            ReminderSoundPathBox.Text              = _mainWindow.ReminderSoundPath;
            RemindClassStartCheck.IsChecked        = _mainWindow.RemindClassStart;
            RemindClassMidCheck.IsChecked          = _mainWindow.RemindClassMid;
            RemindClassEndSoonCheck.IsChecked      = _mainWindow.RemindClassEndSoon;
            RemindClassEndCheck.IsChecked          = _mainWindow.RemindClassEnd;
            RemindNextClassSoonCheck.IsChecked     = _mainWindow.RemindNextClassSoon;
            RemindDayEndCheck.IsChecked            = _mainWindow.RemindDayEnd;
            RemindSpecialPeriodCheck.IsChecked     = _mainWindow.RemindSpecialPeriod;
            AutoCheckUpdateCheck.IsChecked        = _mainWindow.AutoCheckUpdate;

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

            ExamBackgroundColorBox.Text        = _mainWindow.ExamBackgroundColor;
            ExamProgressBarBgColorBox.Text     = _mainWindow.ExamProgressBarBgColor;
            ExamNextSubjectColorBox.Text       = _mainWindow.ExamNextSubjectColor;
            ExamWarningColorBox.Text           = _mainWindow.ExamWarningColor;
            ExamProgressPctColorBox.Text       = _mainWindow.ExamProgressPctColor;
            ExamInfoDimColorBox.Text           = _mainWindow.ExamInfoDimColor;
            RefreshColorPreview(ExamBackgroundColorBox,    ExamBackgroundColorPreview);
            RefreshColorPreview(ExamProgressBarBgColorBox, ExamProgressBarBgColorPreview);
            RefreshColorPreview(ExamNextSubjectColorBox,   ExamNextSubjectColorPreview);
            RefreshColorPreview(ExamWarningColorBox,       ExamWarningColorPreview);
            RefreshColorPreview(ExamProgressPctColorBox,   ExamProgressPctColorPreview);
            RefreshColorPreview(ExamInfoDimColorBox,       ExamInfoDimColorPreview);

            // 填充考试倒计时字体
            foreach (FontFamily ff in Fonts.SystemFontFamilies)
                ExamCountdownFontFamilyBox.Items.Add(new FontFamilyItem(ff));
            foreach (FontFamilyItem item in ExamCountdownFontFamilyBox.Items)
            {
                if (item.FontFamily.Source.Equals(_mainWindow.ExamCountdownFontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    ExamCountdownFontFamilyBox.SelectedItem = item;
                    break;
                }
            }

            // 填充课表 DataGrid
            var sm = _mainWindow.GetScheduleManager();
            if (sm != null)
            {
                PopulateTimeTemplateCombo();
                RefreshTimeTemplate();
                RefreshTimetable();
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
                MessageBox.Show("数字颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(TextColorBox.Text, out Color tc))
            {
                MessageBox.Show("文字颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(ProgressBarColorBox.Text, out Color pc))
            {
                MessageBox.Show("进度条颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
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
            _mainWindow.HideSubjects    = HideSubjectsBox.Text.Trim();

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
                MessageBox.Show("城市名颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherInfoColorBox.Text, out Color wic))
            {
                MessageBox.Show("天气信息颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherTempColorBox.Text, out Color wtc))
            {
                MessageBox.Show("温度颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherTimeColorBox.Text, out Color wtc2))
            {
                MessageBox.Show("更新时间颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
                                   "颜色格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseColor(WeatherIconColorBox.Text, out Color wico))
            {
                MessageBox.Show("天气图标颜色格式不正确，请使用 #RRGGBB 或 #AARRGGBB 格式。",
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
                MessageBox.Show("高考日期格式不正确，请使用 yyyy-MM-dd HH:mm:ss 格式。",
                                   "日期格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!DateTime.TryParse(StartDateBox.Text, out _))
            {
                MessageBox.Show("起算日期格式不正确，请使用 yyyy-MM-dd 格式。",
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

            // 下课倒计时
            if (CountdownExpandCb.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int sec))
                _mainWindow.CountdownExpandSeconds = sec;
            _mainWindow.EnableCountdownSound = EnableCountdownSoundCheck.IsChecked == true;
            _mainWindow.RemindDayEnd            = RemindDayEndCheck.IsChecked == true;
            _mainWindow.RemindSpecialPeriod     = RemindSpecialPeriodCheck.IsChecked == true;
            _mainWindow.AutoCheckUpdate          = AutoCheckUpdateCheck.IsChecked == true;

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

            // 考试模式颜色 — 保存前验证格式
            if (!ValidateExamColor(ExamSubjectColorBox.Text,          "科目文字颜色")) return;
            if (!ValidateExamColor(ExamNameColorBox.Text,             "考试名称颜色")) return;
            if (!ValidateExamColor(ExamCountdownNormalColorBox.Text,  "倒计时正常颜色")) return;
            if (!ValidateExamColor(ExamCountdownWarningColorBox.Text, "倒计时警告颜色")) return;
            if (!ValidateExamColor(ExamCountdownCriticalColorBox.Text,"倒计时紧迫颜色")) return;
            if (!ValidateExamColor(ExamDistanceColorBox.Text,         "距开考倒计时颜色")) return;
            if (!ValidateExamColor(ExamInfoColorBox.Text,             "信息文字颜色")) return;
            if (!ValidateExamColor(ExamProgressBarColorBox.Text,      "进度条颜色")) return;
            if (!ValidateExamColor(ExamBackgroundColorBox.Text,       "主窗口背景")) return;
            if (!ValidateExamColor(ExamProgressBarBgColorBox.Text,    "进度条背景")) return;
            if (!ValidateExamColor(ExamNextSubjectColorBox.Text,      "下一场文字")) return;
            if (!ValidateExamColor(ExamWarningColorBox.Text,          "警告文字")) return;
            if (!ValidateExamColor(ExamProgressPctColorBox.Text,      "百分比文字")) return;
            if (!ValidateExamColor(ExamInfoDimColorBox.Text,          "标签半透明")) return;

            _mainWindow.ExamSubjectColor          = ExamSubjectColorBox.Text.Trim();
            _mainWindow.ExamNameColor             = ExamNameColorBox.Text.Trim();
            _mainWindow.ExamCountdownNormalColor  = ExamCountdownNormalColorBox.Text.Trim();
            _mainWindow.ExamCountdownWarningColor = ExamCountdownWarningColorBox.Text.Trim();
            _mainWindow.ExamCountdownCriticalColor= ExamCountdownCriticalColorBox.Text.Trim();
            _mainWindow.ExamDistanceColor         = ExamDistanceColorBox.Text.Trim();
            _mainWindow.ExamInfoColor             = ExamInfoColorBox.Text.Trim();
            _mainWindow.ExamProgressBarColor      = ExamProgressBarColorBox.Text.Trim();
            _mainWindow.ExamBackgroundColor       = ExamBackgroundColorBox.Text.Trim();
            _mainWindow.ExamProgressBarBgColor    = ExamProgressBarBgColorBox.Text.Trim();
            _mainWindow.ExamNextSubjectColor      = ExamNextSubjectColorBox.Text.Trim();
            _mainWindow.ExamWarningColor          = ExamWarningColorBox.Text.Trim();
            _mainWindow.ExamProgressPctColor      = ExamProgressPctColorBox.Text.Trim();
            _mainWindow.ExamInfoDimColor          = ExamInfoDimColorBox.Text.Trim();

            // 考试字体
            var ffItem = ExamCountdownFontFamilyBox.SelectedItem as FontFamilyItem;
            if (ffItem != null)
                _mainWindow.ExamCountdownFontFamily = ffItem.FontFamily.Source;

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
            var result = MessageBox.Show(
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
    }
}
