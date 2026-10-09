using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Helpers.GradeAnalysis;

/// <summary>趋势图的一条折线。</summary>
public sealed class TrendSeries
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = ChartPalette.Accent;
    /// <summary>与 <see cref="TrendChart.XLabels"/> 一一对应；null 表示该次考试没有数据（折线断开）。</summary>
    public IReadOnlyList<double?> Values { get; set; } = Array.Empty<double?>();
    /// <summary>是否在数据点旁标注数值（波动分析的数据点少，标注后不必来回看坐标轴）。</summary>
    public bool ShowPointLabels { get; set; }
}

/// <summary>
/// 折线趋势图（手绘）。支持 <see cref="InvertY"/>：排名类指标必须**越靠上越好**，
/// 而排名数字是越小越好的，所以 Y 轴要反转 —— 这是「排名趋势图」与「总分趋势图」
/// 唯一的区别，做成一个开关而不是两个控件。
/// </summary>
public class TrendChart : Control
{
    private IReadOnlyList<TrendSeries> _series = Array.Empty<TrendSeries>();
    private IReadOnlyList<string> _xLabels = Array.Empty<string>();

    public static readonly DirectProperty<TrendChart, IReadOnlyList<TrendSeries>> SeriesProperty =
        AvaloniaProperty.RegisterDirect<TrendChart, IReadOnlyList<TrendSeries>>(
            nameof(Series), o => o._series, (o, v) => o.Series = v);

    public static readonly DirectProperty<TrendChart, IReadOnlyList<string>> XLabelsProperty =
        AvaloniaProperty.RegisterDirect<TrendChart, IReadOnlyList<string>>(
            nameof(XLabels), o => o._xLabels, (o, v) => o.XLabels = v);

    public IReadOnlyList<TrendSeries> Series
    {
        get => _series;
        set => SetAndRaise(SeriesProperty, ref _series, value);
    }

    public IReadOnlyList<string> XLabels
    {
        get => _xLabels;
        set => SetAndRaise(XLabelsProperty, ref _xLabels, value);
    }

    /// <summary>Y 轴反转（排名图用）。</summary>
    public bool InvertY { get; set; }

    public bool ShowLegend { get; set; } = true;
    public string YFormat { get; set; } = "0.##";
    /// <summary>Y 轴标题，画在左上角（如「班级排名」「得分率 %」）。</summary>
    public string YAxisTitle { get; set; } = "";
    /// <summary>是否填充折线下方（单序列时好看，多序列时会互相遮挡，默认关）。</summary>
    public bool FillArea { get; set; }

    private const double PadLeft = 46;
    private const double PadRight = 14;
    private const double PadTop = 22;
    private const double PadBottom = 28;

    static TrendChart()
    {
        AffectsRender<TrendChart>(SeriesProperty, XLabelsProperty);
    }

    public TrendChart()
    {
        MinHeight = 200;
        MinWidth = 240;
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 60 || bounds.Height < 60) return;
        ctx.FillRectangle(ChartPalette.PanelBrush, bounds, 4);

        var typeface = new Typeface(FontFamily.Default);
        if (_xLabels.Count == 0 || _series.Count == 0)
        {
            var ft0 = new FormattedText("暂无数据", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 13, ChartPalette.MutedBrush);
            ctx.DrawText(ft0, new Point(bounds.Center.X - ft0.Width / 2, bounds.Center.Y - ft0.Height / 2));
            return;
        }

        // ── Y 值域 ────────────────────────────────────────────────
        double min = double.MaxValue, max = double.MinValue;
        foreach (var s in _series)
            foreach (var v in s.Values)
            {
                if (!v.HasValue) continue;
                if (v.Value < min) min = v.Value;
                if (v.Value > max) max = v.Value;
            }
        if (min > max) { min = 0; max = 1; }
        if (Math.Abs(max - min) < 1e-9) { max = min + 1; } // 所有值相同：给一个量程，避免除零
        double pad = (max - min) * 0.12;
        min -= pad; max += pad;
        if (InvertY) min = Math.Max(0.5, min); // 排名不会是 0.5 名以下

        var plot = new Rect(bounds.X + PadLeft, bounds.Y + PadTop, bounds.Width - PadLeft - PadRight, bounds.Height - PadTop - PadBottom);
        if (plot.Width < 30 || plot.Height < 30) return;

