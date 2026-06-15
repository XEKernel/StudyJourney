using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GaokaoCountdown
{
    /// <summary>
    /// 自定义提醒窗口：无边框、右下角滑入、自动消失。
    /// 根据提醒类型显示不同颜色图标和强调条。
    /// </summary>
    public partial class ReminderWindow : Window
    {
        private readonly DispatcherTimer _dismissTimer;
        private const double ShowDurationMs = 5000;  // 显示 5 秒
        private const double FadeOutDurationMs = 350;

        // 当前显示队列的管理（静态，支持多条通知叠放）
        private static ReminderWindow? _current;

        public ReminderWindow()
        {
            InitializeComponent();
            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ShowDurationMs)
            };
            _dismissTimer.Tick += OnDismissTick;

            // 鼠标悬停时暂停自动消失
            MouseEnter += (s, e) => _dismissTimer.Stop();
            MouseLeave += (s, e) =>
            {
                if (IsLoaded)
                    _dismissTimer.Start();
            };
        }

        /// <summary>
        /// 创建并显示一条提醒。自动管理叠放位置。
        /// </summary>
        public static void Show(string title, string message, ReminderType type)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 关闭之前的提醒窗口（如果还在显示）
                _current?.Dismiss();

                var win = new ReminderWindow();
                _current = win;

                // 设置内容
                var (icon, accentColor) = GetStyleForType(type);
                win.IconTb.Text = icon;
                win.TitleTb.Text = title;
                win.MessageTb.Text = message;

                win.AccentBar.Background = new SolidColorBrush(accentColor);

                // 定位到屏幕右下角
                win.PositionAtBottomRight();

                // 显示并启动自动消失计时
                win.Show();
                win._dismissTimer.Start();
            });
        }

        /// <summary>定位到主屏幕右下角</summary>
        private void PositionAtBottomRight()
        {
            // 先测量
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = DesiredSize;

            double sw = SystemParameters.WorkArea.Width;
            double sh = SystemParameters.WorkArea.Height;

            Left = sw - desired.Width - 16;
            Top = sh - desired.Height - 16;
        }

        /// <summary>平滑淡出 + 下滑并关闭</summary>
        private void Dismiss()
        {
            _dismissTimer.Stop();

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeOutDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (_current == this) _current = null;
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);

            // 同时向下滑出
            var slideDown = new DoubleAnimation(0, 30, TimeSpan.FromMilliseconds(FadeOutDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            RootBorder.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        private void OnDismissTick(object? sender, EventArgs e)
        {
            _dismissTimer.Stop();
            Dismiss();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Dismiss();
        }

        /// <summary>根据提醒类型返回图标和强调色</summary>
        private static (string icon, Color color) GetStyleForType(ReminderType type)
        {
            return type switch
            {
                ReminderType.ClassStart      => ("\U0001F4D6", (Color)ColorConverter.ConvertFromString("#6688CC")), // 📖 蓝
                ReminderType.ClassMid        => ("\u23F0",    (Color)ColorConverter.ConvertFromString("#CC8800")), // ⏰ 橙
                ReminderType.ClassEndSoon    => ("\u231B",    (Color)ColorConverter.ConvertFromString("#FF8800")), // ⌛ 深橙
                ReminderType.ClassEnd        => ("\u2705",    (Color)ColorConverter.ConvertFromString("#66CC88")), // ✅ 绿
                ReminderType.NextClassSoon   => ("\U0001F514",(Color)ColorConverter.ConvertFromString("#6688CC")), // 🔔 蓝
                ReminderType.DayEnd          => ("\U0001F389",(Color)ColorConverter.ConvertFromString("#8866CC")), // 🎉 紫
                ReminderType.MorningStart    => ("\u2600",    (Color)ColorConverter.ConvertFromString("#66CCCC")), // ☀ 青
                ReminderType.MorningEnd      => ("\U0001F31E",(Color)ColorConverter.ConvertFromString("#66CCCC")), // 🌞 青
                ReminderType.EveningStart    => ("\U0001F319",(Color)ColorConverter.ConvertFromString("#6666CC")), // 🌙 靛蓝
                ReminderType.EveningEnd      => ("\u2B50",    (Color)ColorConverter.ConvertFromString("#6666CC")), // ⭐ 靛蓝
                ReminderType.ReadingStart    => ("\U0001F4D5",(Color)ColorConverter.ConvertFromString("#66AA88")), // 📕 青绿
                ReminderType.ReadingEnd      => ("\U0001F4D6",(Color)ColorConverter.ConvertFromString("#66AA88")), // 📖 青绿
                ReminderType.ExamEndSoon     => ("\u26A0",    (Color)ColorConverter.ConvertFromString("#CC4444")), // ⚠ 红
                _                            => ("\U0001F514",(Color)ColorConverter.ConvertFromString("#6688CC")), // 🔔 默认蓝
            };
        }
    }
}
