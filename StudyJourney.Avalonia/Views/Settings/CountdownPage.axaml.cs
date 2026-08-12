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

        // 中英文自定义文本
        ChinesePrefixBox.Text = s.ChinesePrefix;
        ChineseDaysBox.Text = s.ChineseDaysText;
        ChineseHoursBox.Text = s.ChineseHoursText;
        ChineseMinutesBox.Text = s.ChineseMinutesText;
        ChineseSecondsBox.Text = s.ChineseSecondsText;
        EnglishPrefixBox.Text = s.EnglishPrefix;
        EnglishDaysBox.Text = s.EnglishDaysText;
        EnglishHoursBox.Text = s.EnglishHoursText;
        EnglishMinutesBox.Text = s.EnglishMinutesText;
        EnglishSecondsBox.Text = s.EnglishSecondsText;

        NumberColorBox.Text = s.NumberColor.ToString();
        TextColorBox.Text = s.TextColor.ToString();
        ProgressBarColorBox.Text = s.ProgressBarColor.ToString();
        UpdateColorPreview(NumberColorPreview, NumberColorBox.Text);
        UpdateColorPreview(TextColorPreview, TextColorBox.Text);
        UpdateColorPreview(ProgressBarColorPreview, ProgressBarColorBox.Text);
    }

    public void Apply(AppSettings s)
    {
        if (FontFamilyBox.SelectedItem is string ff && !string.IsNullOrWhiteSpace(ff))
            s.FontFamily = ff;

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

        // 中英文自定义文本
        s.ChinesePrefix = ChinesePrefixBox.Text ?? s.ChinesePrefix;
        s.ChineseDaysText = ChineseDaysBox.Text ?? s.ChineseDaysText;
        s.ChineseHoursText = ChineseHoursBox.Text ?? s.ChineseHoursText;
        s.ChineseMinutesText = ChineseMinutesBox.Text ?? s.ChineseMinutesText;
        s.ChineseSecondsText = ChineseSecondsBox.Text ?? s.ChineseSecondsText;
        s.EnglishPrefix = EnglishPrefixBox.Text ?? s.EnglishPrefix;
        s.EnglishDaysText = EnglishDaysBox.Text ?? s.EnglishDaysText;
        s.EnglishHoursText = EnglishHoursBox.Text ?? s.EnglishHoursText;
        s.EnglishMinutesText = EnglishMinutesBox.Text ?? s.EnglishMinutesText;
        s.EnglishSecondsText = EnglishSecondsBox.Text ?? s.EnglishSecondsText;

        if (TryParseColor(NumberColorBox.Text ?? "#FFFFFF", out var nc)) s.NumberColor = nc;
        if (TryParseColor(TextColorBox.Text ?? "#FFFFFF", out var tc)) s.TextColor = tc;
        if (TryParseColor(ProgressBarColorBox.Text ?? "#FFFFFF", out var pc)) s.ProgressBarColor = pc;
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

    private void DecimalSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (DecimalText != null) DecimalText.Text = ((int)e.NewValue).ToString();
    }

    // ── 颜色选择 ────────────────────────────────────────────
    private void PickNumberColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(NumberColorBox, NumberColorPreview);

    private void PickTextColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(TextColorBox, TextColorPreview);

    private void PickProgressBarColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(ProgressBarColorBox, ProgressBarColorPreview);

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
