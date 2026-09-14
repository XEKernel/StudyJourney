using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>
/// 墨迹画布控件：落在宿主上的"可书写透明层"。
///
/// 职责边界（刻意保持窄）：
///   · 它只管"把指针事件变成笔画、把笔画画出来"
///   · 坐标变换交给 <see cref="IInkSurface"/>（宿主注入）
///   · 页面切换/文档切换由宿主换 <see cref="Document"/> 实例完成
///   · 滚动/缩放手势**不拦截**（Touch 默认不书写 → 事件冒泡给宿主的 ScrollViewer/手势识别）
///
/// 绘制分层（性能关键）：
///   历史笔画 -> 一次性画进缓存的 DrawingGroup（笔画数变化时才重建）
///   活动笔画 -> 每次指针移动追加点，只重绘这一条
///   激光笔   -> 独立于文档，不落盘，超时自动消失
/// </summary>
public sealed class InkCanvas : Control
{
    // ── 可配置属性 ───────────────────────────────────────────

    private InkDocument _document = new();
    /// <summary>当前墨迹文档（切换页面/文件时替换即可，控件自动重绘）</summary>
    public InkDocument Document
    {
        get => _document;
        set
        {
            if (_document == value) return;
            _document.Changed -= OnDocumentChanged;
            _document = value ?? new InkDocument();
            _document.Changed += OnDocumentChanged;
            RebuildHistory();
            InvalidateVisual();
        }
    }

    private IInkSurface _surface = IdentityInkSurface.Instance;
    /// <summary>坐标变换（宿主注入；PDF 阅读器传带滚动/缩放的实现）</summary>
    public IInkSurface Surface
    {
        get => _surface;
        set
        {
            _surface = value ?? IdentityInkSurface.Instance;
            RebuildHistory();
            InvalidateVisual();
        }
    }

    private InkTool _tool = InkTool.Pen;
    public InkTool Tool
    {
        get => _tool;
        set { _tool = value; UpdateCursor(); }
    }

    private Color _color = Color.FromRgb(0x2B, 0x6C, 0xB0);   // 默认校园蓝
    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    private double _thickness = 3;
    public double Thickness
    {
        get => _thickness;
        set => _thickness = Math.Clamp(value, 1, 60);
    }

    /// <summary>橡皮半径（内容坐标单位）</summary>
    public double EraserRadius { get; set; } = 12;

    /// <summary>是否允许手指（Touch）书写。默认 false —— 手指留给滚动/平移，符合 2.1/2.3 的触屏约定。
    /// 白板等"没有滚动"的场景可打开。</summary>
    public bool AllowTouchInk { get; set; }

    /// <summary>是否允许鼠标书写（白板总是要；屏幕批注在"只读观察"模式下可能要关）</summary>
    public bool AllowMouseInk { get; set; } = true;

    /// <summary>只读模式：完全不吃指针事件（用于 PDF 阅读时想滚动不想误画）</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>笔画落笔完成（供宿主标记"未保存"）</summary>
    public event Action? StrokeCommitted;

    // ── 内部状态 ─────────────────────────────────────────────

    private InkStroke? _active;                 // 正在书写的笔画（内容坐标）
    private bool _sessionActive;                // 本次指针会话是否已开始（用于 Touch 分流决策）
    private bool _sessionIsInk;                 // 本次会话是否被判定为"书写"（否则完全忽略）

    private List<(Geometry Geo, IPen Pen)>? _historyCache;   // 历史笔画的绘制缓存
    private int _historyCacheCount = -1;                     // 缓存对应的笔画数（变了就重建）

    private readonly List<InkStroke> _laserStrokes = new();
    private readonly DispatcherTimer _laserTimer;
    private DateTime _lastLaserAt = DateTime.MinValue;

    private const int LaserHoldMs = 1600;       // 激光笔轨迹保留时长

