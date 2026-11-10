using StudyJourney.Plugin.Countdown;
using Xunit;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>
/// 卡片刷新测试：走到 0 天/结束后/日期未设置这三种"文案会整体切换"的状态。
/// 只读控件文本，不做渲染（渲染需要平台，属于 App 层的事）。
/// </summary>
public sealed class CountdownCardTests
{
    private static CountdownCard NewCard(string gaokaoDate, string startDate = "2024-08-24")
    {
        var settings = new CountdownSettings { GaokaoDateStr = gaokaoDate, StartDateStr = startDate };
        return new CountdownCard(settings, new FakeShellService().CurrentTheme);
    }

    [Fact(DisplayName = "M1B-30 目标当天上午 → 标题「距高考」，天数 0")]
    public void Refresh_TargetToday_ShowsZeroDays()
    {
        var card = NewCard("2027-06-07 09:00:00");

        card.Refresh(new DateTime(2027, 6, 7, 8, 0, 0));

        Assert.Equal("距高考", card.TitleShown);
        Assert.Equal("0", card.DaysTextShown);
    }

    [Fact(DisplayName = "M1B-31 目标前一天 → 天数 1")]
    public void Refresh_DayBefore_ShowsOneDay()
    {
        var card = NewCard("2027-06-07 09:00:00");

        card.Refresh(new DateTime(2027, 6, 6, 23, 0, 0));

        Assert.Equal("距高考", card.TitleShown);
        Assert.Equal("1", card.DaysTextShown);
    }

    [Fact(DisplayName = "M1B-32 目标已过 → 文案收尾，天数 0（不显示负数）")]
    public void Refresh_PastTarget_ShowsEndedState()
    {
        var card = NewCard("2027-06-07 09:00:00");

        card.Refresh(new DateTime(2027, 6, 8, 0, 0, 0));

        Assert.Equal("高考已结束", card.TitleShown);
        Assert.Equal("0", card.DaysTextShown);
    }

    [Fact(DisplayName = "M1B-33 日期非法 → 「日期未设置」+ 占位天数，不抛")]
    public void Refresh_InvalidDate_ShowsUnsetState()
    {
        var card = NewCard("还没定");

        card.Refresh(DateTime.Now);

        Assert.Equal("日期未设置", card.TitleShown);
        Assert.Equal("--", card.DaysTextShown);
    }
}
