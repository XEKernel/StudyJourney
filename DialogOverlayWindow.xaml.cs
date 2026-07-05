using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GaokaoCountdown
{
    public partial class DialogOverlayWindow : Window
    {
        public DialogOverlayWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);
                if (ContentHost.Content is FrameworkElement fe) fe.Focus();
            };
        }

        public void SetContent(object content)
        {
            ContentHost.Content = content;
        }

        public void CloseWithFade()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
