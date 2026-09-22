using System;
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
/// 屏幕批注覆盖层（PLANNING 2.3）：老师在**任意应用**（PDF、Word、网页、PPT…）之上直接写写画画。
///
/// 与白板/查看器批注的坐标系区别（PLANNING 2.3 明确要求分开实现）：
///   · 本层 = **固定屏幕坐标**（悬浮在底层应用之上，不随底层滚动）→ 用 IdentityInkSurface
///   · 查看器内批注 = 文档内容坐标，随滚动/缩放（属 2.1，另做）
///   两者共用 Helpers/InkLayer + Helpers/InkCanvas（2.6 步骤 3 的公共组件），只差这里注入恒等变换。
///
/// 交互：
///   · 默认「批注模式」：整屏透明覆盖，捕获指针书写；顶部悬浮工具条（不遮挡中央，遵循 #12 规范）
///   · 「穿透模式」（播放/操作底层应用时）：窗口设点击穿透，工具条仍可点 → 由按钮/快捷键切换
///   · 墨迹不落盘；「截屏保存」把当前批注连同桌面合成一张 PNG 带走
///   · Ctrl+Z / Ctrl+Y 撤销重做；Ctrl+Shift+D 或 Esc 退出批注
///
/// 触屏约定：**手指 = 墨迹**（覆盖层没有可滚动内容，不存在 2.1 那种"手指留给滚动"的冲突）；
/// 触控笔同样书写；鼠标左键书写。橡皮用工具栏切换。
/// </summary>
public sealed class ScreenAnnotationWindow : Window, IUnsavedWork
{
    private const double ToolbarButtonSize = 44;   // 触屏热区下限（PLANNING 2.0 UI 约定）

    private static readonly (string Name, Color Color)[] Palette =
    {
        ("红", Color.FromRgb(0xD1, 0x3A, 0x3A)),
        ("校园蓝", Color.FromRgb(0x2B, 0x6C, 0xB0)),
        ("绿", Color.FromRgb(0x2F, 0x8F, 0x4F)),
        ("黄", Color.FromRgb(0xE0, 0xB4, 0x1E)),
        ("白", Colors.White),
    };

    private readonly InkCanvas _ink = new()
    {
        AllowMouseInk = true,
        AllowTouchInk = true,      // 覆盖层无滚动 → 手指直接书写
        Thickness = 3,
        Color = Palette[0].Color,
        EraserRadius = 16,
        Tool = InkTool.Pen,
    };

    private readonly StackPanel _toolbarRow;

    /// <summary>是否有圈画内容（自动更新重启前用它判断"现在重启会不会丢批注"）</summary>
    public bool HasStrokes => _ink.Document.HasStrokes;

    public bool HasUnsavedWork => HasStrokes;
    public string UnsavedWorkHint => "屏幕批注上有未导出的圈画";
    private readonly List<Button> _toolButtons = new();
    private Border _toolbar = null!;
    private Button _passThroughBtn = null!;
    private bool _passThrough;      // 穿透模式：底层应用可正常操作

    public ScreenAnnotationWindow()
    {
        Title = "屏幕批注 · 学程";
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _ink.Document.Changed += () => UpdateUndoButtons();
        _ink.StrokeCommitted += () => UpdateUndoButtons();

        _toolbarRow = BuildToolbarRow();
        _toolbar = BuildToolbar();

        var root = new Grid();
        root.Children.Add(_ink);
        root.Children.Add(_toolbar);
        Content = root;

        KeyDown += OnKeyDown;

        Opened += (_, _) =>
        {
            CoverAllScreens();
            UpdateToolVisuals();
            UpdateUndoButtons();
            _ink.Focus();
        };
    }

    // ── 铺满全部屏幕（多屏：整体覆盖，主屏为默认书写区）────────