        double YOf(double v)
        {
            double t = (v - min) / (max - min);
            if (InvertY) t = 1 - t;
            return plot.Bottom - t * plot.Height;
        }
        double XOf(int i)
            => _xLabels.Count == 1
                ? plot.Center.X
                : plot.X + plot.Width * i / (_xLabels.Count - 1);

        // ── 横向网格 + Y 轴刻度 ──────────────────────────────────
        int ticks = 4;
        for (int t = 0; t <= ticks; t++)
        {
            double value = min + (max - min) * t / ticks;
            double y = plot.Bottom - plot.Height * t / ticks;
            ctx.DrawLine(new Pen(ChartPalette.GridBrush, 1), new Point(plot.X, y), new Point(plot.Right, y));
            var ft = new FormattedText(value.ToString(YFormat, CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, ChartPalette.MutedBrush);
            ctx.DrawText(ft, new Point(plot.X - ft.Width - 6, y - ft.Height / 2));
        }

        // ── X 轴标签（考试名，长了就截断）───────────────────────
        for (int i = 0; i < _xLabels.Count; i++)
        {
            double x = XOf(i);
            var label = Truncate(_xLabels[i], 7);
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, ChartPalette.MutedBrush);
            ctx.DrawText(ft, new Point(x - ft.Width / 2, plot.Bottom + 7));
        }

        // ── 折线 ──────────────────────────────────────────────────
        foreach (var s in _series)
        {
            var pen = new Pen(ChartPalette.Brush(s.Color), 2);
            var dot = ChartPalette.Brush(s.Color);

            // 空值处断开：不能跨空缺直接连线，那会把「没考」画成「考了」
            var segment = new List<Point>();
            void Flush()
            {
                if (segment.Count >= 2)
                {
                    if (FillArea && _series.Count == 1)
                    {
                        var area = new List<Point>(segment) { new(segment[^1].X, plot.Bottom), new(segment[0].X, plot.Bottom) };
                        ctx.DrawGeometry(ChartPalette.Fill(s.Color, 0.14), null, PolygonOf(area));
                    }
                    for (int i = 1; i < segment.Count; i++) ctx.DrawLine(pen, segment[i - 1], segment[i]);
                }
                segment.Clear();
            }

            for (int i = 0; i < s.Values.Count && i < _xLabels.Count; i++)
            {
                var v = s.Values[i];
                if (!v.HasValue) { Flush(); continue; }
                segment.Add(new Point(XOf(i), YOf(v.Value)));
            }
            Flush();

            for (int i = 0; i < s.Values.Count && i < _xLabels.Count; i++)
            {
                var v = s.Values[i];
                if (!v.HasValue) continue;
                var p = new Point(XOf(i), YOf(v.Value));
                ctx.DrawEllipse(dot, null, p, 3.2, 3.2);
                if (s.ShowPointLabels)
                {
                    var ft = new FormattedText(v.Value.ToString(YFormat, CultureInfo.InvariantCulture),
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, ChartPalette.TextBrush);
                    ctx.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height - 6));
                }
            }
        }

        // ── Y 轴标题 ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(YAxisTitle))
        {
            var ft = new FormattedText(YAxisTitle, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, ChartPalette.MutedBrush);
            ctx.DrawText(ft, new Point(plot.X, bounds.Y + 5));
        }

        // ── 图例（右上，横排）────────────────────────────────────
        if (ShowLegend && _series.Count > 1)
        {
            double x = plot.Right - 4;
            for (int i = _series.Count - 1; i >= 0; i--)
            {
                var s = _series[i];
                var t = new FormattedText(s.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, ChartPalette.TextBrush);
                x -= t.Width;
                if (x < plot.X) break;
                ctx.DrawText(t, new Point(x, bounds.Y + 5));
                x -= 8;
                ctx.FillRectangle(ChartPalette.Brush(s.Color), new Rect(x - 10, bounds.Y + 9, 10, 3), 1.5f);
                x -= 16;
            }
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static StreamGeometry PolygonOf(IReadOnlyList<Point> pts)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
            g.EndFigure(true);
        }
        return geo;
    }
}
