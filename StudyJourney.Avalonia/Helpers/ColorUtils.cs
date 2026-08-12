using System;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>跨窗口共享工具：颜色解析 + 天气表情符号（Avalonia 版，替代 WPF ColorUtils）</summary>
public static class ColorUtils
{
    /// <summary>解析十六进制颜色字符串（#RRGGBB / #AARRGGBB），失败返回兜底色</summary>
    public static IBrush ParseBrush(string hex, string fallbackHex)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            try { return new SolidColorBrush(Color.Parse(fallbackHex)); }
            catch { return Brushes.White; }
        }
    }

    /// <summary>解析颜色，失败返回兜底</summary>
    public static Color ParseColor(string hex, string fallbackHex)
    {
        try { return Color.Parse(hex); }
        catch
        {
            try { return Color.Parse(fallbackHex); }
            catch { return Colors.White; }
        }
    }

    /// <summary>天气图标代码 → 表情符号（与 WPF 版一致）</summary>
    public static string GetWeatherEmoji(string icon)
    {
        return icon switch
        {
            "01" or "01d" or "01n"   => "☀️",
            "02" or "02d" or "02n"   => "🌤",
            "03" or "03d" or "03n"   => "☁️",
            "04" or "04d" or "04n"   => "☁️",
            "09" or "09d" or "09n"   => "🌧",
            "10" or "10d" or "10n"   => "🌦",
            "11" or "11d" or "11n"   => "⛈",
            "13" or "13d" or "13n"   => "❄️",
            "50" or "50d" or "50n"   => "🌫",
            "100"                    => "☀️",
            "101"                    => "🌤",
            "102"                    => "🌥",
            "103"                    => "☁️",
            "104"                    => "☁️",
            "150"                    => "🌙",
            "151"                    => "🌤",
            "152"                    => "🌥",
            "153"                    => "☁️",
            "154"                    => "☁️",
            "300"                    => "🌧",
            "301"                    => "🌧",
            "302"                    => "⛈",
            "303"                    => "⛈",
            "304"                    => "⛈",
            "305"                    => "🌧",
            "306"                    => "🌧",
            "307"                    => "🌧",
            "308"                    => "🌧",
            "309"                    => "🌧",
            "310"                    => "🌧",
            "311"                    => "🌧",
            "312"                    => "🌧",
            "313"                    => "🌧",
            "400"                    => "❄️",
            "401"                    => "❄️",
            "402"                    => "❄️",
            "403"                    => "❄️",
            "404"                    => "🌨",
            "405"                    => "🌨",
            "406"                    => "🌨",
            "407"                    => "🌨",
            "500"                    => "🌫",
            "501"                    => "🌫",
            "502"                    => "🌫",
            "503"                    => "🌫",
            "504"                    => "🌫",
            "507"                    => "🌬",
            "508"                    => "🌬",
            "509"                    => "🌧",
            "510"                    => "🌨",
            "511"                    => "🌧",
            "512"                    => "🌧",
            "513"                    => "🌧",
            "514"                    => "🌧",
            "515"                    => "🌧",
            _                         => "🌡"
        };
    }
}
