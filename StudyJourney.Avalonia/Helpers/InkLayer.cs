using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace StudyJourney.Avalonia.Helpers;

// ═══════════════════════════════════════════════════════════════════════════
//  公共墨迹组件（InkLayer）—— PLANNING 2.6 步骤 3 打底
//
//  设计目标（一次抽象，三处复用）：
//    2.1 内置 PDF 阅读器   → 墨迹挂在**文档内容坐标系**（随页面滚动/缩放一起动）
//    2.3 屏幕批注覆盖层     → 墨迹固定在**屏幕坐标系**（悬浮在任意应用之上，不随底层滚动）
//    2.4 白板               → 独立画布坐标系（可分页、可导出 PNG）
//
//  三者共用同一套"笔迹数据 + 绘制 + 撤销重做 + 手势分流"，只把**坐标系变换**
//  交给宿主实现（见 IInkSurface）。这是刻意的分层：坐标逻辑差异最大，单独隔离；
//  绘制与交互逻辑高度一致，集中在这里，避免三份重复实现各自演化出 bug。
//
//  输入分流约定（PLANNING 通用交互约定，班级电脑为触屏）：
//    · 触控笔（PointerType.Pen）          → 书写墨迹（始终）
//    · 鼠标左键                           → 书写墨迹
//    · 手指触摸（PointerType.Touch）       → **默认留给滚动/平移**，不产生墨迹
//                                            （可通过 InkCanvas.AllowTouchInk 打开手指书写）
//    · 双指                               → 缩放（由宿主的 ScrollViewer/手势处理，本层不拦截）
//
//  性能约定（PLANNING 通用交互约定）：
//    · 书写中：当前笔画收集到**活动笔画**，用一条 PolylineGeometry 增量重绘（而非每点重建全部）
//    · 落笔结束：把活动笔画提交进 Strokes 列表，之后只增量追加绘制
//    · 擦除：命中测试用"点到线段距离"，与绘制同一条数学路径，不需要位图命中
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>笔迹工具</summary>
public enum InkTool
{
    /// <summary>不书写（宿主可用它做"只读浏览"或"暂停批注"）</summary>
    None,
    /// <summary>画笔（实线）</summary>
    Pen,
    /// <summary>荧光笔（半透明、粗、圆头、后绘于普通笔迹之下更自然，这里统一按笔画顺序叠加）</summary>
    Highlighter,
    /// <summary>橡皮（按"整笔"擦除，符合板书直觉；不产生半截笔画）</summary>
    Eraser,
    /// <summary>激光笔（短暂的指示点，不落盘、不参与撤销）</summary>
    Laser,
}

/// <summary>一次落笔产生的笔画（坐标一律为**内容坐标系**，即宿主坐标系）</summary>
public sealed class InkStroke
{
    public List<Point> Points { get; } = new();
    public Color Color { get; init; } = Colors.Black;
    public double Thickness { get; init; } = 3;
    public InkTool Tool { get; init; } = InkTool.Pen;

    /// <summary>荧光笔的透明度（画的时候用带 alpha 的画笔）</summary>
    public bool IsHighlighter => Tool == InkTool.Highlighter;

    /// <summary>笔画的有效包围盒（擦除命中测试的快筛；点少时退化成一个点）</summary>
    public Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return default;
            double minX = Points[0].X, maxX = Points[0].X;
            double minY = Points[0].Y, maxY = Points[0].Y;
            foreach (var p in Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            double pad = Thickness;   // 笔画有宽度，包围盒外扩一点
            return new Rect(minX - pad, minY - pad, (maxX - minX) + pad * 2, (maxY - minY) + pad * 2);
        }
    }

    public InkStroke Clone()
    {
        var s = new InkStroke { Color = Color, Thickness = Thickness, Tool = Tool };
        s.Points.AddRange(Points);
        return s;
    }
}

/// <summary>
/// 墨迹画布的宿主契约：把"屏幕上的指针位置"映射到"宿主自己的内容坐标系"。
///
/// 三处实现：
///   · PDF 页画布 → 内容坐标 = 页面坐标（滚动/缩放由宿主做逆变换）
///   · 屏幕批注   → 内容坐标 = 屏幕坐标（恒等变换）
///   · 白板       → 内容坐标 = 画布逻辑坐标（可能带平移/缩放）
/// </summary>
public interface IInkSurface
{
    /// <summary>把画布局部坐标（相对 InkCanvas 左上角）转成内容坐标"</summary>
    Point ToContent(Point canvasPoint);

