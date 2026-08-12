using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GaokaoCountdown.Helpers;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;

namespace GaokaoCountdown.ViewModels
{
    /// <summary>
    /// 共享天气 ViewModel：消除 ScheduleBarWindow / ExamModeWindow 两份重复的天气加载代码。
    /// 文本/图标走绑定；字体大小与颜色（设置控制）由各 View 自行应用（样式属于 View 职责）。
    /// </summary>
    public partial class WeatherViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

        [ObservableProperty]
        private string iconText = "";

        [ObservableProperty]
        private string city = "";

        [ObservableProperty]
        private string weather = "";

        [ObservableProperty]
        private string tempText = "";

        [ObservableProperty]
        private string windText = "--";

        [ObservableProperty]
        private string humidityText = "--";

        public WeatherViewModel(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>拉取天气并更新属性；失败保持原值（City 为空视为失败）</summary>
        public async Task LoadAsync()
        {
            var result = await WeatherService.FetchAsync(_settings.WeatherCity, _settings.WeatherAdcode);
            if (result == null) return;

            IconText = ColorUtils.GetWeatherEmoji(result.WeatherIcon);
            City = result.Location;
            Weather = result.Weather;
            TempText = $"{result.Temperature}°";
            WindText = !string.IsNullOrWhiteSpace(result.WindDirection)
                ? $"{result.WindDirection} {result.WindPower}".Trim() : "--";
            HumidityText = result.Humidity > 0 ? $"{result.Humidity}%" : "--";
        }
    }
}
