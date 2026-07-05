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
            Color.FromRgb(0xFF,0xBB,0xBB), Color.FromRgb(0xBB,0xFF,0xBB), Color.FromRgb(0xBB,0xBB,0xFF),
            Color.FromRgb(0xFF,0xFF,0xBB), Color.FromRgb(0xFF,0xBB,0xFF), Color.FromRgb(0xBB,0xFF,0xFF),
        };

        public ColorPickerControl(string initialHex, Action close)
        {
            InitializeComponent();
            _close = close;

            HexBox.Text = initialHex;
            HexBox.TextChanged += (_, _) =>
            {
                try { PreviewRect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexBox.Text)); } catch { }
            };
            try { PreviewRect.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(initialHex)); } catch { }

            int cols = 6, rows = (_palette.Length + cols - 1) / cols;
            for (int r = 0; r < rows; r++) PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            for (int c = 0; c < cols; c++) PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            for (int i = 0; i < _palette.Length; i++)
            {
                int row = i / cols, col = i % cols;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(_palette[i]),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x55)),
                    StrokeThickness = 0.5, Width = 30, Height = 30,
                    Cursor = Cursors.Hand, RadiusX = 3, RadiusY = 3, Margin = new Thickness(1)
                };
                var c = _palette[i];
                rect.MouseLeftButtonDown += (_, _) => { SelectedColor = c; HexBox.Text = c.ToString(); PreviewRect.Fill = new SolidColorBrush(c); };
                Grid.SetRow(rect, row); Grid.SetColumn(rect, col);
                PaletteGrid.Children.Add(rect);
            }

            BtnOk.Click += (_, _) => { try { SelectedColor = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim()); } catch { } _close?.Invoke(); };
            BtnCancel.Click += (_, _) => { SelectedColor = null; _close?.Invoke(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { SelectedColor = null; _close?.Invoke(); } };
        }
    }
}
