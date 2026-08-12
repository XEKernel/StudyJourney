using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class CountdownPage : UserControl, ISettingsPage
{
    public CountdownPage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        FontSizeSlider.Value = s.FontSize;
        FontSizeText.Text = ((int)s.FontSize).ToString();
        OpacitySlider.Value = s.OverallOpacity;
        OpacityText.Text = $"{s.OverallOpacity * 100:F0}%";
        ShowEnglishCheck.IsChecked = s.ShowEnglishLine;
        ShowProgressBarCheck.IsChecked = s.ShowProgressBar;
        ShowProgressTextCheck.IsChecked = s.ShowProgressText;
        ShowDaysCheck.IsChecked = s.ShowDays;
        ShowHoursCheck.IsChecked = s.ShowHours;
        ShowMinutesCheck.IsChecked = s.ShowMinutes;
        ShowSecondsCheck.IsChecked = s.ShowSeconds;
        DecimalSlider.Value = s.ProgressDecimalDigits;
        DecimalText.Text = s.ProgressDecimalDigits.ToString();
        EnableAnimationsCheck.IsChecked = s.EnableAnimations;
        GaokaoDateBox.Text = s.GaokaoDateStr;
        StartDateBox.Text = s.StartDateStr;
        CustomCountdownGrid.ItemsSource = s.CustomCountdowns;
    }

    public void Apply(AppSettings s)
    {
        s.FontSize = (int)FontSizeSlider.Value;
        s.OverallOpacity = OpacitySlider.Value;
        s.ShowEnglishLine = ShowEnglishCheck.IsChecked == true;
        s.ShowProgressBar = ShowProgressBarCheck.IsChecked == true;
        s.ShowProgressText = ShowProgressTextCheck.IsChecked == true;
        s.ShowDays = ShowDaysCheck.IsChecked == true;
        s.ShowHours = ShowHoursCheck.IsChecked == true;
        s.ShowMinutes = ShowMinutesCheck.IsChecked == true;
        s.ShowSeconds = ShowSecondsCheck.IsChecked == true;
        s.ProgressDecimalDigits = (int)DecimalSlider.Value;
        s.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
        s.GaokaoDateStr = GaokaoDateBox.Text ?? "";
        s.StartDateStr = StartDateBox.Text ?? "";
    }

    // ── 自定义倒计时：增删 + 网格刷新（List 需手动刷新 ItemsSource）──
    private void AddCountdownBtn_Click(object? sender, RoutedEventArgs e)
    {
        var list = App.Settings.CustomCountdowns;
        list.Add(new CustomCountdown
        {
            Name = "新倒计时",
            DateStr = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd")
        });
        RefreshGrid();
    }

    private void DeleteCountdownBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (CustomCountdownGrid.SelectedItem is CustomCountdown c)
        {
            App.Settings.CustomCountdowns.Remove(c);
            RefreshGrid();
        }
    }

    private void RefreshGrid()
    {
        CustomCountdownGrid.ItemsSource = null;
        CustomCountdownGrid.ItemsSource = App.Settings.CustomCountdowns;
    }
}
