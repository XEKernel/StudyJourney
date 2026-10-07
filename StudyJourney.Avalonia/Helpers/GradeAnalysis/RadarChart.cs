using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Helpers.GradeAnalysis;

/// <summary>雷达图的一条数据序列（一个学生 / 一个小组）。</summary>
public sealed class RadarSeries
{
    public string Name { get; set; } = "";
    /// <summary>各维度的值，建议归一化到 0..1（本模块喂的是**得分率**）。长度应等于轴标签数。</summary>
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
    public string Color { get; set; } = ChartPalette.Accent;
}

/// <summary>
/// 六科雷达图（手绘）。
///
/// <para><b>为什么不用 LiveCharts2</b>：见 <c>.workbuddy/GRADE_ANALYSIS_PLAN.md</c> 的选型记录 ——
/// 其 Avalonia 包编译目标为 Avalonia 11 + SkiaSharp 2.88，而本项目是 Avalonia 12 + SkiaSharp 3.119，
/// NuGet 会**静默**升级 SkiaSharp 让问题逃过构建门禁。雷达图本身只是「按角度铺点 + 连多边形」，
/// 手绘不到 150 行，且能完全贴合本项目的配色与圆角纪律。</para>
///
/// <para>用 <see cref="Control"/> + <see cref="Render"/> 实现，与项目既有的
/// <c>InkCanvas</c> / <c>WhiteboardWindow</c> 同款做法，不引入任何新依赖。</para>
/// </summary>
public class RadarChart : Control
{
    private IReadOnlyList<RadarSeries> _series = Array.Empty<RadarSeries>();
    private IReadOnlyList<string> _axisLabels = Array.Empty<string>();

    public static readonly DirectProperty<RadarChart, IReadOnlyList<RadarSeries>> SeriesProperty =
        AvaloniaProperty.RegisterDirect<RadarChart, IReadOnlyList<RadarSeries>>(
            nameof(Series), o => o._series, (o, v) => o.Series = v);

    public static readonly DirectProperty<RadarChart, IReadOnlyList<string>> AxisLabelsProperty =
        AvaloniaProperty.RegisterDirect<RadarChart, IReadOnlyList<string>>(
            nameof(AxisLabels), o => o._axisLabels, (o, v) => o.AxisLabels = v);

    /// <summary>数据序列（支持多人叠加，用于单人 PK 的对比雷达图）。</summary>
    public IReadOnlyList<RadarSeries> Series
    {
        get => _series;
        set => SetAndRaise(SeriesProperty, ref _series, value);
    }

    /// <summary>轴标签（语数英物化生）。</summary>
    public IReadOnlyList<string> AxisLabels
    {
        get => _axisLabels;
        set => SetAndRaise(AxisLabelsProperty, ref _axisLabels, value);
    }

    /// <summary>刻度环数量。</summary>
    public int RingCount { get; set; } = 4;

    /// <summary>是否在图例里显示「名字 + 平均得分率」。</summary>
    public bool ShowLegend { get; set; } = true;

    static RadarChart()
    {
        AffectsRender<RadarChart>(SeriesProperty, AxisLabelsProperty);
    }

    public RadarChart()
    {
        MinHeight = 260;
        MinWidth = 260;
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 40 || bounds.Height < 40) return;

        // 圆形底（面板色），让图表在大屏上和其他卡片一致
        ctx.FillRectangle(ChartPalette.PanelBrush, bounds, 4);

        int n = _axisLabels.Count;
        if (n < 3)
        {
            DrawCentered(ctx, "暂无数据", bounds, ChartPalette.MutedBrush, 13);
            return;
        }

        double legendH = ShowLegend && _series.Count > 0 ? 22 : 0;
        double legendW = ShowLegend ? 150 : 0;

        var plot = new Rect(bounds.X + 12, bounds.Y + 26, bounds.Width - legendW - 24, bounds.Height - legendH - 46);
        if (plot.Width < 60 || plot.Height < 60) return;

        double cx = plot.Center.X;
        double cy = plot.Center.Y;
        double radius = Math.Min(plot.Width, plot.Height) / 2;
        if (radius < 30) return;

        var gridPen = new Pen(ChartPalette.GridBrush, 1);
        var axisPen = new Pen(ChartPalette.BorderBrush, 1);

        // ── 刻度环 ──────────────────────────────────────────────────
        for (int ring = 1; ring <= RingCount; ring++)
        {
            double r = radius * ring / RingCount;
            var pts = new List<Point>(n);
            for (int i = 0; i < n; i++) pts.Add(PointOn(cx, cy, r, i, n));
            ctx.DrawGeometry(null, ring == RingCount ? axisPen : gridPen, Polygon(pts, true));
        }

        // ── 轴线 + 轴标签 ───────────────────────────────────────────
        var labelTypeface = new Typeface(FontFamily.Default);
        for (int i = 0; i < n; i++)
        {
            var outer = PointOn(cx, cy, radius, i, n);
            ctx.DrawLine(axisPen, new Point(cx, cy), outer);

            var labelPt = PointOn(cx, cy, radius + 18, i, n);
            var ft = new FormattedText(_axisLabels[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                labelTypeface, 12, ChartPalette.TextBrush);
            ctx.DrawText(ft, new Point(labelPt.X - ft.Width / 2, labelPt.Y - ft.Height / 2));
        }

        // ── 数据多边形 ──────────────────────────────────────────────
        foreach (var s in _series)
        {
            if (s.Values.Count < n) continue;
            var fill = ChartPalette.Fill(s.Color, 0.18);
            var stroke = new Pen(ChartPalette.Brush(s.Color), 2);
            var pts = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double v = Math.Clamp(s.Values[i], 0, 1);
                pts.Add(PointOn(cx, cy, radius * v, i, n));
            }
            ctx.DrawGeometry(fill, stroke, Polygon(pts, true));

            // 数据点
            foreach (var p in pts) ctx.DrawEllipse(ChartPalette.Brush(s.Color), null, p, 2.8, 2.8);
        }

        // ── 图例（右侧竖排）─────────────────────────────────────────
        if (ShowLegend && _series.Count > 0)
        {
            double ly = bounds.Y + 26;
            foreach (var s in _series)
            {
                double avg = 0;
                if (s.Values.Count > 0)
                {
                    foreach (var v in s.Values) avg += v;
                    avg /= s.Values.Count;
                }
                var swatch = new Rect(bounds.Right - legendW + 4, ly + 3, 10, 10);
                ctx.FillRectangle(ChartPalette.Brush(s.Color), swatch, 2);
                var ft = new FormattedText($"{s.Name} {(avg * 100):0.0}%", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, labelTypeface, 12, ChartPalette.TextBrush);
                ctx.DrawText(ft, new Point(swatch.Right + 6, ly));
                ly += 20;
            }
        }
    }

    /// <summary>第 i 个轴上的点（从正上方开始顺时针）。</summary>
    private static Point PointOn(double cx, double cy, double r, int i, int n)
    {
        double angle = -Math.PI / 2 + 2 * Math.PI * i / n;
        return new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
    }

    private static StreamGeometry Polygon(IReadOnlyList<Point> pts, bool close)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], close);
            for (int i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
            g.EndFigure(close);
        }
        return geo;
    }

    private static void DrawCentered(DrawingContext ctx, string text, Rect area, IBrush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), size, brush);
        ctx.DrawText(ft, new Point(area.Center.X - ft.Width / 2, area.Center.Y - ft.Height / 2));
    }
}
