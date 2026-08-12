using Avalonia.Controls;
using StudyJourney.Avalonia.Models;

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
    }
}
