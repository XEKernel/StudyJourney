using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ApiPage : UserControl, ISettingsPage
{
    public ApiPage()
    {
        InitializeComponent();
    }

    public void Load(AppSettings s)
    {
        ShowDailyQuoteCheck.IsChecked = s.ShowDailyQuote;
        QuoteFontSizeSlider.Value = s.QuoteFontSize;
        QuoteItalicCheck.IsChecked = s.QuoteItalic;
        QuoteApiUrlBox.Text = s.QuoteApiUrl;
        QuoteTextFieldNameBox.Text = s.QuoteTextFieldName;
        QuoteRefreshIntervalSlider.Value = s.QuoteAutoRefreshInterval;
        WeatherCityBox.Text = s.WeatherCity;
        WeatherAdcodeBox.Text = s.WeatherAdcode;
        WeatherFontSizeSlider.Value = s.WeatherFontSize;
        WeatherRefreshIntervalSlider.Value = s.WeatherRefreshInterval;
        WeatherDetailLevelCombo.SelectedIndex = Math.Clamp(s.WeatherDetailLevel, 0, 2);

        WeatherCityColorSwatch.Value = s.WeatherCityColor;
        WeatherInfoColorSwatch.Value = s.WeatherInfoColor;
        WeatherTempColorSwatch.Value = s.WeatherTempColor;
        WeatherIconColorSwatch.Value = s.WeatherIconColor;
        QuoteForegroundSwatch.Value = s.QuoteForegroundHex;
    }

    public void Apply(AppSettings s)
    {
        s.ShowDailyQuote = ShowDailyQuoteCheck.IsChecked == true;
        s.QuoteFontSize = QuoteFontSizeSlider.Value;
        s.QuoteForegroundHex = PickColor(QuoteForegroundSwatch, s.QuoteForegroundHex);
        s.QuoteItalic = QuoteItalicCheck.IsChecked == true;
        s.QuoteApiUrl = QuoteApiUrlBox.Text ?? s.QuoteApiUrl;
        s.QuoteTextFieldName = QuoteTextFieldNameBox.Text ?? s.QuoteTextFieldName;
        s.QuoteAutoRefreshInterval = (int)QuoteRefreshIntervalSlider.Value;
        s.WeatherCity = WeatherCityBox.Text ?? s.WeatherCity;
        s.WeatherAdcode = WeatherAdcodeBox.Text ?? s.WeatherAdcode;
        s.WeatherFontSize = WeatherFontSizeSlider.Value;
        s.WeatherRefreshInterval = (int)WeatherRefreshIntervalSlider.Value;
        s.WeatherDetailLevel = WeatherDetailLevelCombo.SelectedIndex < 0 ? 1 : WeatherDetailLevelCombo.SelectedIndex;

        s.WeatherCityColor = PickColor(WeatherCityColorSwatch, s.WeatherCityColor);
        s.WeatherInfoColor = PickColor(WeatherInfoColorSwatch, s.WeatherInfoColor);
        s.WeatherTempColor = PickColor(WeatherTempColorSwatch, s.WeatherTempColor);
        s.WeatherIconColor = PickColor(WeatherIconColorSwatch, s.WeatherIconColor);
    }

    // ── 滑条联动 ────────────────────────────────────────────
    private void QuoteFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (QuoteFontSizeText != null) QuoteFontSizeText.Text = ((int)e.NewValue).ToString();
    }

    private void QuoteRefreshIntervalSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (QuoteRefreshIntervalText != null) QuoteRefreshIntervalText.Text = $"{(int)e.NewValue}秒";
    }

    private void WeatherFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (WeatherFontSizeText != null) WeatherFontSizeText.Text = ((int)e.NewValue).ToString();
    }

    private void WeatherRefreshIntervalSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (WeatherRefreshIntervalText != null) WeatherRefreshIntervalText.Text = $"{(int)e.NewValue}分";
    }

    // ── 颜色 ────────────────────────────────────────────────
    // 2026-09-25（规划 2.7 ③）：hex 输入框 → ColorSwatch 色板（整行可点）。
    // 顺带修掉一个静默缺陷：原来「每日一言 · 文字颜色」的预览 Border **没有 x:Name**，
    // 选完色只改了输入框、预览色块永远停在 XAML 里的 #FFAAAAAA（看着像没生效）。
    private static string PickColor(ColorSwatch sw, string fallback)
        => ColorSwatch.TryParse(sw.Value, out _) ? sw.Value : fallback;
}
