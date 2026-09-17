using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;
using StudyJourney.Avalonia.Models;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 内置 PDF 阅读器（PLANNING 2.1）：打开 PDF → 连续滚动阅读 → 在页面上批注。
///
/// 关键设计（对齐 2.1 的确认项）：
///   · **墨迹挂在文档内容坐标系**：画布（InkCanvas）按 zoom 放大后铺在**未缩放的文档空间**之上，
///     并且整体放在 ScrollViewer 的内容里 → 滚动时画布与页面一起滚，笔迹天然跟随内容；
///     缩放时改 `ZoomInkSurface.Zoom` + 重算几何 → 笔迹跟着内容一起放大，绝不留在屏幕原位。
///   · **触屏滚动**：`AllowTouchInk = false`（手指留给滚动，交给 ScrollViewer 的触摸拖动），
///     触控笔/鼠标左键才书写 —— 沿用 2.1 的"笔写=墨迹 / 指滑=滚动"约定。
///   · **大文件懒渲染**：只渲染可视页 ±1 缓冲页，位图带缓存并淘汰远离视口的页；
///     真正的 PDFium 渲染放后台线程（PDFium 非线程安全 → 由 PdfRenderer 内部串行化）。
///   · **续读**：打开时读 pdf-state.json 里的上次页码，翻页/滚动时回写。
///
/// UI 约定：工具栏放**屏幕底部**（老师在大屏前够不到顶部）；按钮"图标 + 中文"；
/// 触屏热区 ≥44px；直角 + 校园蓝（跨项目统一视觉）。
/// </summary>
public sealed class PdfReaderWindow : Window
{
    // ── 布局常量 ─────────────────────────────────────────────
    private const double BtnHeight = 44;      // 触屏热区下限
    private const double PageGap = 16;        // 页间距（内容 DIP 空间）
    private const double ContentPadding = 12; // 内容四周留白（内容 DIP 空间）

    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;

    private enum ZoomMode { Custom, FitWidth, FitPage }

    // ── 状态 ─────────────────────────────────────────────────
    private PdfRenderer? _pdf;
    private string? _pdfPath;
    private double _zoom = 1.0;
    private ZoomMode _zoomMode = ZoomMode.FitWidth;
    private int _currentPage;
    private bool _closing;
    private bool _typingInPageBox;
    private bool _dirtyInk;

    // 内容空间（zoom = 1，单位 DIP）：每页矩形 + 整体尺寸
    private readonly List<Rect> _pageRects = new();
    private double _contentWidth;
    private double _contentHeight;

    private readonly ZoomInkSurface _surface = new();
    private readonly Dictionary<int, WriteableBitmap> _bitmapCache = new();
    private readonly HashSet<int> _pending = new();
    private int _renderGeneration;

    // ── 控件 ─────────────────────────────────────────────────
    private readonly ScrollViewer _scroll;
    private readonly Canvas _pageLayer;
    private readonly InkCanvas _ink;
    private readonly Grid _contentHost;
    private readonly List<Border> _pageBoxes = new();
    private readonly List<Image> _pageImages = new();
    private readonly TextBlock _pageLabel;
    private readonly TextBox _pageBox;
    private readonly TextBlock _zoomLabel;
    private readonly List<Button> _toolButtons = new();
    private readonly Button _undoBtn;
    private readonly Button _redoBtn;
    private readonly Button _openBtn;

    public PdfReaderWindow()
    {
        Title = "PDF 阅读 · 学程";
        Width = 1280;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        // 画布：手指滚动、笔/鼠标书写
        _ink = new InkCanvas
        {
            AllowMouseInk = true,
            AllowTouchInk = false,        // ⚠ 手指必须留给 ScrollViewer，否则触屏没法滚动
            EraserRadius = 14,
            Thickness = 3,
            Color = Color.FromRgb(0xD1, 0x3A, 0x3A),   // 批注默认红笔（与板书区分）
            Tool = InkTool.Pen,
            Surface = _surface,
            IsReadOnly = true,            // 未打开文件时不接收指针
        };
        _ink.Document = new InkDocument();
        _ink.Document.Changed += () => { UpdateUndoButtons(); };
        _ink.StrokeCommitted += () => { _dirtyInk = true; UpdateUndoButtons(); };

        _pageLayer = new Canvas();

        _contentHost = new Grid { ClipToBounds = false };
        _contentHost.Children.Add(_pageLayer);
        _contentHost.Children.Add(_ink);          // 墨迹层在页面之上

        _scroll = new ScrollViewer
        {
            Content = _contentHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // 触屏拖动滚动（Avalonia 内置手势识别；InkCanvas 不吞 Touch 事件，能冒泡到这里）
        };
        _scroll.ScrollChanged += (_, _) => OnScrollChanged();
        _scroll.SizeChanged += (_, _) => ApplyFitIfNeeded();

        _pageLabel = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(8, 0),
        };