    public InkCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        _laserTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _laserTimer.Tick += (_, _) =>
        {
            if (_laserStrokes.Count == 0) return;
            if ((DateTime.Now - _lastLaserAt).TotalMilliseconds < LaserHoldMs) return;
            _laserStrokes.Clear();
            InvalidateVisual();
        };
    }

    private void OnDocumentChanged()
    {
        if (_document.Strokes.Count != _historyCacheCount)
        {
            RebuildHistory();
            InvalidateVisual();
        }
        else
        {
            // 撤销/重做后条数可能相同但内容不同（极少见）——保守重建
            RebuildHistory();
            InvalidateVisual();
        }
    }

    /// <summary>重建历史笔画缓存（笔画集合变化时调用）</summary>
    public void RebuildHistory()
    {
        var list = new List<(Geometry, IPen)>(_document.Strokes.Count);
        foreach (var s in _document.Strokes)
            list.Add((InkGeometry.BuildGeometry(s, _surface), InkGeometry.BuildPen(s)));
        _historyCache = list;
        _historyCacheCount = _document.Strokes.Count;
    }

    /// <summary>兼容旧名（宿主可能调用）</summary>
    public void InvalidateInk() { RebuildHistory(); InvalidateVisual(); }

    // ── 指针事件 ─────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsReadOnly) return;

        var props = e.GetCurrentPoint(this).Properties;
        var type = e.Pointer.Type;

        // 分流：什么该被当作"书写"
        bool wanted = type switch
        {
            PointerType.Pen => true,               // 触控笔始终书写（笔尖是书写意图的最强信号）
            PointerType.Touch => AllowTouchInk,    // 手指默认留给滚动
            _ => AllowMouseInk && props.IsLeftButtonPressed,
        };
        if (!wanted) return;

        if (_tool is InkTool.None) return;

        _sessionActive = true;
        _sessionIsInk = true;

        var content = _surface.ToContent(e.GetPosition(this));

        if (_tool == InkTool.Laser)
        {
            _laserStrokes.Clear();
            _laserStrokes.Add(new InkStroke { Color = Color, Thickness = Thickness + 4, Tool = InkTool.Laser });
            _laserStrokes[0].Points.Add(content);
            _lastLaserAt = DateTime.Now;
            _laserTimer.Start();
            InvalidateVisual();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (_tool == InkTool.Eraser)
        {
            _document.EraseAt(content, EraserRadius);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _active = new InkStroke { Color = Color, Thickness = Thickness, Tool = _tool };
        _active.Points.Add(content);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_sessionActive || !_sessionIsInk) return;

        var content = _surface.ToContent(e.GetPosition(this));

        if (_tool == InkTool.Laser)
        {
            if (_laserStrokes.Count == 0) return;
            _laserStrokes[0].Points.Add(content);
            ClampPoints(_laserStrokes[0].Points);
            _lastLaserAt = DateTime.Now;
            InvalidateVisual();
            return;
        }

        if (_tool == InkTool.Eraser)
        {
            _document.EraseAt(content, EraserRadius);
            return;
        }

        if (_active == null) return;

        // 采样过滤：太近的点丢掉（既省内存也让线条更顺；1.5px 是手感与体积的平衡点）
        var last = _active.Points[^1];
        double dx = content.X - last.X, dy = content.Y - last.Y;
        if (dx * dx + dy * dy < 2.25) return;

        _active.Points.Add(content);
        ClampPoints(_active.Points);
        InvalidateVisual();     // 活动笔画增量重绘（历史笔画走缓存，不重建）
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_sessionActive) return;
        _sessionActive = false;
        _sessionIsInk = false;

        if (_tool == InkTool.Laser)
        {
            _lastLaserAt = DateTime.Now;
            return;
        }

        if (_active != null)
        {
            var s = _active;
            _active = null;
            if (s.Points.Count > 0 && _tool != InkTool.Eraser)
            {
                _document.Add(s);       // Add 内部会触发 Changed → 重建缓存
                StrokeCommitted?.Invoke();
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // 捕获丢失（窗口失焦等）：把活动笔画提交，避免"画到一半消失"
        _sessionActive = false;
        _sessionIsInk = false;
        if (_active != null)
        {
            var s = _active;
            _active = null;
            if (s.Points.Count > 0) _document.Add(s);
        }
    }

    /// <summary>点数上限：单笔过长时丢掉中间点（防止一笔画满整个屏幕吃爆内存）</summary>
    private const int MaxPointsPerStroke = 4000;
    private static void ClampPoints(List<Point> pts)
    {
        if (pts.Count <= MaxPointsPerStroke) return;
        // 隔点抽稀（保留首尾），写入后点数回落
        for (int i = pts.Count - 2; i > 0 && pts.Count > MaxPointsPerStroke; i -= 2)
            pts.RemoveAt(i);
    }

    // ── 绘制 ─────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Avalonia 的 Control 没有 Background 属性，命中测试依赖是否"画了东西"。
        // 这里先铺一层全透明矩形，保证整块区域都能收到指针事件（否则空白处点不动）。
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var visible = _surface.VisibleContentRect;

        // 历史笔画（走缓存；有可视裁剪需求时按笔画逐个做包围盒剔除）
        if (visible is { } vis)
        {
            foreach (var s in _document.Strokes)
            {
                if (!vis.Intersects(s.Bounds)) continue;
                context.DrawGeometry(null, InkGeometry.BuildPen(s), InkGeometry.BuildGeometry(s, _surface));
            }
        }
        else if (_historyCache != null)
        {
            foreach (var (geo, pen) in _historyCache)
                context.DrawGeometry(null, pen, geo);
        }

        // 活动笔画（单条，实时跟随）
        if (_active != null)
            context.DrawGeometry(null, InkGeometry.BuildPen(_active), InkGeometry.BuildGeometry(_active, _surface));

        // 激光笔轨迹
        foreach (var l in _laserStrokes)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, l.Color.R, l.Color.G, l.Color.B)),
                l.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, InkGeometry.BuildGeometry(l, _surface));
        }
    }

    private void UpdateCursor()
    {
        Cursor = _tool switch
        {
            InkTool.Eraser => new Cursor(StandardCursorType.Cross),
            InkTool.Pen or InkTool.Highlighter or InkTool.Laser => new Cursor(StandardCursorType.Cross),
            _ => Cursor.Default,
        };
    }
}
