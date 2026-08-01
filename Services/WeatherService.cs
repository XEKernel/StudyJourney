using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GaokaoCountdown.Helpers;

using GaokaoCountdown.Models;
namespace GaokaoCountdown.Services
{
    /// <summary>天气数据结果</summary>
    public class WeatherResult
    {
        public string Location { get; set; } = "";
        public string Weather { get; set; } = "";
        public string WeatherIcon { get; set; } = "";
        public int Temperature { get; set; }
        public string WindDirection { get; set; } = "";
        public string WindPower { get; set; } = "";
        public int Humidity { get; set; }
    }

    /// <summary>共享天气服务：HTTP 调用 + JSON 解析</summary>
    public static class WeatherService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static async Task<WeatherResult?> FetchAsync(string city, string adcode)
        {
            try
            {
                city = string.IsNullOrWhiteSpace(city) ? "北京" : city.Trim();
                adcode = (adcode ?? "").Trim();
                string url = $"https://uapis.cn/api/v1/misc/weather?city={Uri.EscapeDataString(city)}" +
                             $"&adcode={Uri.EscapeDataString(adcode)}" +
                             "&extended=false&forecast=false&hourly=false&minutely=false&indices=false&lang=zh";

                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string rCity    = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                string district = root.TryGetProperty("district", out var d) ? d.GetString() ?? "" : "";

                return new WeatherResult
                {
                    Location      = !string.IsNullOrWhiteSpace(district) ? district
                                   : !string.IsNullOrWhiteSpace(rCity) ? rCity : city,
                    Weather       = root.TryGetProperty("weather", out var w) ? w.GetString() ?? "" : "",
                    WeatherIcon   = root.TryGetProperty("weather_icon", out var wi) ? wi.GetString() ?? "" : "",
                    Temperature   = root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number
                                    ? (int)t.GetDouble() : 0,
                    WindDirection = root.TryGetProperty("wind_direction", out var wd) ? wd.GetString() ?? "" : "",
                    WindPower     = root.TryGetProperty("wind_power", out var wp) ? wp.GetString() ?? "" : "",
                    Humidity      = root.TryGetProperty("humidity", out var h) && h.ValueKind == JsonValueKind.Number
                                    ? (int)h.GetDouble() : 0
                };
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.Warn($"天气获取失败 (city={city}): {ex.Message}");
                return null;
            }
        }
    }
}
