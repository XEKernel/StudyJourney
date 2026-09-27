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

    /// <summary>#8：未保存修改标记（Load 期间置位控件会触发 ValueChanged，用 _loading 挡掉）</summary>
    private bool _dirty;
    private bool _loading;
    public bool IsDirty => _dirty;

    public void Load(AppSettings s)
    {
        _loading = true;
        try
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
            // v2.21.0：TextBox 手打 → NumericUpDown（-1 = 水平居中/自动 的语义保留，
            // 但老师不用再手打 —— 有「恢复居中」按钮，拖动定位才是主交互）
            // ⚠ NumericUpDown.Value 是 decimal?（不是 double?），设置项是 double → 两边都要转
            CustomXBox.Value = (decimal)s.CustomPositionX;
            CustomYBox.Value = (decimal)s.CustomPositionY;
            OffsetXBox.Value = (decimal)s.PositionOffsetX;
            OffsetYBox.Value = (decimal)s.PositionOffsetY;
            AlwaysOnTopCheck.IsChecked = s.AlwaysOnTop;
            CompactTopmostCheck.IsChecked = s.CompactProgressTopmost;
            ClickThroughCheck.IsChecked = s.ClickThrough;
            AutoStartCheck.IsChecked = s.AutoStart;
            HideWhenMaximizedCheck.IsChecked = s.HideWhenMaximized;
            HideDuringClassCheck.IsChecked = s.HideDuringClass;
        }
        finally { _loading = false; }
        _dirty = false;
    }

    private void CornerRadiusSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (CornerRadiusText != null) CornerRadiusText.Text = ((int)e.NewValue).ToString();
        if (!_loading) _dirty = true;
    }

    /// <summary>坐标/偏移数字框改动 → 标记未保存（保存时机统一走 Apply）</summary>
    private void PosBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_loading) _dirty = true;
    }

    private void CenterPosBtn_Click(object? sender, RoutedEventArgs e)
    {
        CustomXBox.Value = -1;
        CustomYBox.Value = -1;
        if (!_loading) _dirty = true;
    }

    private void MarkDirty_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_loading) _dirty = true;
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

        // NumericUpDown 有 Min/Max 兜底，Value 为空（不应发生）时保留原值
        if (CustomXBox.Value is decimal x) s.CustomPositionX = (double)x;
        if (CustomYBox.Value is decimal y) s.CustomPositionY = (double)y;
        if (OffsetXBox.Value is decimal ox) s.PositionOffsetX = (double)ox;
        if (OffsetYBox.Value is decimal oy) s.PositionOffsetY = (double)oy;

        s.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        s.CompactProgressTopmost = CompactTopmostCheck.IsChecked == true;
        s.ClickThrough = ClickThroughCheck.IsChecked == true;
        s.AutoStart = AutoStartCheck.IsChecked == true;
        s.HideWhenMaximized = HideWhenMaximizedCheck.IsChecked == true;
        s.HideDuringClass = HideDuringClassCheck.IsChecked == true;
    }
}
