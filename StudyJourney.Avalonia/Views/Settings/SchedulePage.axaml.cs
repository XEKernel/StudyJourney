using Avalonia.Controls;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class SchedulePage : UserControl, ISettingsPage
{
    public SchedulePage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        ShowScheduleBarCheck.IsChecked = s.ShowScheduleBar;
        ScheduleBarAlwaysOnTopCheck.IsChecked = s.ScheduleBarAlwaysOnTop;
        ScheduleBarClickThroughCheck.IsChecked = s.ScheduleBarClickThrough;
        ScheduleBarAutoCollapseCheck.IsChecked = s.ScheduleBarAutoCollapse;
        ScheduleBarOpacitySlider.Value = s.ScheduleBarOpacity;
        ScheduleBarWidthBox.Text = s.ScheduleBarWidth.ToString();
        ScheduleBarFontSizeSlider.Value = s.ScheduleBarFontSize;
        CountdownExpandCb.SelectedIndex = s.CountdownExpandSeconds >= 60 ? 1 : 0;
        EnableCountdownSoundCheck.IsChecked = s.EnableCountdownSound;
        EnableReminderSoundCheck.IsChecked = s.EnableReminderSound;
        ReminderSoundPathBox.Text = s.ReminderSoundPath;
        RemindClassStartCheck.IsChecked = s.RemindClassStart;
        RemindClassMidCheck.IsChecked = s.RemindClassMid;
        RemindClassEndSoonCheck.IsChecked = s.RemindClassEndSoon;
        RemindClassEndCheck.IsChecked = s.RemindClassEnd;
        RemindNextClassSoonCheck.IsChecked = s.RemindNextClassSoon;
        RemindDayEndCheck.IsChecked = s.RemindDayEnd;
        RemindSpecialPeriodCheck.IsChecked = s.RemindSpecialPeriod;
    }

    public void Apply(AppSettings s)
    {
        s.ShowScheduleBar = ShowScheduleBarCheck.IsChecked == true;
        s.ScheduleBarAlwaysOnTop = ScheduleBarAlwaysOnTopCheck.IsChecked == true;
        s.ScheduleBarClickThrough = ScheduleBarClickThroughCheck.IsChecked == true;
        s.ScheduleBarAutoCollapse = ScheduleBarAutoCollapseCheck.IsChecked == true;
        s.ScheduleBarOpacity = ScheduleBarOpacitySlider.Value;
        if (double.TryParse(ScheduleBarWidthBox.Text, out var w)) s.ScheduleBarWidth = w;
        s.ScheduleBarFontSize = ScheduleBarFontSizeSlider.Value;
        s.CountdownExpandSeconds = CountdownExpandCb.SelectedIndex == 1 ? 60 : 30;
        s.EnableCountdownSound = EnableCountdownSoundCheck.IsChecked == true;
        s.EnableReminderSound = EnableReminderSoundCheck.IsChecked == true;
        s.ReminderSoundPath = ReminderSoundPathBox.Text ?? "";
        s.RemindClassStart = RemindClassStartCheck.IsChecked == true;
        s.RemindClassMid = RemindClassMidCheck.IsChecked == true;
        s.RemindClassEndSoon = RemindClassEndSoonCheck.IsChecked == true;
        s.RemindClassEnd = RemindClassEndCheck.IsChecked == true;
        s.RemindNextClassSoon = RemindNextClassSoonCheck.IsChecked == true;
        s.RemindDayEnd = RemindDayEndCheck.IsChecked == true;
        s.RemindSpecialPeriod = RemindSpecialPeriodCheck.IsChecked == true;
    }
}
