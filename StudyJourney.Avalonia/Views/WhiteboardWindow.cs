using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StudyJourney.Avalonia.Helpers;

namespace StudyJourney.Avalonia.Views;

/// <summary>
/// 白板（PLANNING 2.4）：独立画布，老师板书、讲解推导。
///
/// 与屏幕批注的区别（PLANNING 2.3 已明确）：白板是**独立画布**，批注是**悬浮覆盖层**。
/// 两者共用 Helpers/InkLayer + Helpers/InkCanvas（PLANNING 2.6 步骤 3 的公共墨迹组件）。
///
/// 功能（对齐 2.4 需求）：
///   · 画笔 / 荧光笔 / 橡皮 / 激光笔，颜色与粗细可调
///   · 笔迹级撤销重做（Ctrl+Z / Ctrl+Y），一键清空（带确认内容大小提示）
///   · 多页白板：上一页 / 下一页 / 新增 / 删除，每页独立墨迹
///   · 背景可选：纯白 / 网格 / 横线 / 点阵 / 黑板
///   · 导出当前页 PNG（2x 分辨率，含背景）
///   · 触屏：手指默认书写（白板没有滚动，不存在手势冲突）；
///     触控笔同样书写；两指缩放不在本期范围（PLANNING 2.3「多屏/缩放后置」同理）
///
/// UI 约定（跨项目统一）：直角（CornerRadius 0）+ 校园蓝 #2B6CB0 + 深色主题 + 工具栏热区 ≥44px。
/// </summary>
public sealed class WhiteboardWindow : Window
{
    // ── 布局常量 ─────────────────────────────────────────────
    private const double ToolbarHeight = 52;
    private const double ToolButtonSize = 44;      // 触屏热区下限（PLANNING 2.0 UI 统一约定）
    private const double DefaultPenThickness = 3;
    private const double DefaultEraserRadius = 14;

    // 常用板书色板（校园蓝在首位；不进设置页，白板自己够用即可）
    private static readonly (string Name, Color Color)[] Palette =
    {
        ("墨黑", Color.FromRgb(0x1A, 0x1A, 0x1A)),
        ("校园蓝", Color.FromRgb(0x2B, 0x6C, 0xB0)),
        ("红", Color.FromRgb(0xD1, 0x3A, 0x3A)),
        ("绿", Color.FromRgb(0x2F, 0x8F, 0x4F)),
        ("橙", Color.FromRgb(0xD9, 0x7A, 0x1E)),
        ("紫", Color.FromRgb(0x7B, 0x4E, 0xA8)),
        ("白", Colors.White),
    };

    // ── 状态 ─────────────────────────────────────────────────
    private readonly List<InkDocument> _pages = new() { new InkDocument() };
    private int _pageIndex;
    private BoardBackground _background = BoardBackground.Blank;
    private bool _dirty;   // 是否有未导出的墨迹（关闭时提醒）

    // ── 控件 ─────────────────────────────────────────────────
    private readonly InkCanvas _ink;
    private readonly Border _boardBorder;
    private readonly TextBlock _pageLabel;
    private readonly StackPanel _palettePanel;
    private readonly StackPanel _thicknessPanel;
    private readonly StrokeThicknessPreview _thicknessPreview;
    private readonly List<Button> _toolButtons = new();
    private readonly List<Button> _bgButtons = new();

