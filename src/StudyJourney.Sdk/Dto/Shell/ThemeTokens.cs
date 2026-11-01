using Avalonia.Media;

namespace StudyJourney.Sdk.Dto;

/// <summary>
/// 主题色板（CORE_ARCHITECTURE §2.10）。20+ 个散落的颜色配置归并为 12 个 token。
///
/// **插件只取 token，不在自己的配置里存 <c>#FF...</c>**——否则全项目会出现 5 种"灰"。
/// 要新色就在核心加 token，而不是各插件自定义。
/// </summary>
public sealed record ThemeTokens
{
    /// <summary>强调色（校园蓝，v3.3.0 的 <c>AccentColorHex</c> #2B6CB0）。</summary>
    /// <remarks>消费者：进度环、主窗高亮、按钮主色（全部 UI 插件）</remarks>
    public required Color Accent { get; init; }

    /// <summary>基础背景色。</summary>
    /// <remarks>消费者：主窗背景、卡片背景（倒计时/课表摘要/考试模式）</remarks>
    public required Color Surface { get; init; }

    /// <summary>次级背景（卡片内的分区、列表隔行）。</summary>
    /// <remarks>消费者：课表摘要行、设置页分组</remarks>
    public required Color SurfaceAlt { get; init; }

    /// <summary>主文字色。</summary>
    /// <remarks>消费者：全部文本（原 <c>TextColorHex</c>）</remarks>
    public required Color TextPrimary { get; init; }

    /// <summary>次要文字（说明、单位）。</summary>
    /// <remarks>消费者：倒计时单位、天气描述（原 <c>WeatherInfoColor</c> 一档）</remarks>
    public required Color TextSecondary { get; init; }

    /// <summary>弱化文字（标签、占位、ESC 提示）。</summary>
    /// <remarks>消费者：考试模式的"按 ESC 退出"、空槽位占位文字</remarks>
    public required Color TextDim { get; init; }

    /// <summary>危险色（不可恢复操作、错误）。</summary>
    /// <remarks>消费者：危险操作确认框、更新失败提示、考试倒计时紧迫色</remarks>
    public required Color Danger { get; init; }

    /// <summary>警告色（即将下课、配置冲突）。</summary>
    /// <remarks>消费者：考试倒计时警告色、设置页"字段冲突"红字</remarks>
    public required Color Warning { get; init; }

    /// <summary>成功色（保存成功、连接正常）。</summary>
    /// <remarks>消费者：设置页保存反馈、远程控制台的连接状态</remarks>
    public required Color Success { get; init; }

    /// <summary>正文字体族。</summary>
    /// <remarks>消费者：全部文本（原 <c>FontFamily</c>）</remarks>
    public required string FontFamily { get; init; }

    /// <summary>基准字号（其余字号相对它缩放）。</summary>
    /// <remarks>消费者：设置页、通知正文（原 <c>FontSize</c>）</remarks>
    public required double FontSizeBase { get; init; }

    /// <summary>当前是否暗色主题（用于选对比色、图标变体）。</summary>
    /// <remarks>消费者：所有需要"按明暗换图/换色"的插件</remarks>
    public required bool IsDark { get; init; }
}
