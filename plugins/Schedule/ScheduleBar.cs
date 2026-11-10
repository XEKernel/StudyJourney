using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// 主窗次区（<see cref="ShellSlots.Secondary"/>）的课表栏：日期星期 + 今天全天课表 + 下一节。
///
/// 次区占主窗 40% 宽 × 全高，只放一行"下一节"会让整块面板空着，所以列成全天清单：
/// <list type="bullet">
/// <item>顶部：日期 + 星期（调休时补一行"今天按周 X 上"）；</item>
/// <item>中间：每节一行（见 <see cref="ScheduleRow"/>），当前节强调色、已上完的弱化；</item>
/// <item>底部：下一节 / 今天的课已结束。</item>
/// </list>
///
/// 本类只管**布局与刷新**；"哪节正在上、哪行该什么颜色、下一节是哪节"全在
/// <see cref="TodayView"/> 里算（纯逻辑，可单独断言）。
///
/// 三条实现约束：
/// <list type="number">
/// <item>**纯 C#，不用 axaml** —— axaml 的 <c>x:Name</c> 在本工程的编译配置下不生成字段，
/// 运行期恒为 null，排查成本极高；</item>
/// <item>**颜色只从 <see cref="ThemeTokens"/> 取**，不出现任何 <c>#FF...</c> 字面量
/// （否则全项目会散出五种不同的"灰"）；</item>
/// <item>刷新幂等：每秒被调一次，只改文本与可见性；行控件构造时建满一池，
/// **绝不重建**——重建会让滚动位置每秒归零。</item>
/// </list>
/// </summary>
internal sealed class ScheduleBar : Border
{
    /// <summary>行控件池容量：今天节次多于此数时折叠为「还有 N 节」，避免撑爆面板。</summary>
    private const int MaxRows = 12;

    private readonly IScheduleProvider _provider;
    private readonly ThemeTokens _theme;

    private readonly TextBlock _headerLine;
    private readonly TextBlock _makeupLine;
    private readonly TextBlock _emptyLine;
    private readonly TextBlock _nextLine;
    private readonly TextBlock _truncationLine;
    private readonly ScrollViewer _list;
    private readonly ScheduleRow[] _rows;

    /// <param name="provider">课表能力（本插件自己的实现，纯内存、同步返回）。</param>
    /// <param name="theme">当前主题色板。**必须在 UI 线程构造本控件**。</param>
    public ScheduleBar(IScheduleProvider provider, ThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(theme);

        _provider = provider;
        _theme = theme;

        Background = Brush(theme.SurfaceAlt);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14, 12);
        BorderThickness = new Thickness(0);

        var baseSize = theme.FontSizeBase > 0 ? theme.FontSizeBase : 14;

        _headerLine = MakeText(theme.TextPrimary, baseSize * 1.05, FontWeight.SemiBold);
        _headerLine.Margin = new Thickness(0, 0, 0, 2);

        _makeupLine = MakeText(theme.Warning, baseSize * 0.85, FontWeight.SemiBold);
        _makeupLine.IsVisible = false;

        _emptyLine = MakeText(theme.TextDim, baseSize * 0.95, FontWeight.Normal);
        _emptyLine.Text = "今天没课";
        _emptyLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _emptyLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _emptyLine.IsVisible = false;

        _nextLine = MakeText(theme.TextSecondary, baseSize * 0.9, FontWeight.Normal);
        _nextLine.Margin = new Thickness(0, 6, 0, 0);
        _nextLine.IsVisible = false;

        _truncationLine = MakeText(theme.TextDim, baseSize * 0.8, FontWeight.Normal);
        _truncationLine.Margin = new Thickness(0, 2, 0, 0);
        _truncationLine.IsVisible = false;

        // 行控件一次性建满（MaxRows 个），Refresh 只复用不新建。
        var rowsPanel = new StackPanel();
        _rows = new ScheduleRow[MaxRows];
        for (var i = 0; i < MaxRows; i++)
        {
            _rows[i] = new ScheduleRow(theme) { IsVisible = false };
            rowsPanel.Children.Add(_rows[i]);
        }