    private void CoverAllScreens()
    {
        try
        {
            var screens = Screens.All;
            if (screens.Count == 0) return;

            // 覆盖所有屏幕的并集（教室单屏时就是主屏全屏）
            int minX = screens.Min(s => s.Bounds.X);
            int minY = screens.Min(s => s.Bounds.Y);
            int maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
            int maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);

            // Bounds 是**物理像素**，Window.Width/Height 是 **DIP** —— 缩放不是 100%
            // 时必须换算，否则窗口会比屏幕大一圈（超出屏幕）。
            double scaling = ScalingAt(minX, minY);
            Position = new PixelPoint(minX, minY);
            Width = (maxX - minX) / scaling;
            Height = (maxY - minY) / scaling;
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn($"屏幕批注定屏失败: {ex.Message}");
        }
    }

    /// <summary>指定虚拟桌面坐标所在屏幕的缩放系数（取不到时按 1.0 处理）</summary>
    private double ScalingAt(int x, int y)
    {
        try
        {
            var screen = Screens.ScreenFromPoint(new PixelPoint(x, y))
                         ?? Screens.ScreenFromWindow(this)
                         ?? Screens.Primary;
            double s = screen?.Scaling ?? 1.0;
            return s <= 0 ? 1.0 : s;
        }
        catch { return 1.0; }
    }

    // ── 工具条 ───────────────────────────────────────────────

    private Border BuildToolbar()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1B, 0x1B, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),      // 直角：项目统一视觉约定
            Padding = new Thickness(10, 6),
            // 屏幕**底部**居中：老师站在大屏前，顶部够不到（2026-09-15 用户反馈）。
            // 底部同样不遮挡画面中央，仍符合 #12「通知类浮层让出屏幕中央」的精神。
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16),
            MaxWidth = 1600,                          // 极窄屏时交给横向滚动，不撑破屏幕
        };
        border.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _toolbarRow,
        };
        return border;
    }

    private StackPanel BuildToolbarRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(MakeToolButton("✏", "画笔", "画笔：正常粗细的实线 (P)", InkTool.Pen));
        row.Children.Add(MakeToolButton("🖍", "荧光笔", "荧光笔：半透明粗线，适合划重点 (H)", InkTool.Highlighter));
        row.Children.Add(MakeToolButton("◻", "橡皮", "橡皮：按整笔擦除 (E)", InkTool.Eraser));
        row.Children.Add(MakeToolButton("●", "激光笔", "激光笔：只做指示，约 1.6 秒后自动消失 (L)", InkTool.Laser));
        row.Children.Add(Separator());

        foreach (var (name, color) in Palette)
        {
            var swatch = new Button
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
            ToolTip.SetTip(swatch, $"{name}（笔迹颜色）");
            swatch.Click += (_, _) => { _ink.Color = captured; UpdateToolVisuals(); };
            row.Children.Add(swatch);
        }

        // 粗细（三档够用，避免工具条过长）
        foreach (double t in new[] { 2.0, 4.0, 8.0 })
        {
            var b = MakeBaseButton(ThicknessGlyph(t), $"{t:0}", $"{t:0} px 粗的笔迹（粗细也一并放大橡皮）");
            b.MinWidth = 46;
            var captured = t;
            b.Click += (_, _) =>
            {
                _ink.Thickness = captured;
                _ink.EraserRadius = Math.Max(captured * 3.5, 14);
            };
            row.Children.Add(b);
        }

        row.Children.Add(Separator());

        _undoBtn = MakeActionButton("↶", "撤销", "撤销上一笔 (Ctrl+Z)", () => _ink.Document.Undo());
        _redoBtn = MakeActionButton("↷", "重做", "重做 (Ctrl+Y)", () => _ink.Document.Redo());
        row.Children.Add(_undoBtn);
        row.Children.Add(_redoBtn);
        row.Children.Add(MakeActionButton("🗑", "清除", "清除全部批注", ClearAll));
        row.Children.Add(Separator());

        _passThroughBtn = MakeActionButton("↔", "穿透模式",
            "穿透模式：让鼠标去操作底层应用（再点一次回到批注）", TogglePassThrough);
        row.Children.Add(_passThroughBtn);

        row.Children.Add(MakeActionButton("📷", "截屏保存", "把当前批注与桌面合成一张 PNG 保存", SaveScreenshot));
        row.Children.Add(MakeActionButton("✕", "退出批注", "退出批注模式 (Esc)", Close));
        return row;
    }

    private static string ThicknessGlyph(double t) => t switch
    {
        <= 2.5 => "·",
        <= 5 => "•",
        _ => "●",
    };

    private Button _undoBtn = null!, _redoBtn = null!;

    private Button MakeToolButton(string glyph, string text, string tip, InkTool tool)
    {
        var b = MakeBaseButton(glyph, text, tip);
        b.Tag = tool;
        b.Click += (_, _) => { _ink.Tool = tool; UpdateToolVisuals(); };
        _toolButtons.Add(b);
        return b;
    }

    private Button MakeActionButton(string glyph, string text, string tip, Action onClick)
    {
        var b = MakeBaseButton(glyph, text, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>工具条按钮：图标 + 中文文字（2026-09-15 用户反馈：纯图标看不懂）。</summary>
    private static Button MakeBaseButton(string glyph, string text, string tip)
    {
        var b = new Button
        {
            Content = BuildButtonContent(glyph, text),
            Height = ToolbarButtonSize,
            MinWidth = ToolbarButtonSize,
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

    /// <summary>「图标 + 文字」横向内容（图标或文字为空时自动省略）</summary>
    private static StackPanel BuildButtonContent(string glyph, string text)
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
        return content;
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(4, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
    };

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

    private void UpdateToolVisuals()
    {
        foreach (var b in _toolButtons)
            b.Background = b.Tag is InkTool t && t == _ink.Tool ? ActiveBrush : IdleBrush;
    }

    private void UpdateUndoButtons()
    {
        if (_undoBtn != null) _undoBtn.IsEnabled = _ink.Document.CanUndo;
        if (_redoBtn != null) _redoBtn.IsEnabled = _ink.Document.CanRedo;
    }

    // ── 穿透模式 ─────────────────────────────────────────────

    /// <summary>
    /// 穿透模式：整层不吃鼠标，底层应用照常操作；工具条仍可点击（它是独立子元素，
    /// 但 WS_EX_TRANSPARENT 作用于整个 HWND，故这里改为**缩小覆盖层**到工具条区域）。
    /// 这样比"整窗穿透+工具条例外"更可靠（后者需要 WndProc 命中测试，Avalonia 会覆盖）。
    /// </summary>
    private void TogglePassThrough()
    {
        _passThrough = !_passThrough;
        try
        {
            if (_passThrough)
            {
                // 缩到"工具条高度"的一条，并**贴到屏幕底部**（工具栏在底部，
                // 若只改 Height 而不改 Position，窗口仍锚在左上角 → 工具条会跑到屏幕上方）。
                var h = Math.Max(_toolbar.Bounds.Height, ToolbarButtonSize) + 64;
                var screens = Screens.All;
                if (screens.Count > 0)
                {
                    int minX = screens.Min(s => s.Bounds.X);
                    int maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
                    int maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);
                    Width = maxX - minX;
                    Height = h;
                    Position = new PixelPoint(minX, maxY - (int)h);
                }
                else
                {
                    Height = h;
                }

                _ink.IsReadOnly = true;

                // ⚠ 不要在这里把按钮从 _toolbarRow 移除再加一个新的 —— 本方法正是由
                // 这个按钮的 Click 触发的，在控件自己的事件处理过程中把它摘离视觉树
                // 会让事件路由访问已脱离的控件。改为复用同一个按钮，只换文字与配色。
                SetPassThroughButtonVisual(true);
            }
            else
            {
                CoverAllScreens();
                _ink.IsReadOnly = false;
                SetPassThroughButtonVisual(false);
                _ink.Focus();
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warn($"切换批注穿透模式失败: {ex.Message}");
        }
    }

    /// <summary>更新穿透按钮的文字 / 配色（复用同一控件实例，不重建按钮）</summary>
    private void SetPassThroughButtonVisual(bool passThrough)
    {
        _passThroughBtn.Content = BuildButtonContent(passThrough ? "✏" : "↔",
            passThrough ? "返回批注" : "穿透模式");
        _passThroughBtn.Background = passThrough ? ActiveBrush : IdleBrush;
        ToolTip.SetTip(_passThroughBtn, passThrough
            ? "返回批注模式：重新铺满全屏并捕获书写"
            : "穿透模式：让鼠标去操作底层应用（再点一次回到批注）");
    }

    // ── 截屏保存 ─────────────────────────────────────────────

    /// <summary>把"当前整屏画面 + 批注"合成一张 PNG。
    /// 截图走系统屏幕抓取（含底层应用），再把墨迹叠加绘制上去。</summary>
    private async void SaveScreenshot()
    {
        bool wasToolbarVisible = _toolbar.IsVisible;
        bool wasTopmost = Topmost;
        try
        {
            // 抓屏区域 = 窗口自身当前占据的区域（物理像素）。
            // 注意不能用"主屏 Bounds"——本窗口是铺满所有屏的并集，用单屏尺寸会导致
            // 合成出来的墨迹与桌面画面对不齐（尤其在多屏/缩放非 100% 时）。
            double scaling = ScalingAt(Position.X, Position.Y);
            int wPx = (int)Math.Round(Width * scaling);
            int hPx = (int)Math.Round(Height * scaling);
            if (wPx <= 1 || hPx <= 1) return;

            // 1. 先把本覆盖层临时隐藏，避免把自己的工具条也拍进去
            _toolbar.IsVisible = false;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // 2. 抓屏
            using var screenShot = CaptureScreen(new PixelRect(Position.X, Position.Y, wPx, hPx));

            // 3. 合成墨迹（RTB 的 DPI 跟着缩放走，这样墨迹的 DIP 坐标能对上物理像素）
            using var composite = new RenderTargetBitmap(new PixelSize(wPx, hPx), new Vector(96 * scaling, 96 * scaling));
            using (var ctx = composite.CreateDrawingContext())
            {
                if (screenShot != null)
                    ctx.DrawImage(screenShot, new Rect(0, 0, Width, Height));
                else
                    ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), new Rect(0, 0, Width, Height));

                foreach (var s in _ink.Document.Strokes)
                {
                    if (s.Tool == InkTool.Laser) continue;   // 激光笔是临时指示，不带走
                    ctx.DrawGeometry(null, InkGeometry.BuildPen(s), InkGeometry.BuildGeometry(s, IdentityInkSurface.Instance));
                }
            }

            _toolbar.IsVisible = wasToolbarVisible;

            // 4. 保存（⭐ 打开系统文件对话框前必须取消置顶：
            //    本窗口是 Topmost + 全屏，系统对话框不是 Topmost，会被永远压在下面 ——
            //    用户看不到对话框，看起来就是"点了没反应 / 卡死"。）
            Topmost = false;
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存批注截图",
                    SuggestedFileName = $"批注_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    DefaultExtension = "png",
                    FileTypeChoices = new[] { new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } } },
                });
                if (file == null) return;

                await using var stream = await file.OpenWriteAsync();
                composite.Save(stream);
                await DialogHelper.ShowMessageAsync(this, "屏幕批注", $"已保存：\n{file.Path.LocalPath}");
            }
            finally
            {
                Topmost = wasTopmost;
            }
        }
        catch (Exception ex)
        {
            _toolbar.IsVisible = true;
            Topmost = wasTopmost;
            AppLogger.Error("保存批注截图失败", ex);
            await DialogHelper.ShowMessageAsync(this, "屏幕批注", $"保存失败：{ex.Message}");
        }
    }

    /// <summary>抓取指定屏幕区域（GDI BitBlt，含底层应用画面）。
    /// GDI 句柄可能创建失败（大尺寸/资源紧张），必须逐个判空 —— 对 IntPtr.Zero
    /// 调 GetDIBits/DeleteObject 会直接让进程挂掉（"闪退"的隐患之一）。</summary>
    private static Bitmap? CaptureScreen(PixelRect bounds)
    {
        int w = bounds.Width, h = bounds.Height;
        if (w <= 0 || h <= 0) return null;

        IntPtr hDesk = IntPtr.Zero, hMem = IntPtr.Zero, hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hDesk = GetDC(IntPtr.Zero);
            if (hDesk == IntPtr.Zero) { AppLogger.Warn("抓屏失败：GetDC 返回空"); return null; }

            hMem = CreateCompatibleDC(hDesk);
            if (hMem == IntPtr.Zero) { AppLogger.Warn("抓屏失败：CreateCompatibleDC 返回空"); return null; }

            hBmp = CreateCompatibleBitmap(hDesk, w, h);
            if (hBmp == IntPtr.Zero) { AppLogger.Warn($"抓屏失败：CreateCompatibleBitmap 返回空（{w}x{h}）"); return null; }

            old = SelectObject(hMem, hBmp);

            // CAPTUREBLT(0x40000000) 让分层窗口（含本程序自己的透明层）也进入位图
            BitBlt(hMem, 0, 0, w, h, hDesk, bounds.X, bounds.Y, 0x00CC0020 | 0x40000000);

            // 先把原位图选回 DC —— 未取消选中的位图无法被 DeleteObject 释放（GDI 泄漏）
            if (old != IntPtr.Zero) { SelectObject(hMem, old); old = IntPtr.Zero; }

            var png = BitmapToPng(hBmp, w, h);
            if (png == null) return null;

            using var ms = new MemoryStream(png);
            return new Bitmap(ms);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"抓屏失败: {ex.Message}");
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero && hMem != IntPtr.Zero) SelectObject(hMem, old);
            if (hMem != IntPtr.Zero) DeleteDC(hMem);
            if (hDesk != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hDesk);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
        }
    }

    // ── GDI P/Invoke ─────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight;
        public ushort biPlanes; public ushort biBitCount; public uint biCompression;
        public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
        public uint biClrUsed; public uint biClrImportant;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
        byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    /// <summary>HBITMAP → PNG 字节（32bpp BGRA → PNG，用 zlib 手工封装，避免引入额外依赖）</summary>
    private static byte[]? BitmapToPng(IntPtr hBmp, int w, int h)
    {
        try
        {
            var hdr = new BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,       // 负数 = 自上而下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var buf = new byte[w * h * 4];
            IntPtr hdc = GetDC(IntPtr.Zero);
            int got = GetDIBits(hdc, hBmp, 0, (uint)h, buf, ref hdr, 0);
            ReleaseDC(IntPtr.Zero, hdc);
            if (got == 0) return null;

            return PngWriter.EncodeBgra(buf, w, h);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"位图转 PNG 失败: {ex.Message}");
            return null;
        }
    }

    // ── 键盘 ─────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Z when ctrl: _ink.Document.Undo(); e.Handled = true; break;
            case Key.Y when ctrl: _ink.Document.Redo(); e.Handled = true; break;
            case Key.P: _ink.Tool = InkTool.Pen; UpdateToolVisuals(); e.Handled = true; break;
            case Key.H: _ink.Tool = InkTool.Highlighter; UpdateToolVisuals(); e.Handled = true; break;
            case Key.E: _ink.Tool = InkTool.Eraser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.L: _ink.Tool = InkTool.Laser; UpdateToolVisuals(); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void ClearAll()
    {
        _ink.Document.Clear();
        UpdateUndoButtons();
    }
}