    public WhiteboardWindow()
    {
        Title = "白板 · 学程";
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));

        _ink = new InkCanvas
        {
            AllowMouseInk = true,
            AllowTouchInk = true,        // 白板无滚动 → 手指直接书写
            EraserRadius = DefaultEraserRadius,
            Thickness = DefaultPenThickness,
            Color = Palette[1].Color,
            Document = _pages[0],
        };
        _ink.StrokeCommitted += () => { _dirty = true; UpdateUndoButtons(); };
        _pages[0].Changed += UpdateUndoButtons;
        _ink.Tool = InkTool.Pen;

        _boardBorder = new Border
        {
            Background = new SolidColorBrush(BoardRenderer.BaseColor(_background)),
            CornerRadius = new CornerRadius(0),
            Child = _ink,
            Margin = new Thickness(0, 0, 0, 0),
        };
        _boardBorder.SizeChanged += (_, _) => UpdateBoardSize();

        _pageLabel = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(10, 0),
        };

        _thicknessPreview = new StrokeThicknessPreview(DefaultPenThickness, Palette[1].Color);
        _palettePanel = BuildPalette();
        _thicknessPanel = BuildThicknessPanel();

        var toolbar = BuildToolbar();
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_boardBorder, 1);
        root.Children.Add(toolbar);
        root.Children.Add(_boardBorder);
        Content = root;

        // 快捷键：撤销/重做/清屏/翻页
        KeyDown += OnKeyDown;

        Opened += (_, _) =>
        {
            UpdateBoardSize();
            UpdatePageLabel();
            UpdateToolVisuals();
            UpdateUndoButtons();
            _ink.Focus();
        };
        Closing += OnClosing;
    }

    // ── 工具栏 ───────────────────────────────────────────────

    private Control BuildToolbar()
    {
        var toolbar = new Border
        {
            Height = ToolbarHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0),
            CornerRadius = new CornerRadius(0),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 工具
        row.Children.Add(MakeToolButton("✏", "画笔 (P)", InkTool.Pen));
        row.Children.Add(MakeToolButton("🖍", "荧光笔 (H)", InkTool.Highlighter));
        row.Children.Add(MakeToolButton("◻", "橡皮 (E)", InkTool.Eraser));
        row.Children.Add(MakeToolButton("●", "激光笔 (L)", InkTool.Laser));
        row.Children.Add(MakeSeparator());

        // 撤销 / 重做 / 清空
        _undoBtn = MakeActionButton("↶", "撤销 (Ctrl+Z)", Undo_Click);
        _redoBtn = MakeActionButton("↷", "重做 (Ctrl+Y)", Redo_Click);
        row.Children.Add(_undoBtn);
        row.Children.Add(_redoBtn);
        row.Children.Add(MakeActionButton("🗑", "清空本页", Clear_Click));
        row.Children.Add(MakeSeparator());

        // 颜色 + 粗细
        row.Children.Add(_palettePanel);
        row.Children.Add(_thicknessPanel);
        row.Children.Add(MakeSeparator());

        // 背景
        row.Children.Add(MakeBgButton("白", BoardBackground.Blank, "纯白背景"));
        row.Children.Add(MakeBgButton("▦", BoardBackground.Grid, "网格背景"));
        row.Children.Add(MakeBgButton("☰", BoardBackground.Ruled, "横线背景"));
        row.Children.Add(MakeBgButton("∴", BoardBackground.Dots, "点阵背景"));
        row.Children.Add(MakeBgButton("黑", BoardBackground.Blackboard, "黑板背景"));
        row.Children.Add(MakeSeparator());

        // 分页
        row.Children.Add(MakeActionButton("◀", "上一页 (PgUp)", () => SwitchPage(_pageIndex - 1)));
        row.Children.Add(_pageLabel);
        row.Children.Add(MakeActionButton("▶", "下一页 (PgDn)", () => SwitchPage(_pageIndex + 1)));
        row.Children.Add(MakeActionButton("＋", "新增一页", AddPage));
        row.Children.Add(MakeActionButton("－", "删除本页", DeletePage));
        row.Children.Add(MakeSeparator());

        // 导出
        row.Children.Add(MakeActionButton("💾", "导出本页 PNG", Export_Click));

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = row,
        };
        toolbar.Child = scroll;
        return toolbar;
    }

    private Button _undoBtn = null!, _redoBtn = null!;

    private Button MakeToolButton(string glyph, string tip, InkTool tool)
    {
        var b = MakeBaseButton(glyph, tip);
        b.Click += (_, _) => { _ink.Tool = tool; UpdateToolVisuals(); };
        b.Tag = tool;
        _toolButtons.Add(b);
        return b;
    }

    private Button MakeActionButton(string glyph, string tip, Action onClick)
    {
        var b = MakeBaseButton(glyph, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button MakeBaseButton(string glyph, string tip)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center },
            Width = ToolButtonSize,
            Height = ToolButtonSize,
            Padding = new Thickness(0),
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

    private static Control MakeSeparator() => new Border
    {
        Width = 1,
        Height = 26,
        Margin = new Thickness(4, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
    };

    private StackPanel BuildPalette()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (name, color) in Palette)
        {
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };
            var captured = color;
            ToolTip.SetTip(swatch, name);
            swatch.Click += (_, _) =>
            {
                _ink.Color = captured;
                _thicknessPreview.SetColor(captured);
                UpdateToolVisuals();
            };
            panel.Children.Add(swatch);
        }
        return panel;
    }

    private StackPanel BuildThicknessPanel()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_thicknessPreview);
        foreach (double t in new[] { 2.0, 4.0, 7.0, 12.0 })
        {
            var b = MakeBaseButton(ThicknessGlyph(t), $"{t:0} px 粗");
            b.Width = 34;
            b.Height = 34;
            var captured = t;
            b.Click += (_, _) =>
            {
                _ink.Thickness = captured;
                _ink.EraserRadius = Math.Max(captured * 3.5, DefaultEraserRadius);
                _thicknessPreview.SetThickness(captured);
                UpdateToolVisuals();
            };
            panel.Children.Add(b);
        }
        return panel;
    }

    private static string ThicknessGlyph(double t) => t switch
    {
        <= 2.5 => "·",
        <= 5 => "•",
        <= 9 => "●",
        _ => "⬤",
    };

    private Button MakeBgButton(string glyph, BoardBackground bg, string tip)
    {
        var b = MakeBaseButton(glyph, tip);
        b.Width = 36;
        b.Tag = bg;
        b.Click += (_, _) => { _background = bg; ApplyBackground(); UpdateBgVisuals(); };
        _bgButtons.Add(b);
        return b;
    }

    // ── 视觉状态 ─────────────────────────────────────────────

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

    private void UpdateToolVisuals()
    {
        foreach (var b in _toolButtons)
        {
            bool active = b.Tag is InkTool t && t == _ink.Tool;
            b.Background = active ? ActiveBrush : IdleBrush;
        }
    }

    private void UpdateBgVisuals()
    {
        foreach (var b in _bgButtons)
        {
            bool active = b.Tag is BoardBackground g && g == _background;
            b.Background = active ? ActiveBrush : IdleBrush;
        }
    }

    private void UpdateUndoButtons()
    {
        var doc = CurrentDoc;
        if (_undoBtn != null) _undoBtn.IsEnabled = doc.CanUndo;
        if (_redoBtn != null) _redoBtn.IsEnabled = doc.CanRedo;
    }

    private void UpdatePageLabel() => _pageLabel.Text = $"{_pageIndex + 1} / {_pages.Count}";

    private void ApplyBackground()
    {
        _boardBorder.Background = new SolidColorBrush(BoardRenderer.BaseColor(_background));
        // 背景线由 InkCanvas 之下的一层绘制：这里用一个专用控件铺在 border 内部
        _boardBorder.Child = new Grid
        {
            Children = { new BoardBackgroundLayer(_background) { IsHitTestVisible = false }, _ink },
        };
    }

    private void UpdateBoardSize()
    {
        var size = _boardBorder.Bounds.Size;
        if (size.Width <= 1 || size.Height <= 1) return;
        _ink.Width = size.Width;
        _ink.Height = size.Height;
    }

    // ── 分页 ─────────────────────────────────────────────────

    private InkDocument CurrentDoc => _pages[_pageIndex];

    private void SwitchPage(int index)
    {
        if (index < 0 || index >= _pages.Count || index == _pageIndex) return;
        _pages[_pageIndex].Changed -= UpdateUndoButtons;
        _pageIndex = index;
        _pages[_pageIndex].Changed += UpdateUndoButtons;
        _ink.Document = _pages[_pageIndex];
        UpdatePageLabel();
        UpdateUndoButtons();
    }

    private void AddPage()
    {
        _pages.Add(new InkDocument());
        SwitchPage(_pages.Count - 1);
    }

    private void DeletePage()
    {
        if (_pages.Count <= 1)
        {
            // 只剩一页时"删除"= 清空
            CurrentDoc.Clear();
            return;
        }
        var i = _pageIndex;
        _pages[i].Changed -= UpdateUndoButtons;
        _pages.RemoveAt(i);
        _pageIndex = Math.Min(i, _pages.Count - 1);
        _pages[_pageIndex].Changed += UpdateUndoButtons;
        _ink.Document = _pages[_pageIndex];
        UpdatePageLabel();
        UpdateUndoButtons();
    }

    // ── 命令 ─────────────────────────────────────────────────

    private void Undo_Click() => CurrentDoc.Undo();
    private void Redo_Click() => CurrentDoc.Redo();

    private void Clear_Click()
    {
        if (!CurrentDoc.HasStrokes) return;
        CurrentDoc.Clear();
        UpdateUndoButtons();
    }

    private async void Export_Click()
    {
        try
        {
            var size = _boardBorder.Bounds.Size;
            int w = (int)Math.Max(size.Width, 800);
            int h = (int)Math.Max(size.Height, 600);

            using var bmp = BoardRenderer.RenderPage(_background, CurrentDoc.Strokes, w, h, scale: 2.0);

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出白板",
                SuggestedFileName = $"白板_{DateTime.Now:yyyyMMdd_HHmmss}_第{_pageIndex + 1}页.png",
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } },
                },
            });
            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            bmp.Save(stream);
            _dirty = false;
            await DialogHelper.ShowMessageAsync(this, "白板", $"已导出：\n{file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("导出白板失败", ex);
            await DialogHelper.ShowMessageAsync(this, "白板", $"导出失败：{ex.Message}");
        }
    }

    // ── 键盘 ─────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Z when ctrl: CurrentDoc.Undo(); e.Handled = true; break;
            case Key.Y when ctrl: CurrentDoc.Redo(); e.Handled = true; break;
            case Key.P: _ink.Tool = InkTool.Pen; UpdateToolVisuals(); e.Handled = true; break;
            case Key.H: _ink.Tool = InkTool.Highlighter; UpdateToolVisuals(); e.Handled = true; break;
            case Key.E: _ink.Tool = InkTool.Eraser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.L: _ink.Tool = InkTool.Laser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.PageUp: SwitchPage(_pageIndex - 1); e.Handled = true; break;
            case Key.PageDown: SwitchPage(_pageIndex + 1); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_dirty) return;
        // 有未导出墨迹：询问（避免辛苦板书被误关）
        bool hasInk = _pages.Any(p => p.HasStrokes);
        if (!hasInk) return;

        var defer = e;
        defer.Cancel = true;
        Dispatcher.UIThread.Post(async () =>
        {
            bool discard = await DialogHelper.ShowConfirmAsync(this, "白板",
                "当前白板还有未导出的内容，关闭后不会保存。\n\n确定要关闭吗？", "关闭", "留下");
            if (discard)
            {
                _dirty = false;
                Close();
            }
        });
    }
}

/// <summary>白板背景层（网格/横线/点阵），铺在 InkCanvas 之下，不吃指针事件</summary>
internal sealed class BoardBackgroundLayer : Control
{
    private readonly BoardBackground _bg;
    public BoardBackgroundLayer(BoardBackground bg) => _bg = bg;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        BoardRenderer.DrawBackground(context, _bg, new Rect(Bounds.Size));
    }
}

/// <summary>粗细预览小圆点（当前笔宽/色的直观反馈）</summary>
internal sealed class StrokeThicknessPreview : Control
{
    private double _thickness;
    private Color _color;

    public StrokeThicknessPreview(double thickness, Color color)
    {
        _thickness = thickness;
        _color = color;
        Width = 30;
        Height = 30;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void SetThickness(double t) { _thickness = t; InvalidateVisual(); }
    public void SetColor(Color c) { _color = c; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double r = Math.Clamp(_thickness / 2, 1, 13);
        context.DrawEllipse(new SolidColorBrush(_color), null, new Point(15, 15), r, r);
    }
}
