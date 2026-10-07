using Avalonia.Media;

namespace StudyJourney.Avalonia.Helpers.GradeAnalysis;

/// <summary>
/// 成绩分析模块的配色。取值对齐 **GitHub Dark**，主色沿用项目品牌色校园蓝 <c>#2B6CB0</c>，
/// 不使用蓝紫渐变、不加大圆角与重阴影 —— 与学程全局视觉一致，
/// 也刻意避开「一眼 AI 味」的紫罗兰色系。
/// </summary>
public static class ChartPalette
{
    public const string Panel = "#0D1117";        // 面板底
    public const string PanelAlt = "#161B22";     // 次级面板 / 斑马纹
    public const string Border = "#30363D";       // 描边
    public const string Grid = "#21262D";         // 网格线
    public const string Text = "#C9D1D9";         // 主文字
    public const string TextMuted = "#8B949E";    // 次要文字
    public const string Accent = "#2B6CB0";       // 品牌主色

    // 前三名背景（很淡，避免大屏上抢戏）
    public const string Rank1 = "#3D2E00";
    public const string Rank2 = "#2B2F36";
    public const string Rank3 = "#2A2118";

    /// <summary>多序列调色板。顺序即分配顺序：单人雷达图取第一个，双人 PK 取前两个。</summary>
    public static readonly string[] Series =
    {
        "#58A6FF", // 蓝
        "#3FB950", // 绿
        "#F0883E", // 橙
        "#BC8CFF", // 紫
        "#39C5CF", // 青
        "#DB61A2", // 品红
        "#D29922", // 黄
        "#8B949E", // 灰
    };

    public static string SeriesAt(int index) => Series[((index % Series.Length) + Series.Length) % Series.Length];

    /// <summary>解析 #RRGGBB / #AARRGGBB。非法输入退回主色（绝不抛异常 —— 颜色来自用户配置）。</summary>
    public static Color Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.Parse(Accent);
        try { return Color.Parse(hex.Trim()); }
        catch { return Color.Parse(Accent); }
    }

    public static IBrush Brush(string? hex) => new SolidColorBrush(Parse(hex));

    /// <summary>带透明度的填充（雷达图多边形、折线下方面积）。</summary>
    public static IBrush Fill(string? hex, double opacity) => new SolidColorBrush(Parse(hex), opacity);

    // 常用静态画刷（避免每次渲染都 new，渲染循环里每秒会调用多次）
    public static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse(Text));
    public static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse(TextMuted));
    public static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse(Border));
    public static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse(Grid));
    public static readonly IBrush PanelBrush = new SolidColorBrush(Color.Parse(Panel));
}