/// <summary>极简 PNG 编码器（BGRA 原始像素 → PNG）。
/// 只为一个用途服务：把截屏合成结果落盘，不值得为此引入图像库依赖。</summary>
internal static class PngWriter
{
    public static byte[] EncodeBgra(byte[] bgra, int width, int height)
    {
        // 原始扫描线（每行前置 filter 字节 0）
        var raw = new byte[(width * 4 + 1) * height];
        int src = 0, dst = 0;
        for (int y = 0; y < height; y++)
        {
            raw[dst++] = 0;   // filter: None
            for (int x = 0; x < width; x++)
            {
                // BGRA → RGBA
                raw[dst++] = bgra[src + 2];
                raw[dst++] = bgra[src + 1];
                raw[dst++] = bgra[src + 0];
                raw[dst++] = bgra[src + 3];
                src += 4;
            }
        }

        var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // color type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        // IDAT（zlib 压缩）
        WriteChunk(output, "IDAT", ZlibCompress(raw));

        // IEND
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteBE(byte[] arr, int offset, int value)
    {
        arr[offset] = (byte)(value >> 24);
        arr[offset + 1] = (byte)(value >> 16);
        arr[offset + 2] = (byte)(value >> 8);
        arr[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes);
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] a, byte[] b)
    {
        _crcTable ??= BuildCrcTable();
        uint c = 0xFFFFFFFF;
        foreach (var t in a) c = _crcTable[(c ^ t) & 0xFF] ^ (c >> 8);
        foreach (var t in b) c = _crcTable[(c ^ t) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>zlib 容器（2 字节头 + deflate stored 块 + adler32）——不做压缩，只保证合法可读</summary>
    private static byte[] ZlibCompress(byte[] data)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x78);   // CMF: deflate, 32K window
        ms.WriteByte(0x01);   // FLG: 无预设字典，校验位合法

        int pos = 0;
        while (pos < data.Length)
        {
            int take = Math.Min(65535, data.Length - pos);
            bool last = pos + take >= data.Length;
            ms.WriteByte((byte)(last ? 1 : 0));            // BFINAL + BTYPE=00(存储)
            ms.WriteByte((byte)(take & 0xFF));
            ms.WriteByte((byte)((take >> 8) & 0xFF));
            int nlen = (~take) & 0xFFFF;
            ms.WriteByte((byte)(nlen & 0xFF));
            ms.WriteByte((byte)((nlen >> 8) & 0xFF));
            ms.Write(data, pos, take);
            pos += take;
        }

        uint a = 1, b = 0;
        foreach (var t in data)
        {
            a = (a + t) % 65521;
            b = (b + a) % 65521;
        }
        var adler = new byte[4];
        WriteBE(adler, 0, unchecked((int)((b << 16) | a)));
        ms.Write(adler);

        return ms.ToArray();
    }
}
