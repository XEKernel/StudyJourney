using System;
using System.Windows.Media;

namespace GaokaoCountdown
{
    /// <summary>跨窗口共享的工具方法</summary>
    public static class ColorUtils
    {
        /// <summary>安全解析十六进制颜色字符串为 SolidColorBrush</summary>
        /// <param name="hex">如 "#FF8844" 或 "#AAFF8844"</param>
        /// <param name="fallback">解析失败时的备用颜色，默认白</param>
        public static SolidColorBrush ParseColor(string? hex, string fallback = "#FFFFFFFF")
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(
                    string.IsNullOrWhiteSpace(hex) ? fallback : hex);
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
            catch
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(fallback);
                    var brush = new SolidColorBrush(c);
                    brush.Freeze();
                    return brush;
                }
                catch
                {
                    return new SolidColorBrush(Colors.White);
                }
            }
        }

        /// <summary>根据高德天气图标代码返回对应 emoji</summary>
        /// <param name="icon">高德返回的 weather icon 字段</param>
        public static string GetWeatherEmoji(string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon)) return "🌤";

            return icon switch
            {
                // 高德数字天气代码
                "100" or "晴" or "sunny" => "☀️",
                "101" or "少云" or "partly_cloudy" => "⛅",
                "102" or "晴间多云" or "fair" => "🌤",
                "103" or "104" or "多云" or "cloudy" => "☁️",
                "200" or "有风" or "windy" => "💨",
                "300" or "阵雨" or "shower" => "🌦",
                "301" or "雷阵雨" or "thundershower" => "⛈",
                "400" or "小雪" or "中雪" or "大雪" or "暴雪"
                    or "light_snow" or "moderate_snow" or "heavy_snow" or "snowstorm" => "❄️",
                "500" or "雾" or "fog" => "🌫",
                // 其他常用
                "阴" or "overcast" => "🌥",
                "小雨" or "light_rain" => "🌧",
                "中雨" or "moderate_rain" => "🌧",
                "大雨" or "heavy_rain" => "🌧",
                "暴雨" or "rainstorm" => "🌧",
                "霾" or "haze" => "🌫",
                "扬沙" or "沙尘暴" or "sand" or "sandstorm" or "dust" => "💨",
                "雨夹雪" or "sleet" => "🌨",
                "冻雨" or "freezing_rain" => "🌨",
                "热" or "hot" => "🔥",
                "冷" or "cold" => "🥶",
                "雷阵雨伴有冰雹" or "hail" => "🌨",
                "强风/劲风" or "gale" or "大风" or "风暴" or "飓风" or "热带风暴" => "🌀",
                _ => "🌤"
            };
        }
    }
}
