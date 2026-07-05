using System.Windows;
using System.Windows.Input;

namespace GaokaoCountdown
{
    public enum ThemeMessageBoxButton { OK, YesNo, YesNoCancel }
    public enum ThemeMessageBoxIcon { None, Info, Warning, Error, Question }
    public enum ThemeMessageBoxResult { OK, Yes, No, Cancel }

    public partial class ThemedMessageBox : Window
    {
        public bool? Result { get; private set; }

        public ThemedMessageBox(string title, string message,
            ThemeMessageBoxButton buttons = ThemeMessageBoxButton.OK,
            ThemeMessageBoxIcon icon = ThemeMessageBoxIcon.None)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;

            switch (icon)
            {
                case ThemeMessageBoxIcon.Warning: IconText.Text = "⚠"; IconText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xCC, 0x44)); break;
                case ThemeMessageBoxIcon.Error:   IconText.Text = "✕"; IconText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x44, 0x44)); break;
                case ThemeMessageBoxIcon.Info:    IconText.Text = "ℹ"; IconText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x88, 0xCC)); break;
                case ThemeMessageBoxIcon.Question:IconText.Text = "?"; IconText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0xCC, 0x88)); break;
                default: IconText.Visibility = Visibility.Collapsed; break;
            }

            switch (buttons)
            {
                case ThemeMessageBoxButton.OK:
                    BtnOk.Visibility = Visibility.Visible;
                    BtnOk.Click += (_, _) => { Result = true; Close(); };
                    break;
                case ThemeMessageBoxButton.YesNo:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnYes.Click += (_, _) => { Result = true; Close(); };
                    BtnNo.Click += (_, _) => { Result = false; Close(); };
                    break;
                case ThemeMessageBoxButton.YesNoCancel:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.Click += (_, _) => { Result = true; Close(); };
                    BtnNo.Click += (_, _) => { Result = false; Close(); };
                    BtnCancel.Click += (_, _) => { Result = null; Close(); };
                    break;
            }

            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
