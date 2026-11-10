using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Countdown;

/// <summary>
/// 主窗 <c>main.primary</c> 槽位里的倒计时卡片（纯 C# 构建，无 axaml）。
///
/// **控件树只在构造时建一次**，<see cref="Refresh"/> 之后每秒只改 <c>Text</c>/<c>Value</c>——
/// 每秒重建控件树会让布局与渲染每帧全量失效（v3.3.0 的旧债）。
///
/// **配色只从 <see cref="ThemeTokens"/> 取**，不出现任何 <c>#FF...</c>：
/// 否则暗色/亮色主题切换时插件里会留下"写死的灰"。
///
/// ⚠ 必须在 UI 线程构造（插件用 <c>IShellService.PostToUi</c> 保证这一点）。
/// </summary>
internal sealed class CountdownCard : Border
{
    private readonly CountdownSettings _settings;
    private readonly DateTime? _target;
    private readonly DateTime? _start;
    private readonly TextBlock _title;
    private readonly TextBlock _days;
    private readonly TextBlock _unit;
    private readonly TextBlock? _hms;
    private readonly TextBlock? _percent;
    private readonly ProgressBar? _bar;

    /// <summary>构造卡片：解析日期 + 建控件树 + 首次填充。</summary>
    /// <param name="settings">已解析的配置（字段开关决定哪些控件被建）。</param>
    /// <param name="theme">当前主题色板（唯一的取色来源）。</param>
    internal CountdownCard(CountdownSettings settings, ThemeTokens theme)
    {
        _settings = settings;
        _target = CountdownMath.TryParseDateTime(settings.GaokaoDateStr, out var target) ? target : null;
        _start = CountdownMath.TryParseDateTime(settings.StartDateStr, out var start) ? start : null;

        Background = new SolidColorBrush(theme.Surface);
        BorderBrush = new SolidColorBrush(theme.SurfaceAlt);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(28);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;

        _title = new TextBlock
        {
            Text = "距高考",
            FontSize = theme.FontSizeBase * 1.2,
            Foreground = new SolidColorBrush(theme.TextSecondary),
        };

        _days = new TextBlock
        {
            Text = "--",
            FontSize = theme.FontSizeBase * 4.2,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(theme.Accent),
        };

        _unit = new TextBlock
        {
            Text = "天",
            FontSize = theme.FontSizeBase * 1.4,
            Foreground = new SolidColorBrush(theme.TextSecondary),
            Margin = new Thickness(6, 0, 0, 12),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var daysRow = new StackPanel { Orientation = Orientation.Horizontal, IsVisible = settings.ShowDays };
        daysRow.Children.Add(_days);
        daysRow.Children.Add(_unit);

        var textColumn = new StackPanel { Spacing = 4 };
        textColumn.Children.Add(_title);
        textColumn.Children.Add(daysRow);

        if (settings.ShowHours || settings.ShowMinutes || settings.ShowSeconds)
        {
            _hms = new TextBlock
            {
                Text = string.Empty,
                FontSize = theme.FontSizeBase * 0.95,
                Foreground = new SolidColorBrush(theme.TextDim),
            };
            textColumn.Children.Add(_hms);
        }

        var root = new StackPanel
        {
            // false = 横向紧凑（文字左、进度右）；true = 纵向（文字上、进度下）。对齐 v3.3.0 的"条形/环形"版式语义。
            Orientation = settings.CountdownProgressBarStyle ? Orientation.Vertical : Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20,
        };
        root.Children.Add(textColumn);

        if (settings.ShowProgressBar)
        {
            _bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 8,
                MinWidth = 160,
                Foreground = new SolidColorBrush(theme.Accent),
                Background = new SolidColorBrush(theme.SurfaceAlt),
                VerticalAlignment = VerticalAlignment.Center,
            };

            _percent = new TextBlock
            {
                Text = "0.0%",
                FontSize = theme.FontSizeBase * 0.85,
                Foreground = new SolidColorBrush(theme.TextDim),
            };

            var progressColumn = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                MinWidth = 160,
                VerticalAlignment = VerticalAlignment.Center,
            };
            progressColumn.Children.Add(_bar);
            progressColumn.Children.Add(_percent);
            root.Children.Add(progressColumn);
        }

        Child = root;
    }

    /// <summary>当前显示的天数文本（供测试断言，不参与业务）。</summary>
    internal string DaysTextShown => _days.Text ?? string.Empty;

    /// <summary>当前标题文本（供测试断言，不参与业务）。</summary>
    internal string TitleShown => _title.Text ?? string.Empty;

    /// <summary>每秒刷新一次：只改文本与进度值，不动控件树。UI 线程调用。</summary>
    /// <param name="now">当前本地时间（由调用方注入，便于测试与统一时基）。</param>
    public void Refresh(DateTime now)
    {
        if (_target is null)
        {
            // 日期缺失/非法：明确告知，而不是让数字冻结在旧值上（B2 修复的教训）。
            _title.Text = "日期未设置";
            _days.Text = "--";
            _unit.Text = string.Empty;
            if (_hms is not null)
                _hms.Text = "请在设置里填写高考日期";
            UpdateProgress(0.0);
            return;
        }

        var target = _target.Value;

        if (now >= target)
        {
            _title.Text = "高考已结束";
            _days.Text = "0";
            _unit.Text = "天";
            if (_hms is not null)
                _hms.Text = "祝前程似锦";
            UpdateProgress(1.0);
            return;
        }

        _title.Text = "距高考";
        _days.Text = CountdownMath.DaysRemaining(now, target).ToString(CultureInfo.InvariantCulture);
        _unit.Text = "天";

        if (_hms is not null)
            _hms.Text = FormatHms(CountdownMath.TimeRemaining(now, target));

        // 起始日没配好时不给"100%"这种假进度：进度条归零，由设置页引导用户填起始日。
        var progress = _start is null ? 0.0 : CountdownMath.Progress(now, _start.Value, target);
        UpdateProgress(progress);
    }

    /// <summary>拼"时/分/秒"一行；某一档被关掉就整段不出现（全关时该控件根本不存在）。</summary>
    private string FormatHms(TimeSpan remaining)
    {
        var parts = new List<string>(3);

        if (_settings.ShowHours)
            parts.Add($"{remaining.Hours:D2} 时");
        if (_settings.ShowMinutes)
            parts.Add($"{remaining.Minutes:D2} 分");
        if (_settings.ShowSeconds)
            parts.Add($"{remaining.Seconds:D2} 秒");

        return parts.Count == 0 ? string.Empty : "剩余 " + string.Join(" ", parts);
    }

    /// <summary>进度条与百分比同步更新（0..1 输入）。</summary>
    private void UpdateProgress(double progress)
    {
        var percent = Math.Clamp(progress, 0.0, 1.0) * 100.0;

        if (_bar is not null)
            _bar.Value = percent;

        if (_percent is not null)
            _percent.Text = percent.ToString("F1", CultureInfo.InvariantCulture) + "%";
    }
}
