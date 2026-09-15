using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace StudyJourney.Avalonia.Helpers;

/// <summary>一页的渲染结果（BGRA 原始像素，自上而下）</summary>
public sealed class PdfPageBitmap
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Bgra { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// PDF 渲染器（PLANNING 2.1）：包住 PDFium（Docnet.Core），对外只暴露
/// 「页面尺寸」与「按需渲染某一页」两件事。
///
/// 关键约束：
///   · **PDFium 非线程安全**（Docnet 源码注释原话："PDFium is not thread-safe so we need
///     to lock every native call"）→ 本类内部用一把静态锁把所有原生调用串行化。
///     渲染放后台线程时，多页并发渲染会在原生层崩，必须走这里排队。
///   · `GetPageWidth/Height` 返回的是**按 PageDimensions 缩放后**的尺寸，不是 pt。
///     所以量原始尺寸要用 `PageDimensions(1.0)` 的读取器；量渲染尺寸要用渲染缩放的读取器。
///   · 渲染读取器按缩放缓存：缩放值变了才重建（重建 = 重新解析文件，别每次渲染都做）。
///
/// 坐标系约定（与 InkLayer 配合）：内容坐标 = **PtToDip 换算后的 DIP 空间、zoom = 1**。
/// 见 PdfReaderWindow 的布局计算。
/// </summary>
public sealed class PdfRenderer : IDisposable
{
    /// <summary>PDF 点（1/72 英寸）→ Avalonia DIP（96 DPI）</summary>
    public const double PtToDip = 96.0 / 72.0;

    /// <summary>渲染缩放的夹取范围（相对 1 pt = 1 px）</summary>
    private const double MinScale = 0.1;
    private const double MaxScale = 6.0;

    /// <summary>PDFium 全局串行锁（原生层非线程安全）</summary>
    private static readonly object NativeGate = new();

    private readonly string _path;
    private readonly IDocReader _metrics;      // PageDimensions(1.0)：只用来量每页的 pt 尺寸
    private IDocReader? _renderReader;         // 当前渲染缩放下的读取器（惰性重建）
    private double _renderScale = -1;
    private bool _disposed;

    /// <summary>总页数</summary>
    public int PageCount { get; }

    /// <summary>每页尺寸（**PDF 点**，未缩放）。用于在任何页被渲染之前就能算好布局，避免滚动时跳动。</summary>
    public IReadOnlyList<Size> PageSizesPt { get; }

    public string FilePath => _path;

    public PdfRenderer(string path, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径为空", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("PDF 文件不存在", path);

        _path = path;
        try
        {
            lock (NativeGate)
            {
                _metrics = string.IsNullOrEmpty(password)
                    ? DocLib.Instance.GetDocReader(path, new PageDimensions(1.0))
                    : DocLib.Instance.GetDocReader(path, password, new PageDimensions(1.0));

                PageCount = _metrics.GetPageCount();
                if (PageCount <= 0) throw new InvalidOperationException("这份 PDF 没有任何页面");

                var sizes = new List<Size>(PageCount);
                for (int i = 0; i < PageCount; i++)
                {
                    using var pr = _metrics.GetPageReader(i);
                    sizes.Add(new Size(pr.GetPageWidth(), pr.GetPageHeight()));
                }
                PageSizesPt = sizes;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"打开 PDF 失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 渲染指定页（0 基）。返回 BGRA 原始像素。
    /// <paramref name="scale"/> = 1 pt 对应多少像素（1.0 ≈ 72 DPI；阅读器按显示尺寸传 ≈1.333×zoom，保证文字不糊）。
    /// </summary>
    public PdfPageBitmap RenderPage(int index, double scale)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PdfRenderer));
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        scale = Math.Clamp(scale, MinScale, MaxScale);

        lock (NativeGate)
        {
            // 缩放变化才重建渲染读取器（重建代价 = 重新解析整份 PDF）
            if (_renderReader == null || Math.Abs(_renderScale - scale) > 0.001)
            {
                _renderReader?.Dispose();
                _renderReader = DocLib.Instance.GetDocReader(_path, new PageDimensions(scale));
                _renderScale = scale;
            }

            using var pr = _renderReader.GetPageReader(index);
            int w = pr.GetPageWidth();
            int h = pr.GetPageHeight();
            var bytes = pr.GetImage();

            if (w <= 0 || h <= 0) throw new InvalidOperationException($"第 {index + 1} 页渲染尺寸非法：{w}x{h}");

            // GetImage 返回 BGRA；正常长度应为 w*h*4。防御性裁剪/校验，避免把脏数据当位图。
            int expected = w * h * 4;
            if (bytes == null || bytes.Length < expected)
                throw new InvalidOperationException($"第 {index + 1} 页像素数据不完整：{bytes?.Length ?? 0} < {expected}");

            if (bytes.Length != expected)
                Array.Resize(ref bytes, expected);

            return new PdfPageBitmap { Width = w, Height = h, Bgra = bytes };
        }
    }

    /// <summary>把 PDF 点尺寸换算成给定缩放下的 DIP 尺寸</summary>
    public static Size PtToDipSize(Size pt, double zoom)
        => new(pt.Width * PtToDip * zoom, pt.Height * PtToDip * zoom);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            lock (NativeGate)
            {
                _renderReader?.Dispose();
                _renderReader = null;
                _metrics.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"释放 PDF 读取器失败: {ex.Message}");
        }
    }
}
