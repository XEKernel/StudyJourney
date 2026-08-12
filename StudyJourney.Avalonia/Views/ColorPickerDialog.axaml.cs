using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Views;

/// <summary>颜色选择对话框（Avalonia ColorPicker 控件封装，返回 #AARRGGBB 字符串）</summary>
public partial class ColorPickerDialog : Window
{
    public ColorPickerDialog(string initialHex)
    {
        InitializeComponent();
        try { Picker.Color = Color.Parse(initialHex); } catch { }
    }

    /// <summary>用户确认的颜色（#AARRGGBB），未确认为 null</summary>
    public string? SelectedHex { get; private set; }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        SelectedHex = Picker.Color.ToString();
        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
