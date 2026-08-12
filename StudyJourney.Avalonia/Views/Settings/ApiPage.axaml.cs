using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        QuoteForegroundBox.Text = s.QuoteForegroundHex;
        QuoteItalicCheck.IsChecked = s.QuoteItalic;
        QuoteApiUrlBox.Text = s.QuoteApiUrl;
        QuoteTextFieldNameBox.Text = s.QuoteTextFieldName;
        QuoteRefreshIntervalSlider.Value = s.QuoteAutoRefreshInterval;
        WeatherCityBox.Text = s.WeatherCity;
        WeatherAdcodeBox.Text = s.WeatherAdcode;
        WeatherFontSizeSlider.Value = s.WeatherFontSize;
        WeatherRefreshIntervalSlider.Value = s.WeatherRefreshInterval;

        WeatherCityColorBox.Text = s.WeatherCityColor;
        WeatherInfoColorBox.Text = s.WeatherInfoColor;
        WeatherTempColorBox.Text = s.WeatherTempColor;
        WeatherIconColorBox.Text = s.WeatherIconColor;
        WeatherTimeColorBox.Text = s.WeatherTimeColor;
    }

    public void Apply(AppSettings s)
    {
        s.ShowDailyQuote = ShowDailyQuoteCheck.IsChecked == true;
        s.QuoteFontSize = QuoteFontSizeSlider.Value;
        s.QuoteForegroundHex = QuoteForegroundBox.Text ?? s.QuoteForegroundHex;
        s.QuoteItalic = QuoteItalicCheck.IsChecked == true;
        s.QuoteApiUrl = QuoteApiUrlBox.Text ?? s.QuoteApiUrl;
        s.QuoteTextFieldName = QuoteTextFieldNameBox.Text ?? s.QuoteTextFieldName;
        s.QuoteAutoRefreshInterval = (int)QuoteRefreshIntervalSlider.Value;
        s.WeatherCity = WeatherCityBox.Text ?? s.WeatherCity;
        s.WeatherAdcode = WeatherAdcodeBox.Text ?? s.WeatherAdcode;
        s.WeatherFontSize = WeatherFontSizeSlider.Value;
        s.WeatherRefreshInterval = (int)WeatherRefreshIntervalSlider.Value;

        s.WeatherCityColor = WeatherCityColorBox.Text ?? s.WeatherCityColor;
        s.WeatherInfoColor = WeatherInfoColorBox.Text ?? s.WeatherInfoColor;
        s.WeatherTempColor = WeatherTempColorBox.Text ?? s.WeatherTempColor;
        s.WeatherIconColor = WeatherIconColorBox.Text ?? s.WeatherIconColor;
        s.WeatherTimeColor = WeatherTimeColorBox.Text ?? s.WeatherTimeColor;
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

    // ── 颜色选择 ────────────────────────────────────────────
    private void PickQuoteForeground_Click(object? sender, RoutedEventArgs e)
        => PickColor(QuoteForegroundBox);

    private void PickWeatherCityColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(WeatherCityColorBox);

    private void PickWeatherInfoColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(WeatherInfoColorBox);

    private void PickWeatherTempColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(WeatherTempColorBox);

    private void PickWeatherIconColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(WeatherIconColorBox);

    private void PickWeatherTimeColor_Click(object? sender, RoutedEventArgs e)
        => PickColor(WeatherTimeColorBox);

    private void PickColor(TextBox box)
    {
        var dlg = new ColorPickerDialog(box.Text ?? "#FFFFFFFF");
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) dlg.ShowDialog(owner); else dlg.Show();
        dlg.Closed += (_, _) => { if (dlg.SelectedHex != null) box.Text = dlg.SelectedHex; };
    }
}
