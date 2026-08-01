using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class ColorPickerControl : UserControl
    {
        public Color? SelectedColor { get; private set; }
        private readonly Action? _close;
        private bool _updating;

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

            // 绑定滑块事件（在 InitializeComponent 之后，避免构造时触发）
            RSlider.ValueChanged += OnSliderChanged;
            GSlider.ValueChanged += OnSliderChanged;
            BSlider.ValueChanged += OnSliderChanged;

            // 解析初始颜色
            _updating = true;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(initialHex);
                RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                PreviewRect.Fill = new SolidColorBrush(c);
            }
            catch
            {
                RSlider.Value = 255; GSlider.Value = 255; BSlider.Value = 255;
                RVal.Text = "255"; GVal.Text = "255"; BVal.Text = "255";
                HexBox.Text = "#FFFFFFFF";
                PreviewRect.Fill = new SolidColorBrush(Colors.White);
            }
            _updating = false;

            HexBox.Text = initialHex;
            HexBox.TextChanged += (_, _) =>
            {
                if (_updating) return;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim());
                    _updating = true;
                    RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                    RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                    PreviewRect.Fill = new SolidColorBrush(c);
                    _updating = false;
                }
                catch { }
            };

            // 预置色块
            int cols = 6, rows = (_palette.Length + cols - 1) / cols;
            for (int r = 0; r < rows; r++) PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols; c++) PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
                    _updating = true;
                    RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
                    RVal.Text = c.R.ToString(); GVal.Text = c.G.ToString(); BVal.Text = c.B.ToString();
                    HexBox.Text = c.ToString();
                    PreviewRect.Fill = new SolidColorBrush(c);
                    _updating = false;
                };
                Grid.SetRow(rect, row); Grid.SetColumn(rect, col);
                PaletteGrid.Children.Add(rect);
            }

            BtnOk.Click += (_, _) =>
            {
                try { SelectedColor = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim()); }
                catch { SelectedColor = null; }
                _close?.Invoke();
            };
            BtnCancel.Click += (_, _) => { SelectedColor = null; _close?.Invoke(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { SelectedColor = null; _close?.Invoke(); } };
        }

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating) return;
            var s = (Slider)sender;
            _updating = true;
            var c = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
            RVal.Text = ((int)RSlider.Value).ToString();
            GVal.Text = ((int)GSlider.Value).ToString();
            BVal.Text = ((int)BSlider.Value).ToString();
            HexBox.Text = c.ToString();
            PreviewRect.Fill = new SolidColorBrush(c);
            _updating = false;
        }
    }
}
