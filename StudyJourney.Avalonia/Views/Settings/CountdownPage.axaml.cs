using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class CountdownPage : UserControl, ISettingsPage
{
    public CountdownPage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        // 字体族（系统字体）
        if (FontFamilyBox.Items.Count == 0)
        {
            foreach (var ff in FontManager.Current.SystemFonts)
                FontFamilyBox.Items.Add(ff.Name);
        }
        FontFamilyBox.SelectedItem = s.FontFamily;

        FontSizeSlider.Value = s.FontSize;
        FontSizeText.Text = ((int)s.FontSize).ToString();
        OpacitySlider.Value = s.OverallOpacity;
        OpacityText.Text = $"{s.OverallOpacity * 100:F0}%";
        ShowProgressBarCheck.IsChecked = s.ShowProgressBar;
        ShowProgressTextCheck.IsChecked = s.ShowProgressText;
        ShowDaysCheck.IsChecked = s.ShowDays;
        ShowHoursCheck.IsChecked = s.ShowHours;
        ShowMinutesCheck.IsChecked = s.ShowMinutes;
        ShowSecondsCheck.IsChecked = s.ShowSeconds;
        GaokaoDateBox.Text = s.GaokaoDateStr;
        StartDateBox.Text = s.StartDateStr;
        CustomCountdownGrid.ItemsSource = s.CustomCountdowns;

        TextColorBox.Text = s.TextColor.ToString();
        AccentColorBox.Text = s.AccentColor.ToString();
        UpdateColorPreview(TextColorPreview, TextColorBox.Text);
        UpdateColorPreview(AccentColorPreview, AccentColorBox.Text);
    }

    public void Apply(AppSettings s)
    {
        if (FontFamilyBox.SelectedItem is string ff && !string.IsNullOrWhiteSpace(ff))
            s.FontFamily = ff;

        s.FontSize = (int)FontSizeSlider.Value;
        s.OverallOpacity = OpacitySlider.Value;
        s.ShowProgressBar = ShowProgressBarCheck.IsChecked == true;
        s.ShowProgressText = ShowProgressTextCheck.IsChecked == true;
        s.ShowDays = ShowDaysCheck.IsChecked == true;
        s.ShowHours = ShowHoursCheck.IsChecked == true;
        s.ShowMinutes = ShowMinutesCheck.IsChecked == true;
        s.ShowSeconds = ShowSecondsCheck.IsChecked == true;
        s.GaokaoDateStr = GaokaoDateBox.Text ?? "";
        s.StartDateStr = StartDateBox.Text ?? "";

        if (TryParseColor(TextColorBox.Text ?? "#FFFFFF", out var tc)) s.TextColor = tc;
        if (TryParseColor(AccentColorBox.Text ?? "#2B6CB0", out var ac)) s.AccentColor = ac;
    }

    // ── 滑条联动：数字随滑动实时更新 ────────────────────────
    private void FontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (FontSizeText != null) FontSizeText.Text = ((int)e.NewValue).ToString();
    }

    private void OpacitySlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (OpacityText != null) OpacityText.Text = $"{e.NewValue * 100:F0}%";
    }

    // ── 颜色选择 ────────────────────────────────────────────
    private void PickTextColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(TextColorBox, TextColorPreview);

    private void PickAccentColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(AccentColorBox, AccentColorPreview);

    private void PickColor(TextBox box, Border preview)
    {
        var dlg = new ColorPickerDialog(box.Text ?? "#FFFFFFFF");
        var owner = GetWindow();
        if (owner != null) dlg.ShowDialog(owner); else dlg.Show();
        dlg.Closed += (_, _) =>
        {
            if (dlg.SelectedHex != null)
            {
                box.Text = dlg.SelectedHex;
                UpdateColorPreview(preview, dlg.SelectedHex);
            }
        };
    }

    private static void UpdateColorPreview(Border? preview, string? hex)
    {
        if (preview == null || string.IsNullOrEmpty(hex)) return;
        if (TryParseColor(hex, out var c))
            preview.Background = new SolidColorBrush(c);
    }

    private static bool TryParseColor(string hex, out Color c)
    {
        try { c = Color.Parse(hex); return true; }
        catch { c = Colors.White; return false; }
    }

    private Window? GetWindow() => TopLevel.GetTopLevel(this) as Window;

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
