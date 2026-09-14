using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>白板画布背景样式（2.4）</summary>
public enum BoardBackground
{
    /// <summary>纯白（自由板书）</summary>
    Blank,
    /// <summary>网格（理科作图 / 坐标）</summary>
    Grid,
    /// <summary>横线（文科书写 / 英文）</summary>
    Ruled,
    /// <summary>点阵（轻量对齐参考）</summary>
    Dots,
    /// <summary>深色黑板（护眼 / 投影对比）</summary>
    Blackboard,
}

/// <summary>
/// 白板背景绘制 + 导出工具。
///
/// 背景与墨迹分开画（背景在 InkCanvas 之下由宿主 Border 铺），
/// 这样书写层只关心笔迹，切换背景不需要重绘笔画。
/// </summary>
public static class BoardRenderer
{
    public static readonly Color BlackboardColor = Color.FromRgb(0x1E, 0x24, 0x1E);

    /// <summary>背景底色</summary>
    public static Color BaseColor(BoardBackground bg)
        => bg == BoardBackground.Blackboard ? BlackboardColor : Colors.White;

    /// <summary>背景线条颜色（按底色自动区分明暗）</summary>
    private static Color LineColor(BoardBackground bg)
        => bg == BoardBackground.Blackboard
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0x00, 0x00, 0x00);

    /// <summary>在给定画布上绘制背景（网格/横线/点阵）</summary>
    public static void DrawBackground(DrawingContext ctx, BoardBackground bg, Rect area)
    {
        ctx.FillRectangle(new SolidColorBrush(BaseColor(bg)), area);
        if (bg == BoardBackground.Blank || bg == BoardBackground.Blackboard) return;

        var pen = new Pen(new SolidColorBrush(LineColor(bg)), 1);
        const double spacing = 28;

        switch (bg)
        {
            case BoardBackground.Grid:
                for (double x = 0; x <= area.Width; x += spacing)
                    ctx.DrawLine(pen, new Point(x, 0), new Point(x, area.Height));
                for (double y = 0; y <= area.Height; y += spacing)
                    ctx.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;

            case BoardBackground.Ruled:
                for (double y = spacing; y <= area.Height; y += spacing)
                    ctx.DrawLine(pen, new Point(0, y), new Point(area.Width, y));
                break;

            case BoardBackground.Dots:
                var dot = new SolidColorBrush(LineColor(bg));
                for (double x = spacing; x <= area.Width; x += spacing)
                    for (double y = spacing; y <= area.Height; y += spacing)
                        ctx.DrawEllipse(dot, null, new Point(x, y), 1.2, 1.2);
                break;
        }
    }

    /// <summary>
    /// 把一页白板渲染成位图（导出 PNG）。
    /// 背景与由调用方给出的笔画集合一起画，输出与屏幕所见一致。
    /// </summary>
    public static RenderTargetBitmap RenderPage(
        BoardBackground bg,
        IEnumerable<InkStroke> strokes,
        int pixelWidth,
        int pixelHeight,
        double scale = 2.0)
    {
        pixelWidth = Math.Max(pixelWidth, 1);
        pixelHeight = Math.Max(pixelHeight, 1);
        scale = Math.Clamp(scale, 1.0, 4.0);   // 上限防超大画布导出爆内存

        var bmp = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(pixelWidth * scale), (int)Math.Ceiling(pixelHeight * scale)),
            new Vector(96 * scale, 96 * scale));

        using (var ctx = bmp.CreateDrawingContext())
        {
            var area = new Rect(0, 0, pixelWidth, pixelHeight);
            DrawBackground(ctx, bg, area);

            // 笔迹用恒等坐标系渲染（导出的是内容坐标系本身）
            var identity = IdentityInkSurface.Instance;
            foreach (var s in strokes)
            {
                if (s.Tool == InkTool.Laser) continue;   // 激光笔是临时指示，不导出
                ctx.DrawGeometry(null, InkGeometry.BuildPen(s), InkGeometry.BuildGeometry(s, identity));
            }
        }
        return bmp;
    }
}
