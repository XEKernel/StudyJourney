using Avalonia.Media;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 宿主尚未挂上时的兜底色板（暗色）。
///
/// 存在的理由：<see cref="IShellService.CurrentTheme"/> 是**纯读**，插件在
/// <c>InitializeAsync</c> 里就要取色来构造自己的控件（早于 Avalonia 启动）。
/// 这时候抛异常会炸掉插件初始化，返回一个合理的默认值才是正确行为。
///
/// ⚠ 真值以宿主的 <see cref="IUiHost.CurrentTheme"/> 为准；这里是"能看"不是"好看"。
/// </summary>
public static class ShellDefaults
{
    /// <summary>与 v3.3.0 暗色主题同源的默认色板（校园蓝 #2B6CB0 体系）。</summary>
    public static ThemeTokens DefaultDark { get; } = new()
    {
        Accent = Color.Parse("#2B6CB0"),
        Surface = Color.Parse("#1F1F1F"),
        SurfaceAlt = Color.Parse("#2B2B2B"),
        TextPrimary = Color.Parse("#F0F0F0"),
        TextSecondary = Color.Parse("#B0B0B0"),
        TextDim = Color.Parse("#7A7A7A"),
        Danger = Color.Parse("#E5484D"),
        Warning = Color.Parse("#F5A623"),
        Success = Color.Parse("#3FB950"),
        FontFamily = "Microsoft YaHei UI",
        FontSizeBase = 14,
        IsDark = true,
    };
}