    /// <summary>把内容坐标转回画布局部坐标（绘制/命中时用）</summary>
    Point FromContent(Point contentPoint);

    /// <summary>内容坐标下的可视区域（用于裁剪绘制；返回 null = 不裁剪，全画）</summary>
    Rect? VisibleContentRect { get; }
}

/// <summary>恒等变换（屏幕批注 / 简单画布直接用它）</summary>
public sealed class IdentityInkSurface : IInkSurface
{
    public static readonly IdentityInkSurface Instance = new();
    public Point ToContent(Point canvasPoint) => canvasPoint;
    public Point FromContent(Point contentPoint) => contentPoint;
    public Rect? VisibleContentRect => null;
}

/// <summary>
/// 缩放变换的墨迹坐标系：画布坐标 = 内容坐标 × <see cref="Zoom"/>。
///
/// 适用场景（PDF 阅读器，PLANNING 2.1）：画布本身按 zoom 放大铺在**未缩放的文档空间**之上，
/// 笔迹存的是文档空间坐标，因此
///   · 缩放 → 改 Zoom + 重新布局画布尺寸 → 笔迹自动跟着内容放大（几何重算即对齐）
///   · 滚动 → **不需要参与换算**：画布放在 ScrollViewer 的内容里，滚动由宿主完成，
///            画布与页面一起被滚动，笔迹天然跟随内容（这正是需求"墨迹随内容滚动"）
/// </summary>
public sealed class ZoomInkSurface : IInkSurface
{
    /// <summary>缩放系数（1.0 = 内容原始尺寸）</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>内容坐标下的可视区域（用于绘制裁剪；宿主在滚动时更新，可显著减少重绘）</summary>
    public Rect? Visible { get; set; }

    public Point ToContent(Point canvasPoint)
    {
        double z = Zoom <= 0 ? 1.0 : Zoom;
        return new Point(canvasPoint.X / z, canvasPoint.Y / z);
    }

    public Point FromContent(Point contentPoint)
        => new(contentPoint.X * Zoom, contentPoint.Y * Zoom);

    public Rect? VisibleContentRect => Visible;
}

/// <summary>
/// 墨迹文档：一组笔画 + 撤销/重做栈。与 UI 无关，可被宿主的多页（如白板分页、PDF 多页）各持一份。
/// </summary>
public sealed class InkDocument
{
    private readonly List<InkStroke> _strokes = new();
    private readonly Stack<InkStroke> _redo = new();

    /// <summary>撤销栈深度上限（防止长时间书写吃内存；超出后丢弃最早的记录）</summary>
    private const int MaxUndoDepth = 200;
    private readonly LinkedList<InkStroke> _undoOrder = new();

    public IReadOnlyList<InkStroke> Strokes => _strokes;

    public bool HasStrokes => _strokes.Count > 0;

    public event Action? Changed;

    /// <summary>内容发生任何变化（新增/擦除/撤销/重做/清空）后触发</summary>
    private void RaiseChanged() => Changed?.Invoke();

    /// <summary>提交一条笔画（落笔完成时调用）</summary>
    public void Add(InkStroke stroke)
    {
        if (stroke == null || stroke.Points.Count == 0) return;
        _strokes.Add(stroke);

        // 新的书写动作作废重做链（与所有编辑器一致）
        _redo.Clear();

        _undoOrder.AddLast(stroke);
        while (_undoOrder.Count > MaxUndoDepth)
        {
            var oldest = _undoOrder.First!.Value;
            _undoOrder.RemoveFirst();
            _strokes.Remove(oldest);
        }

        RaiseChanged();
    }

    /// <summary>撤销最后一条笔画（返回是否真的撤销了）</summary>
    public bool Undo()
    {
        if (_undoOrder.Count == 0) return false;
        var last = _undoOrder.Last!.Value;
        _undoOrder.RemoveLast();
        if (_strokes.Remove(last))
        {
            _redo.Push(last);
            RaiseChanged();
            return true;
        }
        return false;
    }