        _list = new ScrollViewer
        {
            Content = rowsPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // 列表与空态占同一格、互斥显示：没课时不留一块空白列表。
        var body = new Grid { Margin = new Thickness(0, 4) };
        body.Children.Add(_list);
        body.Children.Add(_emptyLine);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        root.Children.Add(_headerLine);
        root.Children.Add(_makeupLine);
        root.Children.Add(body);
        root.Children.Add(_truncationLine);
        root.Children.Add(_nextLine);

        Grid.SetRow(_headerLine, 0);
        Grid.SetRow(_makeupLine, 1);
        Grid.SetRow(body, 2);
        Grid.SetRow(_truncationLine, 3);
        Grid.SetRow(_nextLine, 4);

        Child = root;
    }

    /// <summary>
    /// 按给定时刻刷新显示。**UI 线程调用**（插件里一律经 <c>IShellService.PostToUi</c> 投递）。
    /// </summary>
    public void Refresh(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var task = _provider.GetDayAsync(date);

        if (task.IsCompletedSuccessfully)
        {
            Apply(task.Result, now.TimeOfDay);
            return;
        }

        // 本插件内的 provider 是同步的（纯内存快照），正常走不到这里。
        // 万一将来换成异步实现：回来后补刷一次，绝不在 UI 线程阻塞等待。
        _ = task.ContinueWith(
            finished => Dispatcher.UIThread.Post(() => Apply(finished.Result, now.TimeOfDay)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Apply(Result<TodaySchedule> result, TimeSpan now)
    {
        if (result.IsFailure || result.Value is not { } schedule)
            return;

        var view = TodayView.Build(schedule, now, MaxRows);

        _headerLine.Text = view.Header;

        _makeupLine.Text = view.MakeupHint;
        _makeupLine.IsVisible = view.MakeupHint.Length > 0;

        for (var i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            row.IsVisible = i < view.ShownCount;
            if (row.IsVisible)
                row.Apply(view.Periods[i], TodayView.FormatRange(view.Periods[i]), view.StateAt(i));
        }

        _truncationLine.Text = view.HiddenCount > 0 ? $"还有 {view.HiddenCount} 节" : string.Empty;
        _truncationLine.IsVisible = view.HiddenCount > 0;

        _list.IsVisible = !view.IsEmpty;
        _emptyLine.IsVisible = view.IsEmpty;

        _nextLine.Text = view.NextText;
        _nextLine.IsVisible = view.NextText.Length > 0;
    }

    /// <summary>当前显示的行数（供测试断言）。</summary>
    internal int VisibleRowCount => _rows.Count(row => row.IsVisible);

    /// <summary>取第 <paramref name="index"/> 个行控件（供测试断言）。</summary>
    internal ScheduleRow RowAt(int index) => _rows[index];

    /// <summary>是否正在显示"今天没课"空态。</summary>
    internal bool IsEmptyStateShown => _emptyLine.IsVisible;

    /// <summary>顶部日期星期文本（供测试断言）。</summary>
    internal string HeaderTextShown => _headerLine.Text ?? string.Empty;

    /// <summary>底部"下一节 / 今天的课已结束"文本，未显示时为空串（供测试断言）。</summary>
    internal string NextTextShown => _nextLine.IsVisible ? _nextLine.Text ?? string.Empty : string.Empty;

    /// <summary>折叠提示文本，未显示时为空串（供测试断言）。</summary>
    internal string TruncationTextShown
        => _truncationLine.IsVisible ? _truncationLine.Text ?? string.Empty : string.Empty;

    private TextBlock MakeText(Color color, double fontSize, FontWeight weight) => new()
    {
        Foreground = Brush(color),
        FontFamily = new FontFamily(_theme.FontFamily),
        FontSize = fontSize,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    /// <summary>不可变画刷：每秒刷新也不会反复分配可变对象。</summary>
    private static IBrush Brush(Color color) => new ImmutableSolidColorBrush(color);
}
