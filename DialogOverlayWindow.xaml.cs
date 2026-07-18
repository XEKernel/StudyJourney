using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GaokaoCountdown
{
    public partial class DialogOverlayWindow : Window
    {
        private bool _isClosing;

        public DialogOverlayWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var scaleIn = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                BeginAnimation(OpacityProperty, fadeIn);
                if (RootBorder.RenderTransform is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
                }
                if (ContentHost.Content is FrameworkElement fe) fe.Focus();
            };
        }

        public void SetContent(object content)
        {
            ContentHost.Content = content;
        }

        public void CloseWithFade()
        {
            if (_isClosing) return;
            _isClosing = true;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleOut = new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
            if (RootBorder.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
            }
        }
    }
}