    /// <summary>重做（返回是否真的重做了）</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var s = _redo.Pop();
        _strokes.Add(s);
        _undoOrder.AddLast(s);
        RaiseChanged();
        return true;
    }

    public bool CanUndo => _undoOrder.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>清空全部笔画（可被撤销？—— 不，清空是破坏性操作，由宿主决定是否先询问）</summary>
    public void Clear()
    {
        if (_strokes.Count == 0) return;
        _strokes.Clear();
        _undoOrder.Clear();
        _redo.Clear();
        RaiseChanged();
    }

    /// <summary>擦除命中某个点附近的笔画（返回擦掉的笔画数）。橡皮按"整笔删除"，符合板书直觉</summary>
    public int EraseAt(Point contentPoint, double radius)
    {
        if (_strokes.Count == 0) return 0;

        var hit = _strokes.Where(s => StrokeHit(s, contentPoint, radius)).ToList();
        if (hit.Count == 0) return 0;

        foreach (var s in hit)
        {
            _strokes.Remove(s);
            _undoOrder.Remove(s);
        }
        _redo.Clear();
        RaiseChanged();
        return hit.Count;
    }

    /// <summary>点到线段的距离命中测试（与绘制同一条数学路径，无需位图）</summary>
    private static bool StrokeHit(InkStroke stroke, Point p, double radius)
    {
        var b = stroke.Bounds;
        // 包围盒外扩 radius 快筛（绝大多数笔画在这里被排除）
        if (!b.Inflate(radius).Contains(p)) return false;

        double r2 = radius * radius;
        // 单点笔画（点击一下）：按点距离判定
        if (stroke.Points.Count == 1)
            return DistanceSquared(stroke.Points[0], p) <= r2;

        for (int i = 0; i < stroke.Points.Count - 1; i++)
        {
            if (DistanceToSegmentSquared(p, stroke.Points[i], stroke.Points[i + 1]) <= r2)
                return true;
        }
        return false;
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double DistanceToSegmentSquared(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double len2 = vx * vx + vy * vy;
        if (len2 < 1e-9) return DistanceSquared(p, a);
        double t = ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
        t = Math.Clamp(t, 0, 1);
        double px = a.X + t * vx, py = a.Y + t * vy;
        double dx = p.X - px, dy = p.Y - py;
        return dx * dx + dy * dy;
    }
}

/// <summary>
/// 墨迹几何工具：把一条笔画转成 Avalonia 几何（供宿主绘制）。
/// 抽出来是为了让"白板导出 PNG"和"屏幕实时绘制"用同一份路径数据，避免两处不一致。
/// </summary>
public static class InkGeometry
{
    /// <summary>把内容坐标的笔画转成画布局部坐标的几何</summary>
    public static Geometry BuildGeometry(InkStroke stroke, IInkSurface surface)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (stroke.Points.Count == 1)
            {
                // 单点：画一个极短线段，配合 RoundLineCap 呈现成圆点
                var p = surface.FromContent(stroke.Points[0]);
                ctx.BeginFigure(p, false);
                ctx.LineTo(new Point(p.X + 0.01, p.Y));
                ctx.EndFigure(false);
            }
            else
            {
                ctx.BeginFigure(surface.FromContent(stroke.Points[0]), false);
                for (int i = 1; i < stroke.Points.Count; i++)
                    ctx.LineTo(surface.FromContent(stroke.Points[i]));
                ctx.EndFigure(false);
            }
        }
        return geo;
    }

    /// <summary>把若干笔画合成一个 GeometryGroup（导出/整层重绘用）</summary>
    public static GeometryGroup BuildGroup(IEnumerable<InkStroke> strokes, IInkSurface surface)
    {
        var g = new GeometryGroup();
        foreach (var s in strokes)
            g.Children.Add(BuildGeometry(s, surface));
        return g;
    }

    /// <summary>笔画应使用的画笔（荧光笔半透明 + 圆头圆角）</summary>
    public static Pen BuildPen(InkStroke stroke)
    {
        var color = stroke.IsHighlighter
            ? Color.FromArgb(0x66, stroke.Color.R, stroke.Color.G, stroke.Color.B)
            : stroke.Color;
        return new Pen(new SolidColorBrush(color), stroke.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }
}
