namespace StudyJourney.Sdk;

/// <summary>
/// 主窗的四个固定槽位 ID（CORE_ARCHITECTURE §1.2.9）。**一槽一内容，不允许插件自定义槽位。**
///
/// 用常量而不是字符串字面量：改的时候有引用提示，也避免大小写不一致（slotId 区分大小写）。
/// </summary>
public static class ShellSlots
{
    /// <summary>主区（视觉主体，撑满可用空间）。空置时壳显示"学程"占位。</summary>
    /// <remarks>消费者：倒计时插件</remarks>
    public const string Primary = "main.primary";

    /// <summary>次区（主区右侧/下方，窄，最大 40% 宽）。空置时整块隐藏。</summary>
    /// <remarks>消费者：课表插件（当天节次摘要）</remarks>
    public const string Secondary = "main.secondary";

    /// <summary>角标区（固定高度，宽度按内容）。空置时整块隐藏。</summary>
    /// <remarks>消费者：自动化插件（课件顺序胶囊）</remarks>
    public const string Corner = "main.corner";

    /// <summary>底栏（固定 1 行，常驻）。**核心自己用**，插件不应注册。</summary>
    /// <remarks>消费者：核心（版本/更新提示/状态）</remarks>
    public const string Footer = "main.footer";

    /// <summary>四个槽位的固定顺序（主区 → 次区 → 角标 → 底栏）。壳按此顺序布局。</summary>
    /// <remarks>消费者：ShellService（合法性校验）、ShellHost（布局顺序）</remarks>
    public static IReadOnlyList<string> AllSlots { get; } = [Primary, Secondary, Corner, Footer];

    /// <summary>槽位 ID 是否是四个固定值之一（区分大小写）。</summary>
    /// <remarks>消费者：ShellService（<c>slot-unknown</c> 的判定）</remarks>
    public static bool IsValid(string? slotId)
    {
        if (slotId is null)
            return false;

        foreach (var known in AllSlots)
        {
            if (string.Equals(known, slotId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
