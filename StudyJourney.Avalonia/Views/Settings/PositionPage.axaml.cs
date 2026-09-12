using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class PositionPage : UserControl, ISettingsPage
{
    public PositionPage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        CapsuleMerged.IsChecked = !s.IslandSeparated;
        CapsuleSeparated.IsChecked = s.IslandSeparated;
        CornerRadiusSlider.Value = s.MainWindowCornerRadius;
        CountdownBarCheck.IsChecked = s.CountdownProgressBarStyle;
        PosTop.IsChecked = s.PositionPreset == PositionPresetValues.Top;
        PosUpperCenter.IsChecked = s.PositionPreset == PositionPresetValues.UpperCenter;
        PosCenter.IsChecked = s.PositionPreset == PositionPresetValues.Center;
        PosLowerCenter.IsChecked = s.PositionPreset == PositionPresetValues.LowerCenter;
        PosBottom.IsChecked = s.PositionPreset == PositionPresetValues.Bottom;
        PosCustom.IsChecked = s.PositionPreset == PositionPresetValues.Custom;
        CustomXBox.Text = s.CustomPositionX.ToString();
        CustomYBox.Text = s.CustomPositionY.ToString();
        OffsetXBox.Text = s.PositionOffsetX.ToString();
        OffsetYBox.Text = s.PositionOffsetY.ToString();
        AlwaysOnTopCheck.IsChecked = s.AlwaysOnTop;
        CompactTopmostCheck.IsChecked = s.CompactProgressTopmost;
        ClickThroughCheck.IsChecked = s.ClickThrough;
        AutoStartCheck.IsChecked = s.AutoStart;
        HideWhenMaximizedCheck.IsChecked = s.HideWhenMaximized;
        HideDuringClassCheck.IsChecked = s.HideDuringClass;

        // D1 修复：自定义坐标输入框随"自定义坐标"选项启用（原 XAML 写死 IsEnabled=False 且无联动
        // → 选自定义也无法输入，自定义定位实际不可用）
        SetCustomCoordEnabled(s.PositionPreset == PositionPresetValues.Custom);
    }

    private void PosCustom_IsCheckedChanged(object? sender, RoutedEventArgs e)
        => SetCustomCoordEnabled(PosCustom.IsChecked == true);

    private void SetCustomCoordEnabled(bool on)
    {
        if (CustomCoordPanel == null) return;   // XAML 加载期保护
        CustomCoordPanel.IsEnabled = on;
        CustomCoordPanel.Opacity = on ? 1 : 0.55;
    }

    private void CornerRadiusSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (CornerRadiusText != null) CornerRadiusText.Text = ((int)e.NewValue).ToString();
    }

    public void Apply(AppSettings s)
    {
        s.IslandSeparated = CapsuleSeparated.IsChecked == true;
        s.MainWindowCornerRadius = CornerRadiusSlider.Value;
        s.CountdownProgressBarStyle = CountdownBarCheck.IsChecked == true;

        s.PositionPreset = PosTop.IsChecked == true ? PositionPresetValues.Top
            : PosUpperCenter.IsChecked == true ? PositionPresetValues.UpperCenter
            : PosCenter.IsChecked == true ? PositionPresetValues.Center
            : PosLowerCenter.IsChecked == true ? PositionPresetValues.LowerCenter
            : PosBottom.IsChecked == true ? PositionPresetValues.Bottom
            : PosCustom.IsChecked == true ? PositionPresetValues.Custom
            : PositionPresetValues.UpperCenter;

        if (double.TryParse(CustomXBox.Text, out var x)) s.CustomPositionX = x;
        if (double.TryParse(CustomYBox.Text, out var y)) s.CustomPositionY = y;
        if (double.TryParse(OffsetXBox.Text, out var ox)) s.PositionOffsetX = ox;
        if (double.TryParse(OffsetYBox.Text, out var o)) s.PositionOffsetY = o;

        s.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        s.CompactProgressTopmost = CompactTopmostCheck.IsChecked == true;
        s.ClickThrough = ClickThroughCheck.IsChecked == true;
        s.AutoStart = AutoStartCheck.IsChecked == true;
        s.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
        s.HideDuringClass = HideDuringClassCheck.IsChecked == true;
    }
}
