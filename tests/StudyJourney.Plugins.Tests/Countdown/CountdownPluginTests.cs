using System.Text.Json.Nodes;
using StudyJourney.Plugin.Countdown;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>
/// 插件生命周期测试：只验证"注册了该注册的、释放了该释放的、重置写对了 8 个键"。
/// 不验证定时器真实走秒（那需要等 1 秒，等于给 CI 添堵）——秒级刷新由卡片的 Refresh 直接覆盖。
/// </summary>
public sealed class CountdownPluginTests
{
    private static (CountdownPlugin Plugin, FakeShellService Shell, FakePluginContext Context) Create(
        JsonNode? section = null)
    {
        var shell = new FakeShellService();
        return (new CountdownPlugin(), shell, new FakePluginContext(shell, section));
    }

    [Fact(DisplayName = "M1B-21 Id 与 plugin.json 一致")]
    public void Id_IsCountdown()
    {
        Assert.Equal("countdown", new CountdownPlugin().Id);
    }

    [Fact(DisplayName = "M1B-22 Initialize 注册 1 个菜单 + 1 个托盘 + primary 槽位卡片，且不抢热键")]
    public async Task InitializeAsync_RegistersMenuTrayAndSlot()
    {
        var (plugin, shell, context) = Create();

        var result = await plugin.InitializeAsync(context);

        Assert.True(result.IsOk);

        var menu = Assert.Single(shell.MenuItems);
        Assert.Equal("countdown.settings", menu.Id);
        Assert.Equal("倒计时…", menu.Header);
        Assert.Equal(10, menu.Order);

        var tray = Assert.Single(shell.TrayItems);
        Assert.Equal("countdown.toggle", tray.Id);
        Assert.Equal("显示/隐藏倒计时", tray.Header);
        Assert.Equal(10, tray.Order);

        var slot = Assert.Single(shell.SlotContents);
        Assert.Equal(ShellSlots.Primary, slot.SlotId);
        Assert.IsType<CountdownCard>(slot.Content);

        Assert.Empty(shell.HotKeys);          // Ctrl+Shift+H 归壳
        Assert.Equal(1, shell.PostToUiCount); // 建卡片必须投递到 UI 线程
    }

    [Fact(DisplayName = "M1B-23 菜单/托盘回调可执行：托盘切换可见性不抛")]
    public async Task Callbacks_ToggleVisibility()
    {
        var (plugin, shell, context) = Create();
        await plugin.InitializeAsync(context);

        var card = Assert.IsType<CountdownCard>(Assert.Single(shell.SlotContents).Content);
        Assert.True(card.IsVisible);

        Assert.Single(shell.TrayItems).Invoke();
        Assert.False(card.IsVisible);

        Assert.Single(shell.TrayItems).Invoke();
        Assert.True(card.IsVisible);

        Assert.Single(shell.MenuItems).Invoke();   // 设置入口目前只记日志
        Assert.NotEmpty(context.Logger.Infos);
    }

    [Fact(DisplayName = "M1B-24 StopAsync 幂等：释放菜单/托盘/槽位三个句柄，调两次不抛")]
    public async Task StopAsync_IsIdempotent()
    {
        var (plugin, shell, context) = Create();
        await plugin.InitializeAsync(context);

        await plugin.StopAsync();
        Assert.Equal(3, shell.DisposedHandles);

        await plugin.StopAsync();                 // 第二次不能抛，也不能重复释放
        Assert.Equal(3, shell.DisposedHandles);
    }

    [Fact(DisplayName = "M1B-25 未 Initialize 就 Stop 也不抛（卸载兜底路径）")]
    public async Task StopAsync_WithoutInitialize_DoesNotThrow()
    {
        await new CountdownPlugin().StopAsync();
    }

    [Fact(DisplayName = "M1B-26 ResetToDefaults 把 8 个键就地写成默认值，其它键原样保留")]
    public void ResetToDefaults_WritesAllEightFields()
    {
        var node = new JsonObject
        {
            ["GaokaoDateStr"] = "1999-01-01",
            ["StartDateStr"] = "1999-01-01",
            ["ShowDays"] = false,
            ["ShowHours"] = false,
            ["ShowMinutes"] = false,
            ["ShowSeconds"] = false,
            ["ShowProgressBar"] = false,
            ["CountdownProgressBarStyle"] = true,
            ["NotMine"] = "keep-me",
        };

        new CountdownPlugin().ResetToDefaults(node);

        Assert.Equal(CountdownSettings.DefaultGaokaoDate, node["GaokaoDateStr"]!.GetValue<string>());
        Assert.Equal(CountdownSettings.DefaultStartDate, node["StartDateStr"]!.GetValue<string>());
        Assert.True(node["ShowDays"]!.GetValue<bool>());
        Assert.True(node["ShowHours"]!.GetValue<bool>());
        Assert.True(node["ShowMinutes"]!.GetValue<bool>());
        Assert.True(node["ShowSeconds"]!.GetValue<bool>());
        Assert.True(node["ShowProgressBar"]!.GetValue<bool>());
        Assert.False(node["CountdownProgressBarStyle"]!.GetValue<bool>());
        Assert.Equal("keep-me", node["NotMine"]!.GetValue<string>());
    }

    [Fact(DisplayName = "M1B-27 设置贡献：SectionKey=countdown、空页面列表、重置清单两条才不是不可恢复")]
    public void SettingsContribution_IsDeclared()
    {
        var plugin = new CountdownPlugin();

        Assert.Equal("countdown", plugin.SectionKey);
        Assert.Empty(plugin.GetPages());

        var plan = plugin.BuildResetPlan();
        Assert.False(string.IsNullOrWhiteSpace(plan.Summary));
        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.Equal(ResetItemGroup.Reset, item.Group));
    }

    [Fact(DisplayName = "M1B-28 日期非法时 StartAsync 仍返回 Ok（卡片自己显示「日期未设置」），Stop 正常")]
    public async Task StartAsync_InvalidDate_StillOk()
    {
        var section = new JsonObject { ["GaokaoDateStr"] = "不是日期" };
        var (plugin, shell, context) = Create(section);

        await plugin.InitializeAsync(context);
        var result = await plugin.StartAsync();

        Assert.True(result.IsOk);
        Assert.NotEmpty(context.Logger.Warns);

        var card = Assert.IsType<CountdownCard>(Assert.Single(shell.SlotContents).Content);
        Assert.Equal("日期未设置", card.TitleShown);

        await plugin.StopAsync();
    }

    [Fact(DisplayName = "M1B-29 日期合法时 StartAsync 起表、StopAsync 停表，均不抛")]
    public async Task StartThenStop_WithValidDate_DoesNotThrow()
    {
        var (plugin, _, context) = Create();

        await plugin.InitializeAsync(context);
        Assert.True((await plugin.StartAsync()).IsOk);
        await plugin.StopAsync();
    }
}
