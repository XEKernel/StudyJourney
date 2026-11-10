using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>一节课在今日列表里的状态，唯一决定这一行怎么着色。</summary>
internal enum ScheduleRowState
{
    /// <summary>还没开始。</summary>
    Upcoming,

    /// <summary>正在上：强调色 + 加粗 + 左侧 2px 竖条。</summary>
    Current,

    /// <summary>已经上完：弱化。</summary>
    Finished,
}

/// <summary>
/// 课表列表的一行：「节次 · 科目 · 起止时间」。
///
/// 四列固定布局（<c>2,8,24,*,Auto</c>）：竖条 / 间隙 / 节次 / 科目 / 时间。
/// 节次列宽固定且右对齐，1 位数与 2 位数才能上下对齐——这是"等宽对齐"的要求，
/// 不要去调字体等宽（中文字体里等宽数字反而更难看）。
///
/// 约束与 <see cref="ScheduleBar"/> 相同：控件只在构造时建一次，
/// <see cref="Apply"/> 每秒被调也只改文本/颜色/粗细，不动控件树。
/// </summary>
internal sealed class ScheduleRow : Grid
{
    private readonly Border _accentBar;
    private readonly TextBlock _period;
    private readonly TextBlock _subject;
    private readonly TextBlock _time;

    private readonly IBrush _accentBrush;
    private readonly IBrush _primaryBrush;
    private readonly IBrush _dimBrush;

    /// <param name="theme">当前主题色板（唯一的取色来源）。**必须在 UI 线程构造**。</param>
    internal ScheduleRow(ThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _accentBrush = new ImmutableSolidColorBrush(theme.Accent);
        _primaryBrush = new ImmutableSolidColorBrush(theme.TextPrimary);
        _dimBrush = new ImmutableSolidColorBrush(theme.TextDim);

        var baseSize = theme.FontSizeBase > 0 ? theme.FontSizeBase : 14;
        var font = new FontFamily(theme.FontFamily);

        ColumnDefinitions = new ColumnDefinitions("2,8,24,*,Auto");
        MinHeight = 22;
        Margin = new Thickness(0, 1);

        _accentBar = new Border
        {
            Background = _accentBrush,
            CornerRadius = new CornerRadius(1),
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 2),
        };

        _period = new TextBlock
        {
            FontFamily = font,
            FontSize = baseSize * 0.9,
            Foreground = _primaryBrush,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _subject = new TextBlock
        {
            FontFamily = font,
            FontSize = baseSize * 0.95,
            Foreground = _primaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };

        _time = new TextBlock
        {
            FontFamily = font,
            FontSize = baseSize * 0.8,
            Foreground = _primaryBrush,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Children.Add(_accentBar);
        Children.Add(_period);
        Children.Add(_subject);
        Children.Add(_time);

        SetColumn(_accentBar, 0);
        SetColumn(_period, 2);
        SetColumn(_subject, 3);
        SetColumn(_time, 4);
    }

    /// <summary>把一个节次写进这一行并按状态着色。**只改属性，不重建控件。**</summary>
    /// <param name="period">要显示的节次。</param>
    /// <param name="timeRange">已格式化好的起止时间（如 <c>09:15–10:00</c>）。</param>
    /// <param name="state">该节当前处于什么状态。</param>
    internal void Apply(ScheduledPeriod period, string timeRange, ScheduleRowState state)
    {
        ArgumentNullException.ThrowIfNull(period);

        _period.Text = period.Period.ToString(CultureInfo.InvariantCulture);
        _subject.Text = period.Subject ?? string.Empty;
        _time.Text = timeRange;

        var brush = state switch
        {
            ScheduleRowState.Current => _accentBrush,
            ScheduleRowState.Finished => _dimBrush,
            _ => _primaryBrush,
        };

        _period.Foreground = brush;
        _subject.Foreground = brush;
        _time.Foreground = brush;

        // 只加粗不改字号：字号变化会让整列在切换那一秒抖动。
        _subject.FontWeight = state == ScheduleRowState.Current ? FontWeight.Bold : FontWeight.Normal;
        _accentBar.IsVisible = state == ScheduleRowState.Current;
    }

    /// <summary>节次文本（供测试断言）。</summary>
    internal string PeriodShown => _period.Text ?? string.Empty;

    /// <summary>科目文本（供测试断言）。</summary>
    internal string SubjectShown => _subject.Text ?? string.Empty;

    /// <summary>起止时间文本（供测试断言）。</summary>
    internal string TimeShown => _time.Text ?? string.Empty;

    /// <summary>科目当前前景色（供测试断言着色状态）。</summary>
    internal Color? SubjectColor => (_subject.Foreground as ISolidColorBrush)?.Color;

    /// <summary>科目当前字重（当前课应为 <see cref="FontWeight.Bold"/>）。</summary>
    internal FontWeight SubjectWeight => _subject.FontWeight;

    /// <summary>左侧强调竖条是否显示（当前课才显示）。</summary>
    internal bool AccentBarShown => _accentBar.IsVisible;
}
