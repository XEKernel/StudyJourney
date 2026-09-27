using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;
using StudyJourney.Avalonia.Views;

namespace StudyJourney.Avalonia.Views.Settings;

public partial class ApiPage : UserControl, ISettingsPage
{
    public ApiPage()
    {
        InitializeComponent();
    }

    /// <summary>#8：未保存修改标记（Load 期间置位控件会触发事件，用 _loading 挡掉）</summary>
    private bool _dirty;
    private bool _loading;
    public bool IsDirty => _dirty;

    private void MarkDirty()
    {
        if (!_loading) _dirty = true;
    }

    // v2.21.0：滑条/开关事件都只标脏；真正写回统一走 Apply
    private void DirtyCheck_Changed(object? sender, RoutedEventArgs e) => MarkDirty();

    /// <summary>间隔开关：顺带联动滑条启用状态（关掉还留着可拖的滑条 = "看着能调其实不生效"）</summary>
    private void QuoteAutoRefreshCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (QuoteRefreshIntervalSlider != null)
            QuoteRefreshIntervalSlider.IsEnabled = QuoteAutoRefreshCheck.IsChecked == true;
        MarkDirty();
    }

    private void WeatherAutoRefreshCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (WeatherRefreshIntervalSlider != null)
            WeatherRefreshIntervalSlider.IsEnabled = WeatherAutoRefreshCheck.IsChecked == true;
        MarkDirty();
    }
    private void QuoteFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (QuoteFontSizeText != null) QuoteFontSizeText.Text = ((int)e.NewValue).ToString();
        MarkDirty();
    }
    private void WeatherFontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (WeatherFontSizeText != null) WeatherFontSizeText.Text = ((int)e.NewValue).ToString();
        MarkDirty();
    }

    public void Load(AppSettings s)
    {
        _loading = true;
        try
        {
            ShowDailyQuoteCheck.IsChecked = s.ShowDailyQuote;
            QuoteFontSizeSlider.Value = s.QuoteFontSize;
            QuoteItalicCheck.IsChecked = s.QuoteItalic;
            QuoteApiUrlBox.Text = s.QuoteApiUrl;
            QuoteTextFieldNameBox.Text = s.QuoteTextFieldName;
            WeatherCityBox.Text = s.WeatherCity;
            WeatherAdcodeBox.Text = s.WeatherAdcode;
            WeatherFontSizeSlider.Value = s.WeatherFontSize;
            WeatherDetailLevelCombo.SelectedIndex = Math.Clamp(s.WeatherDetailLevel, 0, 2);

            // v2.21.0：「0 = 关闭」魔法值 → 显式开关 + 滑条（落盘格式不变：0 = 关）
            // ⚠ 下限统一 5：旧滑条 tick=5 且 min=0，所以 5 是既有的合法值 ——
            //    下限设更大（如 10）会把配置过 5 秒的老师**静默改掉**。
            (bool on, double slider) = IntervalSwitch.ToUi(s.QuoteAutoRefreshInterval, fallback: 30, min: 5, max: 120);
            QuoteAutoRefreshCheck.IsChecked = on;
            QuoteRefreshIntervalSlider.Value = slider;
            QuoteRefreshIntervalText.Text = $"{(int)slider}秒";
            QuoteRefreshIntervalSlider.IsEnabled = on;

            (on, slider) = IntervalSwitch.ToUi(s.WeatherRefreshInterval, fallback: 30, min: 5, max: 120);
            WeatherAutoRefreshCheck.IsChecked = on;
            WeatherRefreshIntervalSlider.Value = slider;
            WeatherRefreshIntervalText.Text = $"{(int)slider}分";
            WeatherRefreshIntervalSlider.IsEnabled = on;

            WeatherCityColorSwatch.Value = s.WeatherCityColor;
            WeatherInfoColorSwatch.Value = s.WeatherInfoColor;
            WeatherTempColorSwatch.Value = s.WeatherTempColor;
            WeatherIconColorSwatch.Value = s.WeatherIconColor;
            QuoteForegroundSwatch.Value = s.QuoteForegroundHex;
        }
        finally { _loading = false; }
        _dirty = false;
    }

    public void Apply(AppSettings s)
    {
        s.ShowDailyQuote = ShowDailyQuoteCheck.IsChecked == true;
        s.QuoteFontSize = QuoteFontSizeSlider.Value;
        s.QuoteForegroundHex = PickColor(QuoteForegroundSwatch, s.QuoteForegroundHex);
        s.QuoteItalic = QuoteItalicCheck.IsChecked == true;
        s.QuoteApiUrl = QuoteApiUrlBox.Text ?? s.QuoteApiUrl;
        s.QuoteTextFieldName = QuoteTextFieldNameBox.Text ?? s.QuoteTextFieldName;
        s.QuoteAutoRefreshInterval = IntervalSwitch.FromUi(QuoteAutoRefreshCheck.IsChecked == true,
            QuoteRefreshIntervalSlider.Value, min: 5, max: 120);
        s.WeatherCity = WeatherCityBox.Text ?? s.WeatherCity;
        s.WeatherAdcode = WeatherAdcodeBox.Text ?? s.WeatherAdcode;
        s.WeatherFontSize = WeatherFontSizeSlider.Value;
        s.WeatherRefreshInterval = IntervalSwitch.FromUi(WeatherAutoRefreshCheck.IsChecked == true,
            WeatherRefreshIntervalSlider.Value, min: 5, max: 120);
        s.WeatherDetailLevel = WeatherDetailLevelCombo.SelectedIndex < 0 ? 1 : WeatherDetailLevelCombo.SelectedIndex;

        s.WeatherCityColor = PickColor(WeatherCityColorSwatch, s.WeatherCityColor);
        s.WeatherInfoColor = PickColor(WeatherInfoColorSwatch, s.WeatherInfoColor);
        s.WeatherTempColor = PickColor(WeatherTempColorSwatch, s.WeatherTempColor);
        s.WeatherIconColor = PickColor(WeatherIconColorSwatch, s.WeatherIconColor);
    }

    // ── 切换间隔滑条联动（文字标签；标脏在 ValueChanged 里统一做）──
    private void QuoteRefreshIntervalSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (QuoteRefreshIntervalText != null) QuoteRefreshIntervalText.Text = $"{(int)e.NewValue}秒";
        MarkDirty();
    }

    private void WeatherRefreshIntervalSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (WeatherRefreshIntervalText != null) WeatherRefreshIntervalText.Text = $"{(int)e.NewValue}分";
        MarkDirty();
    }

    // ── 颜色 ────────────────────────────────────────────────
    // 2026-09-25（规划 2.7 ③）：hex 输入框 → ColorSwatch 色板（整行可点）。
    private static string PickColor(ColorSwatch sw, string fallback)
        => ColorSwatch.TryParse(sw.Value, out _) ? sw.Value : fallback;
}
