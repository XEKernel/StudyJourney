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
        private readonly AppSettings _settings;   // 直接引用设置模型，替代 _mainWindow 代理属性

        // 运行时动画状态
        private bool _enableSettingsAnimations = true;
        private bool _isInitializing = true;   // 抑制初始加载时的 Tab 动画
        private bool _isInitialized = false;   // 防重复初始化
        private ScrollViewer[]? _tabContents;  // 索引 → 内容面板

        public SettingWindow(MainWindow window)
        {
            InitializeComponent();
            _mainWindow = window;
            _settings = window.GetSettings();
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
            ChinesePrefixText.Text  = _settings.ChinesePrefix;
            ChineseDaysText.Text    = _settings.ChineseDaysText;
            ChineseHoursText.Text   = _settings.ChineseHoursText;
            ChineseMinutesText.Text = _settings.ChineseMinutesText;
            ChineseSecondsText.Text = _settings.ChineseSecondsText;

            EnglishPrefixText.Text  = _settings.EnglishPrefix;
            EnglishDaysText.Text    = _settings.EnglishDaysText;
            EnglishHoursText.Text   = _settings.EnglishHoursText;
            EnglishMinutesText.Text = _settings.EnglishMinutesText;
            EnglishSecondsText.Text = _settings.EnglishSecondsText;

            // ── 外观 ──────────────────────────────────────────
            FontSizeSlider.Value = _settings.FontSize;
            FontSizeText.Text    = _settings.FontSize.ToString();

            OpacitySlider.Value = _settings.OverallOpacity;
            OpacityText.Text    = $"{_settings.OverallOpacity * 100:F0}%";

            NumberColorBox.Text      = ColorToHex(_settings.NumberColor);
            TextColorBox.Text        = ColorToHex(_settings.TextColor);
            ProgressBarColorBox.Text = ColorToHex(_settings.ProgressBarColor);
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
                case PositionPresetValues.Top:         PosTop.IsChecked         = true; break;
                case PositionPresetValues.UpperCenter: PosUpperCenter.IsChecked = true; break;
                case PositionPresetValues.Center:      PosCenter.IsChecked      = true; break;
                case PositionPresetValues.LowerCenter: PosLowerCenter.IsChecked = true; break;
                case PositionPresetValues.Bottom:      PosBottom.IsChecked      = true; break;
                case PositionPresetValues.Custom:      PosCustom.IsChecked      = true; break;
                default: PosUpperCenter.IsChecked = true; break;
            }

            CustomXBox.Text = _settings.CustomPositionX.ToString("F0");
            CustomYBox.Text = _settings.CustomPositionY.ToString("F0");
            OffsetYBox.Text = _settings.PositionOffsetY.ToString("F0");
            AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
            AutoStartCheck.IsChecked   = MainWindow.GetAutoStartFromRegistry();
            HideWhenMaximizedCheck.IsChecked = _settings.HideWhenMaximized;
            HideDuringClassCheck.IsChecked = _settings.HideDuringClass;
            HideSubjectsBox.Text = _settings.HideSubjects;

            // ── 显示 ──────────────────────────────────────────
            ShowEnglishCheck.IsChecked      = _settings.ShowEnglishLine;
            ShowProgressBarCheck.IsChecked  = _settings.ShowProgressBar;
            ShowProgressTextCheck.IsChecked = _settings.ShowProgressText;
            ShowDaysCheck.IsChecked         = _settings.ShowDays;
            ShowHoursCheck.IsChecked        = _settings.ShowHours;
            ShowMinutesCheck.IsChecked      = _settings.ShowMinutes;
            ShowSecondsCheck.IsChecked      = _settings.ShowSeconds;
            DecimalSlider.Value = _settings.ProgressDecimalDigits;
            DecimalText.Text    = _settings.ProgressDecimalDigits.ToString();

            // ── 日期 ──────────────────────────────────────────
            GaokaoDateBox.Text = _settings.GaokaoDateStr;
            StartDateBox.Text  = _settings.StartDateStr;
            RefreshCustomCountdownGrid();

            // ── 动画 ──────────────────────────────────────────
            EnableAnimationsCheck.IsChecked = _settings.EnableAnimations;
            var settingsAnim = _settings.EnableAnimations;
            _enableSettingsAnimations = settingsAnim;
            EnableSettingsAnimationsCheck.IsChecked = settingsAnim;

            // ── 每日一言 ──────────────────────────────────────
            ShowDailyQuoteCheck.IsChecked      = _settings.ShowDailyQuote;
            QuoteFontSizeSlider.Value          = _settings.QuoteFontSize;
            QuoteFontSizeText.Text             = _settings.QuoteFontSize.ToString("F0");
            QuoteForegroundBox.Text            = _settings.QuoteForegroundHex;
            QuoteItalicCheck.IsChecked         = _settings.QuoteItalic;
            QuoteApiUrlBox.Text                = _settings.QuoteApiUrl;
            QuoteTextFieldNameBox.Text          = _settings.QuoteTextFieldName;
            QuoteRefreshIntervalSlider.Value   = _settings.QuoteAutoRefreshInterval;
            QuoteRefreshIntervalText.Text      = _settings.QuoteAutoRefreshInterval == 0
                ? "关" : $"{_settings.QuoteAutoRefreshInterval}s";

            // ── 课表栏 ────────────────────────────────────────
            ShowScheduleBarCheck.IsChecked         = _settings.ShowScheduleBar;
            ScheduleBarAlwaysOnTopCheck.IsChecked  = _settings.ScheduleBarAlwaysOnTop;
            ScheduleBarClickThroughCheck.IsChecked = _settings.ScheduleBarClickThrough;
            ScheduleBarAutoCollapseCheck.IsChecked = _settings.ScheduleBarAutoCollapse;
            ScheduleBarOpacitySlider.Value         = _settings.ScheduleBarOpacity;
            ScheduleBarOpacityLabel.Text           = $"{_settings.ScheduleBarOpacity * 100:F0}%";
            ScheduleBarWidthBox.Text               = _settings.ScheduleBarWidth.ToString("F0");
            ScheduleBarFontSizeSlider.Value       = _settings.ScheduleBarFontSize;
            ScheduleBarFontSizeText.Text          = _settings.ScheduleBarFontSize.ToString("F0");

            // 下课倒计时
            for (int i = 0; i < CountdownExpandCb.Items.Count; i++)
            {
                if (CountdownExpandCb.Items[i] is ComboBoxItem item && item.Tag?.ToString() == _settings.CountdownExpandSeconds.ToString())
                {
                    CountdownExpandCb.SelectedIndex = i;
                    break;
                }
            }
            EnableCountdownSoundCheck.IsChecked = _settings.EnableCountdownSound;
            EnableReminderSoundCheck.IsChecked     = _settings.EnableReminderSound;
            ReminderSoundPathBox.Text              = _settings.ReminderSoundPath;
            RemindClassStartCheck.IsChecked        = _settings.RemindClassStart;
            RemindClassMidCheck.IsChecked          = _settings.RemindClassMid;
            RemindClassEndSoonCheck.IsChecked      = _settings.RemindClassEndSoon;
            RemindClassEndCheck.IsChecked          = _settings.RemindClassEnd;
            RemindNextClassSoonCheck.IsChecked     = _settings.RemindNextClassSoon;
            RemindDayEndCheck.IsChecked            = _settings.RemindDayEnd;
            RemindSpecialPeriodCheck.IsChecked     = _settings.RemindSpecialPeriod;
            AutoCheckUpdateCheck.IsChecked        = _settings.AutoCheckUpdate;

            // ── 考试模式 ──────────────────────────────────────
            EnableExamModeCheck.IsChecked   = _settings.EnableExamMode;
            AutoEnterExamModeCheck.IsChecked = _settings.AutoEnterExamMode;
            ExamModeFontSizeSlider.Value     = _settings.ExamModeFontSize;
            ExamModeFontSizeText.Text        = _settings.ExamModeFontSize.ToString("F0");

            // ── 考试模式样式 ──────────────────────────────────
            ExamSubjectFontSizeSlider.Value     = _settings.ExamSubjectFontSize;
            ExamSubjectFontSizeText.Text        = _settings.ExamSubjectFontSize.ToString("F0");
            ExamNameFontSizeSlider.Value        = _settings.ExamNameFontSize;
            ExamNameFontSizeText.Text           = _settings.ExamNameFontSize.ToString("F0");
            ExamCountdownFontSizeSlider.Value   = _settings.ExamCountdownFontSize;
            ExamCountdownFontSizeText.Text      = _settings.ExamCountdownFontSize.ToString("F0");
            ExamTimeInfoFontSizeSlider.Value    = _settings.ExamTimeInfoFontSize;
            ExamTimeInfoFontSizeText.Text       = _settings.ExamTimeInfoFontSize.ToString("F0");
            ExamNextSubjectFontSizeSlider.Value = _settings.ExamNextSubjectFontSize;
            ExamNextSubjectFontSizeText.Text    = _settings.ExamNextSubjectFontSize.ToString("F0");
            ExamWarningFontSizeSlider.Value     = _settings.ExamWarningFontSize;
            ExamWarningFontSizeText.Text        = _settings.ExamWarningFontSize.ToString("F0");
            ExamEscHintFontSizeSlider.Value     = _settings.ExamEscHintFontSize;
            ExamEscHintFontSizeText.Text        = _settings.ExamEscHintFontSize.ToString("F0");
            ExamProgressBarHeightSlider.Value   = _settings.ExamProgressBarHeight;
            ExamProgressBarHeightText.Text      = _settings.ExamProgressBarHeight.ToString("F0");

            ExamSubjectColorBox.Text           = _settings.ExamSubjectColor;
            ExamNameColorBox.Text              = _settings.ExamNameColor;
            ExamCountdownNormalColorBox.Text   = _settings.ExamCountdownNormalColor;
            ExamCountdownWarningColorBox.Text  = _settings.ExamCountdownWarningColor;
            ExamCountdownCriticalColorBox.Text = _settings.ExamCountdownCriticalColor;
            ExamDistanceColorBox.Text          = _settings.ExamDistanceColor;
            ExamInfoColorBox.Text              = _settings.ExamInfoColor;
            ExamProgressBarColorBox.Text       = _settings.ExamProgressBarColor;
            RefreshColorPreview(ExamSubjectColorBox,          ExamSubjectColorPreview);
            RefreshColorPreview(ExamNameColorBox,             ExamNameColorPreview);
            RefreshColorPreview(ExamCountdownNormalColorBox,  ExamCountdownNormalColorPreview);
            RefreshColorPreview(ExamCountdownWarningColorBox, ExamCountdownWarningColorPreview);
            RefreshColorPreview(ExamCountdownCriticalColorBox,ExamCountdownCriticalColorPreview);
            RefreshColorPreview(ExamDistanceColorBox,         ExamDistanceColorPreview);
            RefreshColorPreview(ExamInfoColorBox,             ExamInfoColorPreview);
            RefreshColorPreview(ExamProgressBarColorBox,      ExamProgressBarColorPreview);

            ExamBackgroundColorBox.Text        = _settings.ExamBackgroundColor;
            ExamProgressBarBgColorBox.Text     = _settings.ExamProgressBarBgColor;
            ExamNextSubjectColorBox.Text       = _settings.ExamNextSubjectColor;
            ExamWarningColorBox.Text           = _settings.ExamWarningColor;
            ExamProgressPctColorBox.Text       = _settings.ExamProgressPctColor;
            ExamInfoDimColorBox.Text           = _settings.ExamInfoDimColor;
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
                if (item.FontFamily.Source.Equals(_settings.ExamCountdownFontFamily, StringComparison.OrdinalIgnoreCase))
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
            WeatherCityBox.Text                 = _settings.WeatherCity;
            WeatherAdcodeBox.Text               = _settings.WeatherAdcode;
            WeatherFontSizeSlider.Value         = _settings.WeatherFontSize;
            WeatherFontSizeText.Text            = _settings.WeatherFontSize.ToString("F0");
            WeatherRefreshIntervalSlider.Value  = _settings.WeatherRefreshInterval;
            WeatherRefreshIntervalText.Text     = _settings.WeatherRefreshInterval == 0
                ? "关" : $"{_settings.WeatherRefreshInterval}min";

            // 天气文字颜色
            WeatherCityColorBox.Text      = _settings.WeatherCityColor;
            WeatherInfoColorBox.Text      = _settings.WeatherInfoColor;
            WeatherTempColorBox.Text      = _settings.WeatherTempColor;
            WeatherTimeColorBox.Text      = _settings.WeatherTimeColor;
            WeatherIconColorBox.Text      = _settings.WeatherIconColor;
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
            _settings.ChinesePrefix      = ChinesePrefixText.Text;
            _settings.ChineseDaysText    = ChineseDaysText.Text;
            _settings.ChineseHoursText   = ChineseHoursText.Text;
            _settings.ChineseMinutesText = ChineseMinutesText.Text;
            _settings.ChineseSecondsText = ChineseSecondsText.Text;

            _settings.EnglishPrefix      = EnglishPrefixText.Text;
            _settings.EnglishDaysText    = EnglishDaysText.Text;
            _settings.EnglishHoursText   = EnglishHoursText.Text;
            _settings.EnglishMinutesText = EnglishMinutesText.Text;
            _settings.EnglishSecondsText = EnglishSecondsText.Text;

            // ── 字体 ──────────────────────────────────────────
            _settings.FontSize = (int)FontSizeSlider.Value;
            if (FontFamilyComboBox.SelectedItem is FontFamilyItem selectedFont)
                _mainWindow.CountdownFontFamily = selectedFont.FontFamily;

            // ── 透明度 ────────────────────────────────────────
            _settings.OverallOpacity = OpacitySlider.Value;

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
            _settings.NumberColor      = nc;
            _settings.TextColor        = tc;
            _settings.ProgressBarColor = pc;

            // ── 位置 ──────────────────────────────────────────
            _mainWindow.PositionPreset =
                PosTop.IsChecked == true         ? PositionPresetValues.Top :
                PosUpperCenter.IsChecked == true ? PositionPresetValues.UpperCenter :
                PosCenter.IsChecked == true      ? PositionPresetValues.Center :
                PosLowerCenter.IsChecked == true ? PositionPresetValues.LowerCenter :
                PosBottom.IsChecked == true      ? PositionPresetValues.Bottom :
                PosCustom.IsChecked == true      ? PositionPresetValues.Custom : PositionPresetValues.UpperCenter;

            if (double.TryParse(CustomXBox.Text, out double cx)) _settings.CustomPositionX = cx;
            if (double.TryParse(CustomYBox.Text, out double cy)) _settings.CustomPositionY = cy;
            if (double.TryParse(OffsetYBox.Text, out double oy)) _settings.PositionOffsetY = oy;

            _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
            // AutoStart 在 CheckBox 事件中实时写注册表，此处同步 settings 字段即可
            _mainWindow.AutoStart   = AutoStartCheck.IsChecked == true;
            // HideWhenMaximized 在 CheckBox 事件中实时生效，此处同步 settings 字段
            _settings.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
            _settings.HideDuringClass = HideDuringClassCheck.IsChecked == true;
            _settings.HideSubjects    = HideSubjectsBox.Text.Trim();

            // ── 显示 ──────────────────────────────────────────
            _settings.ShowEnglishLine       = ShowEnglishCheck.IsChecked == true;
            _settings.ShowProgressBar       = ShowProgressBarCheck.IsChecked == true;
            _settings.ShowProgressText      = ShowProgressTextCheck.IsChecked == true;
            _settings.ShowDays              = ShowDaysCheck.IsChecked == true;
            _settings.ShowHours             = ShowHoursCheck.IsChecked == true;
            _settings.ShowMinutes           = ShowMinutesCheck.IsChecked == true;
            _settings.ShowSeconds           = ShowSecondsCheck.IsChecked == true;
            _settings.ProgressDecimalDigits = (int)DecimalSlider.Value;

            // ── 动画 ──────────────────────────────────────────
            _settings.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
            _enableSettingsAnimations    = EnableSettingsAnimationsCheck.IsChecked == true;

            // ── 每日一言 ──────────────────────────────────────
            _settings.ShowDailyQuote          = ShowDailyQuoteCheck.IsChecked == true;
            _settings.QuoteFontSize           = QuoteFontSizeSlider.Value;
            _settings.QuoteForegroundHex       = QuoteForegroundBox.Text.Trim();
            _settings.QuoteItalic             = QuoteItalicCheck.IsChecked == true;
            _settings.QuoteApiUrl             = QuoteApiUrlBox.Text.Trim();
            _settings.QuoteTextFieldName      = QuoteTextFieldNameBox.Text.Trim();
            _settings.QuoteAutoRefreshInterval = (int)QuoteRefreshIntervalSlider.Value;

            // 应用样式到主窗口
            _mainWindow.ApplyQuoteStyle();
            // 更新自动切换定时器
            _mainWindow.StartQuoteRefreshTimer();
            // 如果开关打开，立即加载一条
            if (_settings.ShowDailyQuote)
                _ = _mainWindow.RefreshQuoteAsync();

            // ── 天气 ──────────────────────────────────────────
            _settings.WeatherCity          = WeatherCityBox.Text.Trim();
            _settings.WeatherAdcode        = WeatherAdcodeBox.Text.Trim();


            _settings.WeatherFontSize     = WeatherFontSizeSlider.Value;

            _settings.WeatherRefreshInterval = (int)WeatherRefreshIntervalSlider.Value;

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
            _settings.WeatherCityColor  = WeatherCityColorBox.Text.Trim();
            _settings.WeatherInfoColor  = WeatherInfoColorBox.Text.Trim();
            _settings.WeatherTempColor  = WeatherTempColorBox.Text.Trim();
            _settings.WeatherTimeColor  = WeatherTimeColorBox.Text.Trim();
            _settings.WeatherIconColor  = WeatherIconColorBox.Text.Trim();


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
            _settings.GaokaoDateStr = GaokaoDateBox.Text.Trim();
            _settings.StartDateStr  = StartDateBox.Text.Trim();
            _mainWindow.RefreshDateFields();

            // ── 应用窗口层级 ──────────────────────────────────
            _mainWindow.ApplyWindowLayer();

            // ── 刷新主窗口显示 ────────────────────────────────
            _mainWindow.UpdateCountdownDisplay();

            // ── 课表栏设置 ────────────────────────────────────
            _settings.ShowScheduleBar         = ShowScheduleBarCheck.IsChecked == true;
            _settings.ScheduleBarAlwaysOnTop  = ScheduleBarAlwaysOnTopCheck.IsChecked == true;
            _settings.ScheduleBarClickThrough = ScheduleBarClickThroughCheck.IsChecked == true;
            _settings.ScheduleBarAutoCollapse = ScheduleBarAutoCollapseCheck.IsChecked == true;
            _settings.ScheduleBarOpacity      = ScheduleBarOpacitySlider.Value;
            if (double.TryParse(ScheduleBarWidthBox.Text, out double sbw)) _settings.ScheduleBarWidth = sbw;
            _settings.ScheduleBarFontSize     = ScheduleBarFontSizeSlider.Value;
            _settings.EnableReminderSound     = EnableReminderSoundCheck.IsChecked == true;
            _settings.ReminderSoundPath       = ReminderSoundPathBox.Text.Trim();
            _settings.RemindClassStart        = RemindClassStartCheck.IsChecked == true;
            _settings.RemindClassMid          = RemindClassMidCheck.IsChecked == true;
            _settings.RemindClassEndSoon      = RemindClassEndSoonCheck.IsChecked == true;
            _settings.RemindClassEnd          = RemindClassEndCheck.IsChecked == true;
            _settings.RemindNextClassSoon     = RemindNextClassSoonCheck.IsChecked == true;

            // 下课倒计时
            if (CountdownExpandCb.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int sec))
                _settings.CountdownExpandSeconds = sec;
            _settings.EnableCountdownSound = EnableCountdownSoundCheck.IsChecked == true;
            _settings.RemindDayEnd            = RemindDayEndCheck.IsChecked == true;
            _settings.RemindSpecialPeriod     = RemindSpecialPeriodCheck.IsChecked == true;
            _settings.AutoCheckUpdate          = AutoCheckUpdateCheck.IsChecked == true;

            // ── 考试模式 ──────────────────────────────────────
            _settings.EnableExamMode    = EnableExamModeCheck.IsChecked == true;
            _settings.AutoEnterExamMode = AutoEnterExamModeCheck.IsChecked == true;
            _settings.ExamModeFontSize  = ExamModeFontSizeSlider.Value;

            // ── 考试模式样式 ──────────────────────────────────
            _settings.ExamSubjectFontSize       = ExamSubjectFontSizeSlider.Value;
            _settings.ExamNameFontSize          = ExamNameFontSizeSlider.Value;
            _settings.ExamCountdownFontSize     = ExamCountdownFontSizeSlider.Value;
            _settings.ExamTimeInfoFontSize      = ExamTimeInfoFontSizeSlider.Value;
            _settings.ExamNextSubjectFontSize   = ExamNextSubjectFontSizeSlider.Value;
            _settings.ExamWarningFontSize       = ExamWarningFontSizeSlider.Value;
            _settings.ExamEscHintFontSize       = ExamEscHintFontSizeSlider.Value;
            _settings.ExamProgressBarHeight     = ExamProgressBarHeightSlider.Value;

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

            _settings.ExamSubjectColor          = ExamSubjectColorBox.Text.Trim();
            _settings.ExamNameColor             = ExamNameColorBox.Text.Trim();
            _settings.ExamCountdownNormalColor  = ExamCountdownNormalColorBox.Text.Trim();
            _settings.ExamCountdownWarningColor = ExamCountdownWarningColorBox.Text.Trim();
            _settings.ExamCountdownCriticalColor= ExamCountdownCriticalColorBox.Text.Trim();
            _settings.ExamDistanceColor         = ExamDistanceColorBox.Text.Trim();
            _settings.ExamInfoColor             = ExamInfoColorBox.Text.Trim();
            _settings.ExamProgressBarColor      = ExamProgressBarColorBox.Text.Trim();
            _settings.ExamBackgroundColor       = ExamBackgroundColorBox.Text.Trim();
            _settings.ExamProgressBarBgColor    = ExamProgressBarBgColorBox.Text.Trim();
            _settings.ExamNextSubjectColor      = ExamNextSubjectColorBox.Text.Trim();
            _settings.ExamWarningColor          = ExamWarningColorBox.Text.Trim();
            _settings.ExamProgressPctColor      = ExamProgressPctColorBox.Text.Trim();
            _settings.ExamInfoDimColor          = ExamInfoDimColorBox.Text.Trim();

            // 考试字体
            var ffItem = ExamCountdownFontFamilyBox.SelectedItem as FontFamilyItem;
            if (ffItem != null)
                _settings.ExamCountdownFontFamily = ffItem.FontFamily.Source;

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
            _settings.ChinesePrefix      = defaults.ChinesePrefix;
            _settings.ChineseDaysText    = defaults.ChineseDaysText;
            _settings.ChineseHoursText   = defaults.ChineseHoursText;
            _settings.ChineseMinutesText = defaults.ChineseMinutesText;
            _settings.ChineseSecondsText = defaults.ChineseSecondsText;
            _settings.EnglishPrefix      = defaults.EnglishPrefix;
            _settings.EnglishDaysText    = defaults.EnglishDaysText;
            _settings.EnglishHoursText   = defaults.EnglishHoursText;
            _settings.EnglishMinutesText = defaults.EnglishMinutesText;
            _settings.EnglishSecondsText = defaults.EnglishSecondsText;
            _mainWindow.CountdownFontFamily = new FontFamily(defaults.FontFamily);
            _settings.FontSize   = defaults.FontSize;
            _settings.NumberColor         = defaults.NumberColor;
            _settings.TextColor           = defaults.TextColor;
            _settings.ProgressBarColor    = defaults.ProgressBarColor;
            _settings.OverallOpacity      = defaults.OverallOpacity;
            _settings.ShowEnglishLine     = defaults.ShowEnglishLine;
            _settings.ShowProgressBar     = defaults.ShowProgressBar;
            _settings.ShowProgressText    = defaults.ShowProgressText;
            _settings.ShowDays            = defaults.ShowDays;
            _settings.ShowHours           = defaults.ShowHours;
            _settings.ShowMinutes         = defaults.ShowMinutes;
            _settings.ShowSeconds         = defaults.ShowSeconds;
            _mainWindow.PositionPreset      = defaults.PositionPreset;
            _settings.CustomPositionX     = defaults.CustomPositionX;
            _settings.CustomPositionY     = defaults.CustomPositionY;
            _settings.PositionOffsetY     = defaults.PositionOffsetY;
            _settings.AlwaysOnTop         = defaults.AlwaysOnTop;
            _mainWindow.AutoStart           = defaults.AutoStart;  // 默认 false → 删除注册表项
            _settings.HideWhenMaximized   = defaults.HideWhenMaximized;
            _settings.HideDuringClass     = defaults.HideDuringClass;
            _settings.GaokaoDateStr       = defaults.GaokaoDateStr;
            _settings.StartDateStr        = defaults.StartDateStr;
            _settings.ProgressDecimalDigits = defaults.ProgressDecimalDigits;
            _settings.EnableAnimations    = defaults.EnableAnimations;
            _enableSettingsAnimations       = true;
            _settings.ShowDailyQuote            = defaults.ShowDailyQuote;
            _settings.QuoteFontSize             = defaults.QuoteFontSize;
            _settings.QuoteForegroundHex        = defaults.QuoteForegroundHex;
            _settings.QuoteItalic               = defaults.QuoteItalic;
            _settings.QuoteApiUrl               = defaults.QuoteApiUrl;
            _settings.QuoteTextFieldName        = defaults.QuoteTextFieldName;
            _settings.QuoteAutoRefreshInterval   = defaults.QuoteAutoRefreshInterval;
            _settings.WeatherCity              = defaults.WeatherCity;
            _settings.WeatherAdcode            = defaults.WeatherAdcode;
            _settings.WeatherFontSize          = defaults.WeatherFontSize;
            _settings.WeatherRefreshInterval   = defaults.WeatherRefreshInterval;
            _settings.WeatherCityColor        = defaults.WeatherCityColor;
            _settings.WeatherInfoColor        = defaults.WeatherInfoColor;
            _settings.WeatherTempColor        = defaults.WeatherTempColor;
            _settings.WeatherTimeColor        = defaults.WeatherTimeColor;
            _settings.WeatherIconColor        = defaults.WeatherIconColor;
            _settings.ScheduleBarFontSize     = defaults.ScheduleBarFontSize;
            _settings.ScheduleBarAutoCollapse = defaults.ScheduleBarAutoCollapse;
            _settings.ExamModeFontSize        = defaults.ExamModeFontSize;
            _settings.ExamSubjectFontSize       = defaults.ExamSubjectFontSize;
            _settings.ExamNameFontSize          = defaults.ExamNameFontSize;
            _settings.ExamCountdownFontSize     = defaults.ExamCountdownFontSize;
            _settings.ExamTimeInfoFontSize      = defaults.ExamTimeInfoFontSize;
            _settings.ExamNextSubjectFontSize   = defaults.ExamNextSubjectFontSize;
            _settings.ExamWarningFontSize       = defaults.ExamWarningFontSize;
            _settings.ExamEscHintFontSize       = defaults.ExamEscHintFontSize;
            _settings.ExamProgressBarHeight     = defaults.ExamProgressBarHeight;
            _settings.ExamSubjectColor          = defaults.ExamSubjectColor;
            _settings.ExamNameColor             = defaults.ExamNameColor;
            _settings.ExamCountdownNormalColor  = defaults.ExamCountdownNormalColor;
            _settings.ExamCountdownWarningColor = defaults.ExamCountdownWarningColor;
            _settings.ExamCountdownCriticalColor= defaults.ExamCountdownCriticalColor;
            _settings.ExamDistanceColor         = defaults.ExamDistanceColor;
            _settings.ExamInfoColor             = defaults.ExamInfoColor;
            _settings.ExamProgressBarColor      = defaults.ExamProgressBarColor;
            _settings.ExamProgressBarBgColor    = defaults.ExamProgressBarBgColor;
            _settings.ExamBackgroundColor       = defaults.ExamBackgroundColor;
            _settings.ExamNextSubjectColor      = defaults.ExamNextSubjectColor;
            _settings.ExamWarningColor          = defaults.ExamWarningColor;
            _settings.ExamProgressPctColor      = defaults.ExamProgressPctColor;
            _settings.ExamCountdownFontFamily   = defaults.ExamCountdownFontFamily;
            _settings.ExamInfoDimColor          = defaults.ExamInfoDimColor;
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
            _settings.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
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
