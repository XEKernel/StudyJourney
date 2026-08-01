using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using GaokaoCountdown.Helpers;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class MessageBoxControl : UserControl
    {
        public bool? Result { get; private set; }
        private readonly Action? _close;

        public MessageBoxControl(string title, string message,
            ThemeMessageBoxButton buttons, ThemeMessageBoxIcon icon, Action close)
        {
            InitializeComponent();
            _close = close;
            TitleText.Text = title;
            MessageText.Text = message;

            switch (icon)
            {
                case ThemeMessageBoxIcon.Warning: IconText.Text = "⚠"; IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44)); break;
                case ThemeMessageBoxIcon.Error: IconText.Text = "✕"; IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)); break;
                case ThemeMessageBoxIcon.Info: IconText.Text = "ℹ"; IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xCC)); break;
                case ThemeMessageBoxIcon.Question: IconText.Text = "?"; IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0x88)); break;
                default: IconText.Visibility = Visibility.Collapsed; break;
            }

            switch (buttons)
            {
                case ThemeMessageBoxButton.OK:
                    BtnOk.Visibility = Visibility.Visible;
                    BtnOk.Click += (_, _) => { Result = true; _close?.Invoke(); };
                    break;
                case ThemeMessageBoxButton.YesNo:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnYes.Click += (_, _) => { Result = true; _close?.Invoke(); };
                    BtnNo.Click += (_, _) => { Result = false; _close?.Invoke(); };
                    break;
                case ThemeMessageBoxButton.YesNoCancel:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.Click += (_, _) => { Result = true; _close?.Invoke(); };
                    BtnNo.Click += (_, _) => { Result = false; _close?.Invoke(); };
                    BtnCancel.Click += (_, _) => { Result = null; _close?.Invoke(); };
                    break;
            }

            KeyDown += (_, e) => { if (e.Key == Key.Escape) { Result = null; _close?.Invoke(); } };
        }
    }
}
