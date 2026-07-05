using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GaokaoCountdown
{
    public partial class ColorPickerControl : UserControl
    {
        public Color? SelectedColor { get; private set; }
        private readonly Action? _close;

        private static readonly Color[] _palette = new[]
        {
            Color.FromRgb(0xFF,0x00,0x00), Color.FromRgb(0xFF,0x44,0x00), Color.FromRgb(0xFF,0x88,0x00),
            Color.FromRgb(0xFF,0xCC,0x00), Color.FromRgb(0xFF,0xFF,0x00), Color.FromRgb(0xCC,0xFF,0x00),
            Color.FromRgb(0x88,0xFF,0x00), Color.FromRgb(0x44,0xFF,0x00), Color.FromRgb(0x00,0xFF,0x00),
            Color.FromRgb(0x00,0xFF,0x44), Color.FromRgb(0x00,0xFF,0x88), Color.FromRgb(0x00,0xFF,0xCC),
            Color.FromRgb(0x00,0xFF,0xFF), Color.FromRgb(0x00,0xCC,0xFF), Color.FromRgb(0x00,0x88,0xFF),
            Color.FromRgb(0x00,0x44,0xFF), Color.FromRgb(0x00,0x00,0xFF), Color.FromRgb(0x44,0x00,0xFF),
            Color.FromRgb(0x88,0x00,0xFF), Color.FromRgb(0xCC,0x00,0xFF), Color.FromRgb(0xFF,0x00,0xFF),
            Color.FromRgb(0xFF,0x00,0xCC), Color.FromRgb(0xFF,0x00,0x88), Color.FromRgb(0xFF,0x00,0x44),
            Color.FromRgb(0xFF,0xFF,0xFF), Color.FromRgb(0xCC,0xCC,0xCC), Color.FromRgb(0x99,0x99,0x99),
            Color.FromRgb(0x66,0x66,0x66), Color.FromRgb(0x33,0x33,0x33), Color.FromRgb(0x00,0x00,0x00),
            Color.FromRgb(0xBB,0xDD,0xFF), Color.FromRgb(0xFF,0xCC,0x88), Color.FromRgb(0xFF,0xDD,0xAA),
            Color.FromRgb(0xCC,0xDD,0xEE), Color.FromRgb(0xDD,0xEE,0xFF), Color.FromRgb(0xEE,0xCC,0xCC),
        };

        public ColorPickerControl(string initialHex, Action close)
        {
            InitializeComponent();
            _close = close;

            // 解析初始颜色并设置滑块
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(initialHex);
                RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                PreviewRect.Fill = new SolidColorBrush(c);
            }
            catch { }

            HexBox.Text = initialHex;
            HexBox.TextChanged += (_, _) =>
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(HexBox.Text);
                    RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                    RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                    PreviewRect.Fill = new SolidColorBrush(c);
                }
                catch { }
            };

            // 预置色块网格
            int cols = 6, rows = (_palette.Length + cols - 1) / cols;
            for (int r = 0; r < rows; r++) PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            for (int c = 0; c < cols; c++) PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            for (int i = 0; i < _palette.Length; i++)
            {
                int row = i / cols, col = i % cols;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(_palette[i]),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x55)),
                    StrokeThickness = 0.5, Width = 26, Height = 26,
                    Cursor = Cursors.Hand, RadiusX = 3, RadiusY = 3, Margin = new Thickness(1)
                };
                var c = _palette[i];
                rect.MouseLeftButtonDown += (_, _) =>
                {
                    RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                    ApplyFromSliders();
                };
                Grid.SetRow(rect, row); Grid.SetColumn(rect, col);
                PaletteGrid.Children.Add(rect);
            }

            BtnOk.Click += (_, _) => { ApplyFromSliders(); try { SelectedColor = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim()); } catch { } _close?.Invoke(); };
            BtnCancel.Click += (_, _) => { SelectedColor = null; _close?.Invoke(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { SelectedColor = null; _close?.Invoke(); } };
        }

        private void ApplyFromSliders()
        {
            var c = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
            HexBox.Text = c.ToString();
            PreviewRect.Fill = new SolidColorBrush(c);
        }

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var s = sender as Slider;
            if (s == RSlider) RVal.Text = ((int)s!.Value).ToString();
            else if (s == GSlider) GVal.Text = ((int)s!.Value).ToString();
            else if (s == BSlider) BVal.Text = ((int)s!.Value).ToString();
            ApplyFromSliders();
        }
    }
}