        _pageBox = new TextBox
        {
            Width = 64,
            Height = 34,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            PlaceholderText = "页码",
        };
        _pageBox.GotFocus += (_, _) => _typingInPageBox = true;
        _pageBox.LostFocus += (_, _) => _typingInPageBox = false;
        _pageBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { JumpToTypedPage(); e.Handled = true; }
        };

        _zoomLabel = new TextBlock
        {
            FontSize = 14,
            MinWidth = 52,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        };

        _undoBtn = MakeButton("↶", "撤销", "撤销上一笔批注 (Ctrl+Z)", () => InkDoc.Undo());
        _redoBtn = MakeButton("↷", "重做", "重做 (Ctrl+Y)", () => InkDoc.Redo());
        _openBtn = MakeButton("📂", "打开 PDF", "选择要打开的 PDF 文件", Open_Click);

        var toolbar = BuildToolbar();
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(_scroll, 0);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(_scroll);
        root.Children.Add(toolbar);
        Content = root;

        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Opened += (_, _) => { UpdateZoomLabel(); UpdatePageLabel(); UpdateUndoButtons(); SetInkEnabled(false); };
    }

    private InkDocument InkDoc => _ink.Document;

    /// <summary>是否有未导出的批注（自动更新重启前用它判断"现在重启会不会丢批注"）</summary>
    public bool HasUnsavedInk => _dirtyInk && InkDoc.HasStrokes;

    // ── 工具栏（底部两行：上行阅读、下行批注）──────────────────

    private Control BuildToolbar()
    {
        var wrap = new StackPanel { Orientation = Orientation.Vertical };

        // 第一行：文件 / 翻页 / 缩放 / 退出
        var readRow = NewRow();
        readRow.Children.Add(_openBtn);
        readRow.Children.Add(MakeSeparator());
        readRow.Children.Add(MakeButton("◀", "上一页", "上一页 (PgUp)", () => GoToPage(_currentPage - 1)));
        readRow.Children.Add(_pageBox);
        readRow.Children.Add(MakeButton("", "跳转", "跳到上面填写的页码（也可直接按回车）", JumpToTypedPage));
        readRow.Children.Add(_pageLabel);
        readRow.Children.Add(MakeButton("▶", "下一页", "下一页 (PgDn)", () => GoToPage(_currentPage + 1)));
        readRow.Children.Add(MakeSeparator());
        readRow.Children.Add(MakeButton("－", "缩小", "缩小 (Ctrl+-)", () => ZoomBy(1 / 1.2)));
        readRow.Children.Add(_zoomLabel);
        readRow.Children.Add(MakeButton("＋", "放大", "放大 (Ctrl++)", () => ZoomBy(1.2)));
        readRow.Children.Add(MakeButton("↔", "适应宽度", "页面宽度铺满窗口（推荐：横向不用拖）", () => SetZoomMode(ZoomMode.FitWidth)));
        readRow.Children.Add(MakeButton("▭", "整页", "一页完整显示在窗口里", () => SetZoomMode(ZoomMode.FitPage)));
        readRow.Children.Add(MakeButton("", "100%", "原始尺寸（100%，1:1）", () => SetZoomMode(ZoomMode.Custom, 1.0)));
        readRow.Children.Add(MakeSeparator());
        readRow.Children.Add(MakeButton("✕", "退出", "关闭阅读器 (Esc)", Close));

        // 第二行：批注
        var inkRow = NewRow();
        inkRow.Children.Add(MakeLabel("批注"));
        inkRow.Children.Add(MakeToolButton("✏", "画笔", "画笔：正常粗细的实线 (P)", InkTool.Pen));
        inkRow.Children.Add(MakeToolButton("🖍", "荧光笔", "荧光笔：半透明粗线，适合划重点 (H)", InkTool.Highlighter));
        inkRow.Children.Add(MakeToolButton("◻", "橡皮", "橡皮：按整笔擦除 (E)", InkTool.Eraser));
        inkRow.Children.Add(MakeToolButton("●", "激光笔", "激光笔：只做指示，约 1.6 秒自动消失 (L)", InkTool.Laser));
        inkRow.Children.Add(MakeSeparator());
        inkRow.Children.Add(MakeLabel("颜色"));
        inkRow.Children.Add(BuildPalette());
        inkRow.Children.Add(MakeSeparator());
        inkRow.Children.Add(_undoBtn);
        inkRow.Children.Add(_redoBtn);
        inkRow.Children.Add(MakeButton("🗑", "清空", "清除全部批注", ClearInk_Click));
        inkRow.Children.Add(MakeSeparator());
        inkRow.Children.Add(MakeButton("💾", "导出本页", "把当前页（含批注）导出为 PNG", Export_Click));

        wrap.Children.Add(Wrap(readRow));
        wrap.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
        });
        wrap.Children.Add(Wrap(inkRow));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(0),
            Child = wrap,
        };
    }

    private static StackPanel NewRow() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Wrap(StackPanel row) => new ScrollViewer
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = row,
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private static Control MakeSeparator() => new Border
    {
        Width = 1,
        Height = 26,
        Margin = new Thickness(4, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
    };

    private Button MakeButton(string glyph, string text, string tip, Action onClick)
    {
        var b = BuildButtonContent(glyph, text, tip);
        b.Click += (_, _) => SafeRun(onClick, text);
        return b;
    }

    private Button MakeToolButton(string glyph, string text, string tip, InkTool tool)
    {
        var b = BuildButtonContent(glyph, text, tip);
        b.Tag = tool;
        b.Click += (_, _) => { _ink.Tool = tool; UpdateToolVisuals(); };
        _toolButtons.Add(b);
        return b;
    }

    private static Button BuildButtonContent(string glyph, string text, string tip)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(glyph))
            content.Children.Add(new TextBlock { Text = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(text))
            content.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

        var b = new Button
        {
            Content = content,
            Height = BtnHeight,
            MinWidth = BtnHeight,
            Padding = new Thickness(12, 0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>所有按钮动作统一包一层：出错记日志 + 提示，绝不让异常冒出去把进程打崩。</summary>
    private void SafeRun(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PDF 阅读器[{what}]失败", ex);
            _ = DialogHelper.ShowMessageAsync(this, "PDF 阅读", $"{what}失败：{ex.Message}");
        }
    }

    private Control BuildPalette()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (name, color) in new[]
        {
            ("红", Color.FromRgb(0xD1, 0x3A, 0x3A)),
            ("蓝", Color.FromRgb(0x2B, 0x6C, 0xB0)),
            ("绿", Color.FromRgb(0x2F, 0x8F, 0x4F)),
            ("黄", Color.FromRgb(0xE0, 0xB4, 0x1E)),
        })
        {
            var sw = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            var captured = color;
            ToolTip.SetTip(sw, $"{name}（批注颜色）");
            sw.Click += (_, _) => _ink.Color = captured;
            panel.Children.Add(sw);
        }
        return panel;
    }

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

    private void UpdateToolVisuals()
    {
        foreach (var b in _toolButtons)
            b.Background = b.Tag is InkTool t && t == _ink.Tool ? ActiveBrush : IdleBrush;
    }

    private void UpdateUndoButtons()
    {
        _undoBtn.IsEnabled = InkDoc.CanUndo;
        _redoBtn.IsEnabled = InkDoc.CanRedo;
    }

    private void UpdateZoomLabel() => _zoomLabel.Text = $"{_zoom * 100:0}%";

    private void UpdatePageLabel()
    {
        int total = _pdf?.PageCount ?? 0;
        _pageLabel.Text = total > 0 ? $"/ {total} 页" : "未打开文件";
    }

    private void SetInkEnabled(bool enabled)
    {
        _ink.IsReadOnly = !enabled;
    }

    // ── 打开文件 ─────────────────────────────────────────────

    private async void Open_Click()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "打开 PDF",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PDF 文档") { Patterns = new[] { "*.pdf" }, MimeTypes = new[] { "application/pdf" } },
                },
            });
            var file = files?.FirstOrDefault();
            if (file == null) return;
            await OpenFileAsync(file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error("选择 PDF 失败", ex);
            await DialogHelper.ShowMessageAsync(this, "PDF 阅读", $"打开失败：{ex.Message}");
        }
    }

    /// <summary>打开指定 PDF（也供外部调用，例如自动化"打开课件"走内置阅读器）</summary>
    public async Task OpenFileAsync(string path)
    {
        try
        {
            var renderer = new PdfRenderer(path);      // 解析失败会抛，下面统一提示
            _pdf?.Dispose();
            _pdf = renderer;
            _pdfPath = path;

            _dirtyInk = false;
            InkDoc.Clear();
            _bitmapCache.Clear();
            _pending.Clear();
            _renderGeneration++;
            _currentPage = 0;

            LayoutContent();
            BuildPageBoxes();
            SetInkEnabled(true);
            UpdatePageLabel();
            UpdateUndoButtons();

            // 续读：上次看到第几页
            int last = PdfReadingState.GetLastPage(path);
            if (last > 0 && last < _pdf.PageCount)
            {
                // 等布局完成再定位，避免 Offset 被后面的一次布局重置
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                GoToPage(last, scrollToTop: true);
            }
            else
            {
                ApplyFitIfNeeded();
                ApplyZoom();
                GoToPage(0, scrollToTop: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"打开 PDF 失败: {path}", ex);
            await DialogHelper.ShowMessageAsync(this, "PDF 阅读", $"打开失败：\n{ex.Message}");
        }
    }

    // ── 布局（内容空间 = zoom 1）─────────────────────────────

    private void LayoutContent()
    {
        _pageRects.Clear();
        if (_pdf == null) { _contentWidth = 0; _contentHeight = 0; return; }

        double maxW = 0, y = ContentPadding;
        var tmp = new List<Rect>(_pdf.PageCount);
        for (int i = 0; i < _pdf.PageCount; i++)
        {
            var pt = _pdf.PageSizesPt[i];
            double w = pt.Width * PdfRenderer.PtToDip;
            double h = pt.Height * PdfRenderer.PtToDip;
            if (w <= 0 || h <= 0) { w = Math.Max(w, 1); h = Math.Max(h, 1); }
            maxW = Math.Max(maxW, w);
            tmp.Add(new Rect(0, y, w, h));
            y += h + PageGap;
        }

        _contentWidth = maxW + ContentPadding * 2;
        _contentHeight = Math.Max(y - PageGap + ContentPadding, ContentPadding * 2);

        // 水平居中
        foreach (var r in tmp)
            _pageRects.Add(new Rect((_contentWidth - r.Width) / 2, r.Y, r.Width, r.Height));
    }

    private void BuildPageBoxes()
    {
        _pageLayer.Children.Clear();
        _pageBoxes.Clear();
        _pageImages.Clear();
        if (_pdf == null) return;

        for (int i = 0; i < _pdf.PageCount; i++)
        {
            var img = new Image { Stretch = Stretch.Fill };
            var box = new Border
            {
                Background = Brushes.White,          // 页面底：PDF 透明区域显白
                BorderThickness = new Thickness(0),
                ClipToBounds = true,
                Child = img,
            };
            _pageBoxes.Add(box);
            _pageImages.Add(img);
            _pageLayer.Children.Add(box);
        }
    }

    /// <summary>把内容空间的矩形按当前 zoom 应用到实际控件上</summary>
    private void ApplyZoom()
    {
        if (_pdf == null) return;

        _contentHost.Width = _contentWidth * _zoom;
        _contentHost.Height = _contentHeight * _zoom;

        for (int i = 0; i < _pageBoxes.Count && i < _pageRects.Count; i++)
        {
            var r = _pageRects[i];
            var box = _pageBoxes[i];
            Canvas.SetLeft(box, r.X * _zoom);
            Canvas.SetTop(box, r.Y * _zoom);
            box.Width = r.Width * _zoom;
            box.Height = r.Height * _zoom;
        }

        // 墨迹层：同样是"内容空间 × zoom"，坐标系交给 ZoomInkSurface
        _surface.Zoom = _zoom;
        _ink.Width = _contentWidth * _zoom;
        _ink.Height = _contentHeight * _zoom;
        _ink.InvalidateInk();      // 按新 zoom 重建历史几何（笔迹内容坐标不变 → 自动跟随缩放）

        // 缩放变了 → 已渲染位图的分辨率不再匹配，全部丢弃重渲
        _bitmapCache.Clear();
        foreach (var img in _pageImages) img.Source = null;
        _renderGeneration++;
        _pending.Clear();

        UpdateZoomLabel();
        RequestVisibleRenders();
    }

    private void ZoomBy(double factor) => SetZoomMode(ZoomMode.Custom, _zoom * factor);

    private void SetZoomMode(ZoomMode mode, double? customZoom = null)
    {
        if (_pdf == null) return;
        _zoomMode = mode;
        if (customZoom is { } z) _zoom = Math.Clamp(z, MinZoom, MaxZoom);
        ApplyFitIfNeeded();   // FitWidth / FitPage 在这里按视口算出 zoom（Custom 不会被它改）
        ApplyZoom();          // 统一按当前 _zoom 重新布局 + 丢弃旧位图
    }

    /// <summary>FitWidth / FitPage 模式下按视口尺寸重算 zoom；结果没变就不折腾</summary>
    private void ApplyFitIfNeeded()
    {
        if (_pdf == null || _zoomMode == ZoomMode.Custom) return;
        double before = _zoom;
        ComputeFitZoom();
        if (Math.Abs(before - _zoom) > 0.0005) ApplyZoom();
    }

    private void ComputeFitZoom()
    {
        if (_pdf == null) return;
        double vw = _scroll.Viewport.Width;
        double vh = _scroll.Viewport.Height;
        if (vw <= 1 || vh <= 1) return;     // 还没布局完，等 SizeChanged 再来

        double byWidth = _contentWidth > 1 ? vw / _contentWidth : 1.0;
        double z = _zoomMode switch
        {
            ZoomMode.FitWidth => byWidth,
            ZoomMode.FitPage => Math.Min(byWidth,
                _pageRects.Count > 0 && _pageRects.Max(r => r.Height) > 1
                    ? vh / _pageRects.Max(r => r.Height)
                    : byWidth),
            _ => _zoom,
        };
        _zoom = Math.Clamp(z, MinZoom, MaxZoom);
    }

    // ── 懒渲染 ───────────────────────────────────────────────

    private void OnScrollChanged()
    {
        if (_pdf == null) return;
        RequestVisibleRenders();
    }

    private (int From, int To, int FirstVisible) VisiblePageRange()
    {
        if (_pdf == null || _pageRects.Count == 0) return (0, 0, 0);

        double top = _scroll.Offset.Y / _zoom;
        double bottom = (_scroll.Offset.Y + _scroll.Viewport.Height) / _zoom;

        int firstVisible = -1, lastVisible = -1;
        for (int i = 0; i < _pageRects.Count; i++)
        {
            var r = _pageRects[i];
            if (r.Bottom >= top && r.Top <= bottom)
            {
                if (firstVisible < 0) firstVisible = i;
                lastVisible = i;
            }
        }
        if (firstVisible < 0) { firstVisible = Math.Clamp(_currentPage, 0, _pageRects.Count - 1); lastVisible = firstVisible; }

        int from = Math.Max(0, firstVisible - 1);
        int to = Math.Min(_pdf.PageCount - 1, lastVisible + 1);
        return (from, to, firstVisible);
    }

    private void RequestVisibleRenders()
    {
        if (_pdf == null) return;

        var (from, to, firstVisible) = VisiblePageRange();

        // 可视区域（内容坐标）交给 surface 做绘制裁剪，减少无效重绘
        double vTop = _scroll.Offset.Y / _zoom;
        double vH = _scroll.Viewport.Height / _zoom;
        _surface.Visible = new Rect(0, vTop, Math.Max(_contentWidth, 1), Math.Max(vH, 1));

        // 记录当前页（用于页码显示 + 续读）
        if (firstVisible != _currentPage)
        {
            _currentPage = firstVisible;
            UpdatePageLabel();
            _pageBox.Text = (_currentPage + 1).ToString();
            if (_pdfPath != null) PdfReadingState.SetLastPage(_pdfPath, _currentPage);
        }

        for (int i = from; i <= to; i++) EnsureRendered(i);
        EvictOutside(from - 2, to + 2);
    }

    private void EnsureRendered(int index)
    {
        if (_pdf == null) return;
        if (_bitmapCache.ContainsKey(index) || _pending.Contains(index)) return;

        var pdf = _pdf;
        int gen = _renderGeneration;
        double scale = PdfRenderer.PtToDip * _zoom;
        _pending.Add(index);

        // PDFium 渲染放后台线程（大页 20~80ms，放 UI 线程会明显掉帧）；
        // PdfRenderer 内部有全局锁，多页并发也会被串行化，安全。
        _ = Task.Run(() =>
        {
            try
            {
                var page = pdf.RenderPage(index, scale);
                Dispatcher.UIThread.Post(() =>
                {
                    _pending.Remove(index);
                    if (_closing || gen != _renderGeneration || !ReferenceEquals(pdf, _pdf)) return;
                    try
                    {
                        var wb = ToWriteableBitmap(page);
                        _bitmapCache[index] = wb;
                        if (index < _pageImages.Count) _pageImages[index].Source = wb;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"上传 PDF 第 {index + 1} 页位图失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _pending.Remove(index);
                    AppLogger.Warn($"渲染 PDF 第 {index + 1} 页失败: {ex.Message}");
                });
            }
        });
    }

    /// <summary>淘汰远离视口的位图，控制内存（一页 A4 @100% 约 3.5MB）</summary>
    private void EvictOutside(int keepFrom, int keepTo)
    {
        if (_bitmapCache.Count == 0) return;
        List<int>? drop = null;
        foreach (var kv in _bitmapCache)
        {
            if (kv.Key < keepFrom || kv.Key > keepTo)
                (drop ??= new List<int>()).Add(kv.Key);
        }
        if (drop == null) return;

        foreach (var i in drop)
        {
            _bitmapCache.Remove(i);
            if (i < _pageImages.Count) _pageImages[i].Source = null;
        }
    }

    private static WriteableBitmap ToWriteableBitmap(PdfPageBitmap page)
    {
        // AlphaFormat.Opaque：PDFium 输出里空白区域 alpha 可能为 0，
        // 用 Opaque 直接忽略 alpha，页面始终不透明（底色由外层白 Border 保证）
        var wb = new WriteableBitmap(
            new PixelSize(page.Width, page.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var fb = wb.Lock())
        {
            int rowBytes = page.Width * 4;
            for (int y = 0; y < page.Height; y++)
            {
                // 逐行拷贝：帧缓冲 stride 可能大于 rowBytes
                Marshal.Copy(page.Bgra, y * rowBytes, IntPtr.Add(fb.Address, y * fb.RowBytes), rowBytes);
            }
        }
        return wb;
    }

    // ── 翻页 / 跳转 ──────────────────────────────────────────

    private void GoToPage(int index, bool scrollToTop = true)
    {
        if (_pdf == null || _pageRects.Count == 0) return;
        index = Math.Clamp(index, 0, _pageRects.Count - 1);
        _currentPage = index;
        UpdatePageLabel();
        _pageBox.Text = (index + 1).ToString();
        if (_pdfPath != null) PdfReadingState.SetLastPage(_pdfPath, index);

        var r = _pageRects[index];
        double offsetY = r.Y * _zoom + (scrollToTop ? -8 : 0);
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, offsetY));

        RequestVisibleRenders();
    }

    private void JumpToTypedPage()
    {
        if (_pdf == null) return;
        if (int.TryParse(_pageBox.Text?.Trim(), out int oneBased))
            GoToPage(oneBased - 1);
        else
            _pageBox.Text = (_currentPage + 1).ToString();
        _ink.Focus();
    }

    // ── 批注 ─────────────────────────────────────────────────

    private void ClearInk_Click()
    {
        InkDoc.Clear();
        _dirtyInk = false;
        UpdateUndoButtons();
    }

    // ── 导出当前页（页面 + 批注）─────────────────────────────

    private async void Export_Click()
    {
        if (_pdf == null || _pdfPath == null)
        {
            await DialogHelper.ShowMessageAsync(this, "PDF 阅读", "请先打开一个 PDF 文件。");
            return;
        }

        try
        {
            int idx = Math.Clamp(_currentPage, 0, _pdf.PageCount - 1);
            var pageRect = _pageRects[idx];

            // 用当前缩放渲染；如果当前很小，至少放大到 2 倍，避免导出太糊
            double exportScale = Math.Max(PdfRenderer.PtToDip * _zoom, PdfRenderer.PtToDip * 2.0);
            var data = _pdf.RenderPage(idx, exportScale);

            using var bmp = new RenderTargetBitmap(new PixelSize(data.Width, data.Height), new Vector(96, 96));
            using (var ctx = bmp.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.White, new Rect(0, 0, data.Width, data.Height));

                using var pageBmp = ToWriteableBitmap(data);
                ctx.DrawImage(pageBmp, new Rect(0, 0, data.Width, data.Height));

                // 墨迹：内容坐标 → 该页内的导出像素坐标
                var exportSurface = new PageExportSurface
                {
                    Origin = pageRect.TopLeft,
                    Scale = pageRect.Width > 1 ? data.Width / pageRect.Width : 1.0,
                };
                var pageBounds = new Rect(pageRect.X - 40, pageRect.Y - 40, pageRect.Width + 80, pageRect.Height + 80);
                foreach (var s in InkDoc.Strokes)
                {
                    if (s.Tool == InkTool.Laser) continue;
                    if (!pageBounds.Intersects(s.Bounds)) continue;   // 别的页上的批注不带走
                    ctx.DrawGeometry(null, InkGeometry.BuildPen(s), InkGeometry.BuildGeometry(s, exportSurface));
                }
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出当前页",
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_pdfPath)}_第{idx + 1}页.png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } } },
            });
            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            bmp.Save(stream);
            await DialogHelper.ShowMessageAsync(this, "PDF 阅读", $"已导出：\n{file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("导出 PDF 页失败", ex);
            await DialogHelper.ShowMessageAsync(this, "PDF 阅读", $"导出失败：{ex.Message}");
        }
    }

    /// <summary>导出用坐标系：把内容坐标映射到"该页内部的导出像素坐标"</summary>
    private sealed class PageExportSurface : IInkSurface
    {
        public Point Origin { get; init; }
        public double Scale { get; init; } = 1.0;

        public Point ToContent(Point p) => new(p.X / Scale + Origin.X, p.Y / Scale + Origin.Y);
        public Point FromContent(Point c) => new((c.X - Origin.X) * Scale, (c.Y - Origin.Y) * Scale);
        public Rect? VisibleContentRect => null;
    }

    // ── 键盘 ─────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_typingInPageBox) return;   // 正在输页码，别抢按键

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Z when ctrl: InkDoc.Undo(); e.Handled = true; break;
            case Key.Y when ctrl: InkDoc.Redo(); e.Handled = true; break;
            case Key.OemPlus or Key.Add when ctrl: ZoomBy(1.2); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomBy(1 / 1.2); e.Handled = true; break;
            case Key.P when !ctrl: _ink.Tool = InkTool.Pen; UpdateToolVisuals(); e.Handled = true; break;
            case Key.H when !ctrl: _ink.Tool = InkTool.Highlighter; UpdateToolVisuals(); e.Handled = true; break;
            case Key.E when !ctrl: _ink.Tool = InkTool.Eraser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.L when !ctrl: _ink.Tool = InkTool.Laser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.PageUp: GoToPage(_currentPage - 1); e.Handled = true; break;
            case Key.PageDown: GoToPage(_currentPage + 1); e.Handled = true; break;
            case Key.Home when ctrl: GoToPage(0); e.Handled = true; break;
            case Key.End when ctrl: GoToPage(int.MaxValue); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_dirtyInk && InkDoc.HasStrokes)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                bool discard = await DialogHelper.ShowConfirmAsync(this, "PDF 阅读",
                    "当前还有未导出的批注，关闭后不会保存。\n\n确定要关闭吗？", "关闭", "留下");
                if (!discard) return;
                _dirtyInk = false;
                Close();
            });
            return;
        }

        _closing = true;
        if (_pdfPath != null) PdfReadingState.SetLastPage(_pdfPath, _currentPage);
        _pdf?.Dispose();
        _pdf = null;
    }
}
