using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using MessageBox = GaokaoCountdown.Views.DialogHelper;
using GaokaoCountdown.Helpers;
using Hardcodet.Wpf.TaskbarNotification;
using GaokaoCountdown.Models;
using GaokaoCountdown.Services;
namespace GaokaoCountdown.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>从 SettingWindow 调用的公开刷新方法</summary>
        public async Task RefreshQuoteAsync()
        {
            await LoadDailyQuoteAsync();
        }

        /// <summary>调用 API 加载一言，并应用当前样式设置、淡入动画</summary>
        private async Task LoadDailyQuoteAsync()
        {
            // 窗口隐藏时（上课/考试中）不请求 API
            if (Visibility != Visibility.Visible || !ShowDailyQuote) return;
            try
            {
                string url = string.IsNullOrWhiteSpace(QuoteApiUrl)
                    ? "https://uapis.cn/api/v1/saying" : QuoteApiUrl;
                var json = await _httpClient.GetStringAsync(url);

                // 使用动态字段名解析 JSON（支持自定义 API）
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string fieldName = string.IsNullOrWhiteSpace(QuoteTextFieldName)
                    ? "text" : QuoteTextFieldName.Trim();
                string? quoteText = root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString() : null;
                if (string.IsNullOrWhiteSpace(quoteText)) return;

                string text = $"「{quoteText.Trim()}」";

                await Dispatcher.InvokeAsync(() =>
                {
                    // 应用当前样式设置
                    ApplyQuoteStyle();
                    DailyQuoteTb.Text = text;
                    // 淡入动画
                    var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8))
                    {
                        EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut }
                    };
                    DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, anim);
                    DailyQuoteTb.Visibility = Visibility.Visible;
                });
            }
            catch
            {
                // 网络异常时静默处理
            }
        }

        /// <summary>将当前设置中的字体大小、颜色、斜体应用到 DailyQuoteTb</summary>
        public void ApplyQuoteStyle()
        {
            DailyQuoteTb.FontSize = QuoteFontSize;
            DailyQuoteTb.FontStyle = QuoteItalic ? FontStyles.Italic : FontStyles.Normal;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(QuoteForegroundHex);
                DailyQuoteTb.Foreground = new SolidColorBrush(c);
            }
            catch { }
        }

        /// <summary>启动/重启每日一言自动切换定时器</summary>
        public void StartQuoteRefreshTimer()
        {
            _quoteRefreshTimer?.Stop();
            _quoteRefreshTimer = null;

            if (!ShowDailyQuote) return;
            int intervalSec = QuoteAutoRefreshInterval;
            if (intervalSec <= 0) return;

            _quoteRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(intervalSec)
            };
            _quoteRefreshTimer.Tick += async (_, _) =>
            {
                // 窗口隐藏时不刷新
                if (Visibility != Visibility.Visible) return;
                // 淡出 → 重新加载 → 淡入
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4))
                {
                    EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += async (_, _) => await LoadDailyQuoteAsync();
                DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            _quoteRefreshTimer.Start();
        }

        private async void DailyQuoteTb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 点击刷新：先淡出再重新加载
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += async (_, _) => await LoadDailyQuoteAsync();
            DailyQuoteTb.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
