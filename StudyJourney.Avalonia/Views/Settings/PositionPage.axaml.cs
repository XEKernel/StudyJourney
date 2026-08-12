using Avalonia.Controls;
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
        PosTop.IsChecked = s.PositionPreset == PositionPresetValues.Top;
        PosUpperCenter.IsChecked = s.PositionPreset == PositionPresetValues.UpperCenter;
        PosCenter.IsChecked = s.PositionPreset == PositionPresetValues.Center;
        PosLowerCenter.IsChecked = s.PositionPreset == PositionPresetValues.LowerCenter;
        PosBottom.IsChecked = s.PositionPreset == PositionPresetValues.Bottom;
        PosCustom.IsChecked = s.PositionPreset == PositionPresetValues.Custom;
        CustomXBox.Text = s.CustomPositionX.ToString();
        CustomYBox.Text = s.CustomPositionY.ToString();
        OffsetYBox.Text = s.PositionOffsetY.ToString();
        AlwaysOnTopCheck.IsChecked = s.AlwaysOnTop;
        AutoStartCheck.IsChecked = s.AutoStart;
        HideWhenMaximizedCheck.IsChecked = s.HideWhenMaximized;
        HideDuringClassCheck.IsChecked = s.HideDuringClass;
        HideSubjectsBox.Text = s.HideSubjects;
    }

    public void Apply(AppSettings s)
    {
        s.PositionPreset = PosTop.IsChecked == true ? PositionPresetValues.Top
            : PosUpperCenter.IsChecked == true ? PositionPresetValues.UpperCenter
            : PosCenter.IsChecked == true ? PositionPresetValues.Center
            : PosLowerCenter.IsChecked == true ? PositionPresetValues.LowerCenter
            : PosBottom.IsChecked == true ? PositionPresetValues.Bottom
            : PosCustom.IsChecked == true ? PositionPresetValues.Custom
            : PositionPresetValues.UpperCenter;

        if (double.TryParse(CustomXBox.Text, out var x)) s.CustomPositionX = x;
        if (double.TryParse(CustomYBox.Text, out var y)) s.CustomPositionY = y;
        if (double.TryParse(OffsetYBox.Text, out var o)) s.PositionOffsetY = o;

        s.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        s.AutoStart = AutoStartCheck.IsChecked == true;
        s.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
        s.HideDuringClass = HideDuringClassCheck.IsChecked == true;
        s.HideSubjects = HideSubjectsBox.Text ?? "";
    }
}
