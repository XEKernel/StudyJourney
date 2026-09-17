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
    private readonly BoardBackgroundLayer _bgLayer;
    private readonly Border _toolbar;             // 底部工具栏（自检要量它的实际宽度）
    private readonly TextBlock _pageLabel;
    private readonly StackPanel _palettePanel;
    private readonly StackPanel _thicknessPanel;
    private readonly StrokeThicknessPreview _thicknessPreview;
    private readonly List<Button> _toolButtons = new();
    private readonly List<Button> _bgButtons = new();

    public WhiteboardWindow()
    {
        Title = "白板 · 学程";

        // 全屏板书：老师在大屏上写整块屏，减少干扰与误触。
        // 退出靠 Esc 或工具栏「退出白板」（两者都会走未导出内容的二次确认）。
        WindowDecorations = WindowDecorations.None;
        WindowState = WindowState.FullScreen;
        CanResize = false;
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

        // ── 画布视觉树只构建一次 ──
        // 背景层与墨迹层固定挂在同一个 Grid 上，切背景只改 _bgLayer 的枚举值并重绘。
        // （早期实现每次点背景按钮都新建 Grid 并把 _ink 重新挂进去 —— 那会让 _ink
        //   同时被新旧两个父级持有，视觉树损坏，点背景按钮直接卡死/闪退。）
        _bgLayer = new BoardBackgroundLayer(_background) { IsHitTestVisible = false };
        var boardGrid = new Grid();
        boardGrid.Children.Add(_bgLayer);
        boardGrid.Children.Add(_ink);

        _boardBorder = new Border
        {
            Background = new SolidColorBrush(BoardRenderer.BaseColor(_background)),
            CornerRadius = new CornerRadius(0),
            Child = boardGrid,
        };
        // 注意：不要再用 SizeChanged 给 _ink 显式设 Width/Height ——
        // _ink 在 Grid 里自然会拉伸填满，显式设尺寸会形成"布局→事件→再设尺寸"的反馈环。

        _pageLabel = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(8, 0),
        };

        _thicknessPreview = new StrokeThicknessPreview(DefaultPenThickness, Palette[1].Color);
        _palettePanel = BuildPalette();
        _thicknessPanel = BuildThicknessPanel();

        var toolbar = BuildToolbar();
        _toolbar = toolbar;

        // 工具栏放**底部**（2026-09-15 用户反馈：放顶部老师在大屏前够不到）；
        // 画布占满其余空间。
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(_boardBorder, 0);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(_boardBorder);
        root.Children.Add(toolbar);
        Content = root;

        // 快捷键：撤销/重做/清屏/翻页
        KeyDown += OnKeyDown;

        Opened += (_, _) =>
        {
            UpdatePageLabel();
            UpdateToolVisuals();
            UpdateBgVisuals();
            UpdateUndoButtons();
            _ink.Focus();
        };
        Closing += OnClosing;
    }

    // ── 工具栏 ───────────────────────────────────────────────
    //
    // 布局演进（2026-09-16 用户反馈："按钮按功能合并、要居中、有的跑到屏幕外"）：
    //   原实现是**一整条平铺**：7 组按钮全挤在一行，总宽 ≈2100px，
    //   在 1920/缩放非 100% 的屏上会超出可视区 —— 虽然套了横向 ScrollViewer，
    //   但触屏上滚动条几乎看不见，表现就是"按钮跑到屏幕外面去"。
    //   现在改为：**按功能装进带标题的组框** + **两行居中**（每行是 WrapPanel，
    //   内容放得下就居中，放不下才在行内折行，保证任何分辨率都能点到）。

    private Border BuildToolbar()
    {
        var toolbar = new Border
        {
            // 高度不再写死：分组后可能折行，高度要自适应
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),   // 底栏 → 分隔线画在上边
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(0),
        };

        // ── 第一行：写字相关的（工具 / 颜色 / 粗细）──
        var row1 = NewToolbarRow();
        row1.Children.Add(MakeGroup("书写",
            MakeToolButton("✏", "画笔", "画笔：正常粗细的实线 (P)", InkTool.Pen),
            MakeToolButton("🖍", "荧光笔", "荧光笔：半透明粗线，适合划重点 (H)", InkTool.Highlighter),
            MakeToolButton("◻", "橡皮", "橡皮：按整笔擦除 (E)", InkTool.Eraser),
            MakeToolButton("●", "激光笔", "激光笔：只做指示，约 1.6 秒后自动消失、不导出 (L)", InkTool.Laser)));

        row1.Children.Add(MakeGroup("颜色", _palettePanel));
        row1.Children.Add(MakeGroup("粗细", _thicknessPanel));

        // ── 第二行：改内容的（编辑 / 背景 / 页面 / 文件）──
        var row2 = NewToolbarRow();

        _undoBtn = MakeActionButton("↶", "撤销", "撤销上一笔 (Ctrl+Z)", Undo_Click);
        _redoBtn = MakeActionButton("↷", "重做", "重做 (Ctrl+Y)", Redo_Click);
        row2.Children.Add(MakeGroup("编辑",
            _undoBtn,
            _redoBtn,
            MakeActionButton("🗑", "清空", "清空当前页的所有笔迹", Clear_Click)));

        row2.Children.Add(MakeGroup("背景",
            MakeBgButton("", "纯白", BoardBackground.Blank, "纯白背景：自由板书"),
            MakeBgButton("▦", "网格", BoardBackground.Grid, "网格背景：理科作图 / 坐标系"),
            MakeBgButton("☰", "横线", BoardBackground.Ruled, "横线背景：文科书写 / 英文"),
            MakeBgButton("∴", "点阵", BoardBackground.Dots, "点阵背景：轻量对齐参考"),
            MakeBgButton("", "黑板", BoardBackground.Blackboard, "黑板背景：深色底，投影对比强")));

        // 翻页箭头紧挨页码，配「页面」标题后一目了然（不再塞"上一页/下一页"四个字，省宽度）
        row2.Children.Add(MakeGroup("页面",
            MakeActionButton("◀", "", "上一页 (PgUp)", () => SwitchPage(_pageIndex - 1)),
            _pageLabel,
            MakeActionButton("▶", "", "下一页 (PgDn)", () => SwitchPage(_pageIndex + 1)),
            MakeActionButton("＋", "加页", "在当前页之后新增一页白板", AddPage),
            MakeActionButton("－", "删页", "删除当前页（只剩一页时等于清空）", DeletePage)));

        row2.Children.Add(MakeGroup("文件",
            MakeActionButton("💾", "导出", "把当前页（含背景）导出为 PNG 图片，2 倍分辨率", Export_Click),
            MakeActionButton("✕", "退出", "退出白板 (Esc)；有未导出内容会先询问", Close)));

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        stack.Children.Add(row1);
        stack.Children.Add(row2);
        toolbar.Child = stack;
        return toolbar;
    }

    /// <summary>
    /// 一行工具栏：WrapPanel + 居中。
    /// 内容放得下时面板宽度 = 内容宽度 → 整行居中；
    /// 放不下时才折行（按钮仍在屏幕内，不会"跑出去"）。
    /// </summary>
    private static WrapPanel NewToolbarRow() => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>把按钮按功能装进一个带标题的组框（标题 + 白描边框，一眼看出这几颗是一组）</summary>
    private static Border MakeGroup(string label, params Control[] children)
    {
        var inner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (!string.IsNullOrEmpty(label))
        {
            inner.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 0, 4, 0),
            });
        }

        foreach (var c in children) inner.Children.Add(c);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),      // 直角：项目统一视觉约定
            Padding = new Thickness(10, 5),
            Margin = new Thickness(4, 0),
            Child = inner,
        };
    }

    private Button _undoBtn = null!, _redoBtn = null!;

    private Button MakeToolButton(string glyph, string text, string tip, InkTool tool)
    {
        var b = MakeBaseButton(glyph, text, tip);
        b.Click += (_, _) => { _ink.Tool = tool; UpdateToolVisuals(); };
        b.Tag = tool;
        _toolButtons.Add(b);
        return b;
    }

    private Button MakeActionButton(string glyph, string text, string tip, Action onClick)
    {
        var b = MakeBaseButton(glyph, text, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>工具条按钮：图标 + 中文文字（2026-09-15 用户反馈：纯图标看不懂）。
    /// 高度 44px 满足触屏热区下限，宽度自适应文字。</summary>
    private Button MakeBaseButton(string glyph, string text, string tip)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(glyph))
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
            });
        if (!string.IsNullOrEmpty(text))
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            });

        var b = new Button
        {
            Content = content,
            Height = ToolButtonSize,
            MinWidth = ToolButtonSize,
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

    // 分隔线已由「分组框」取代（MakeGroup），不再需要 MakeSeparator

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
            var b = MakeBaseButton(ThicknessGlyph(t), $"{t:0}", $"{t:0} px 粗的笔迹");
            b.MinWidth = 46;
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

    private Button MakeBgButton(string glyph, string text, BoardBackground bg, string tip)
    {
        var b = MakeBaseButton(glyph, text, tip);
        b.Tag = bg;
        b.Click += (_, _) =>
        {
            _background = bg;
            ApplyBackground();
            UpdateBgVisuals();
        };
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

    /// <summary>
    /// 切换画布背景（纯白 / 网格 / 横线 / 点阵 / 黑板）。
    ///
    /// ⚠ 只更新已有背景层的枚举值并重绘，**绝不重建视觉树**。
    /// 早期实现写的是 `_boardBorder.Child = new Grid { Children = { layer, _ink } }` ——
    /// 每点一次背景按钮就把 _ink 从旧父节点摘下来塞进新建的 Grid，导致同一个
    /// InkCanvas 被新旧两个父级同时持有（Avalonia 视觉树损坏），表现为
    /// 「点背景按钮卡死 / 闪退」。
    /// </summary>
    private void ApplyBackground()
    {
        _bgLayer.SetBackground(_background);
        // 底色也同步给 Border，避免切换瞬间露出上一层底色
        _boardBorder.Background = new SolidColorBrush(BoardRenderer.BaseColor(_background));
    }

    // ── 分页 ─────────────────────────────────────────────────

    private InkDocument CurrentDoc => _pages[_pageIndex];

    /// <summary>
    /// 是否有未导出的板书。供自动更新判断"现在重启会不会把老师的板书丢掉"——
    /// 自动更新走 Environment.Exit，会绕过 OnClosing 里的未保存确认，必须先问过这里。
    /// </summary>
    public bool HasUnsavedInk => _dirty && _pages.Any(p => p.HasStrokes);

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

/// <summary>白板背景层（网格/横线/点阵），铺在 InkCanvas 之下，不吃指针事件。
/// 背景可**原地切换**（SetBackground）—— 宿主只需这一个实例，不必重建视觉树。</summary>
internal sealed class BoardBackgroundLayer : Control
{
    private BoardBackground _bg;

    public BoardBackgroundLayer(BoardBackground bg) => _bg = bg;

    /// <summary>切换背景样式并重绘（不更换控件实例）</summary>
    public void SetBackground(BoardBackground bg)
    {
        if (_bg == bg) return;
        _bg = bg;
        InvalidateVisual();
    }

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
