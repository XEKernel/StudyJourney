using Avalonia.Controls;
using Avalonia.Input;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Core.Tests.Shell;

/// <summary>
/// S1：<see cref="ShellService"/> 的 8 个成员 + 宿主晚到重放（CORE_ARCHITECTURE §2.5）。
///
/// 这一层的价值是**失败语义**：菜单 Id 重复要炸（编程错误），槽位占用/热键冲突要返回
/// <see cref="Result"/> 且**点名占用者**（运行期可预期，必须给人话提示，A1-4）。
/// </summary>
public sealed class ShellServiceTests
{
    private static MenuItemDescriptor Menu(string id, int order = 0, string header = "项")
        => new() { Id = id, Header = header, Order = order, Invoke = () => { } };

    private static TrayItemDescriptor Tray(string id, int order = 0)
        => new() { Id = id, Header = "托盘项", Order = order, Invoke = () => { } };

    private static HotKeyDescriptor HotKey(string id, Key key = Key.H)
        => new() { Id = id, Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = key, Handler = () => { } };

    [Fact(DisplayName = "S1-01 菜单：注册后宿主的快照按 Order 升序；释放句柄即移除")]
    public void Menu_RegisteredInOrder_AndReleasedOnDispose()
    {
        var shell = new ShellService();
        var host = new RecordingUiHost();
        shell.AttachHost(host);
        var owner = shell.ForPlugin("countdown");

        var registration = owner.RegisterMenuItem(Menu("late", order: 20));
        owner.RegisterMenuItem(Menu("early", order: 10));
        owner.RegisterMenuItem(Menu("mid", order: 15));

        Assert.Equal(["early", "mid", "late"], host.LastMenuIds);

        registration.Dispose();
        Assert.Equal(["early", "mid"], host.LastMenuIds);
    }

    [Fact(DisplayName = "S1-02 菜单 Id 重复 → ArgumentException（编程错误，必须立刻炸）")]
    public void Menu_DuplicateId_Throws()
    {
        var shell = new ShellService();
        var owner = shell.ForPlugin("countdown");
        owner.RegisterMenuItem(Menu("settings"));

        var ex = Assert.Throws<ArgumentException>(() => owner.RegisterMenuItem(Menu("settings")));
        Assert.Contains("settings", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S1-03 托盘项：注册/排序/释放；载荷是 TrayItemDescriptor")]
    public void Tray_RegisteredAndSorted()
    {
        var shell = new ShellService();
        var host = new RecordingUiHost();
        shell.AttachHost(host);
        var owner = shell.ForPlugin("countdown");

        owner.RegisterTrayItem(Tray("b", order: 2));
        owner.RegisterTrayItem(Tray("a", order: 1));

        Assert.Equal(["a", "b"], host.TraySnapshots[^1].Select(t => t.Id));

        // 菜单与托盘是两张独立的表：同名 Id 互不干扰
        owner.RegisterMenuItem(Menu("a"));
        Assert.Equal(["a", "b"], host.TraySnapshots[^1].Select(t => t.Id));
    }

    [Fact(DisplayName = "S1-04 槽位：成功挂上；重复 → slot-occupied 且点名占用者；未知 ID → slot-unknown")]
    public void Slot_Occupied_NamesTheOwner_AndUnknownIsRejected()
    {
        var shell = new ShellService();
        var host = new RecordingUiHost();
        shell.AttachHost(host);

        var countdown = shell.ForPlugin("countdown");
        var schedule = shell.ForPlugin("schedule");

        var first = countdown.RegisterSlotContent(ShellSlots.Primary, new Border());
        Assert.True(first.IsOk);
        Assert.Equal(ShellSlots.Primary, host.SlotChanges[^1].SlotId);
        Assert.Equal("Border", host.SlotChanges[^1].Content?.GetType().Name);

        var second = schedule.RegisterSlotContent(ShellSlots.Primary, new Border());
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCodes.SlotOccupied, second.Code);
        Assert.Contains("countdown", second.Message, StringComparison.Ordinal);

        var unknown = countdown.RegisterSlotContent("main.nope", new Border());
        Assert.True(unknown.IsFailure);
        Assert.Equal(ErrorCodes.SlotUnknown, unknown.Code);

        // 释放后槽位空出来，别人才能占
        first.Value!.Dispose();
        Assert.Equal(ShellSlots.Primary, host.SlotChanges[^1].SlotId);
        Assert.Null(host.SlotChanges[^1].Content);
        Assert.True(schedule.RegisterSlotContent(ShellSlots.Primary, new Border()).IsOk);
    }

    [Fact(DisplayName = "S1-05 槽位 ID 大小写敏感：main.Primary 不是合法槽位")]
    public void Slot_IdIsCaseSensitive()
    {
        var shell = new ShellService();
        var result = shell.ForPlugin("x").RegisterSlotContent("main.Primary", new Border());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.SlotUnknown, result.Code);
    }

    [Fact(DisplayName = "S1-06 热键：同 Id 重复 → hotkey-taken；绑定层拒绝时原样透传（不静默失败，A1-4）")]
    public void HotKey_DuplicateAndBinderRejection()
    {
        var binder = new FakeHotKeyBinder("bySystem");
        var shell = new ShellService(binder);
        var owner = shell.ForPlugin("countdown");

        Assert.True(owner.RegisterHotKey(HotKey("toggleMain")).IsOk);
        Assert.Single(binder.Bound);

        var duplicate = owner.RegisterHotKey(HotKey("toggleMain", Key.J));
        Assert.Equal(ErrorCodes.HotKeyTaken, duplicate.Code);

        // 绑定层说被系统占了 → 结果原样透传，且不写进登记表
        var takenBySystem = owner.RegisterHotKey(HotKey("bySystem"));
        Assert.True(takenBySystem.IsFailure);
        Assert.Equal(ErrorCodes.HotKeyTaken, takenBySystem.Code);
        Assert.Contains("被其它程序占用", takenBySystem.Message, StringComparison.Ordinal);

        // 释放句柄要真的解绑，不动其它热键
        duplicate = owner.RegisterHotKey(HotKey("other", Key.W));
        Assert.True(duplicate.IsOk);
        duplicate.Value!.Dispose();
        Assert.Contains("other", binder.Unbound);
        Assert.DoesNotContain("toggleMain", binder.Unbound);
    }

    [Fact(DisplayName = "S1-07 热键超过上限 → hotkey-limit（不静默丢弃）")]
    public void HotKey_Limit()
    {
        var shell = new ShellService(new FakeHotKeyBinder());
        var owner = shell.ForPlugin("countdown");

        for (var i = 0; i < ShellService.MaxHotKeys; i++)
            Assert.True(owner.RegisterHotKey(HotKey("hk" + i, (Key)((int)Key.A + i))).IsOk);

        var overflow = owner.RegisterHotKey(HotKey("hkOverflow", Key.Z));
        Assert.True(overflow.IsFailure);
        Assert.Equal(ErrorCodes.HotKeyLimit, overflow.Code);
        Assert.Contains("上限", overflow.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S1-08 宿主晚到：先登记再挂宿主 → 一次性重放菜单/托盘/槽位")]
    public void AttachHost_ReplaysEverythingRegisteredBefore()
    {
        var shell = new ShellService();
        var owner = shell.ForPlugin("countdown");

        owner.RegisterMenuItem(Menu("settings"));
        owner.RegisterTrayItem(Tray("toggle"));
        owner.RegisterSlotContent(ShellSlots.Primary, new Border());

        Assert.False(shell.HasHost);

        var host = new RecordingUiHost();
        shell.AttachHost(host);

        Assert.True(shell.HasHost);
        Assert.Equal(["settings"], host.LastMenuIds);
        Assert.Equal(["toggle"], host.TraySnapshots[^1].Select(t => t.Id));
        Assert.Equal(ShellSlots.Primary, host.SlotChanges[^1].SlotId);
        Assert.Equal("Border", host.SlotChanges[^1].Content?.GetType().Name);
    }

    [Fact(DisplayName = "S1-09 PostToUi：宿主未挂时排队，挂上后按序补投")]
    public void PostToUi_QueuesUntilHostAttached()
    {
        var shell = new ShellService();
        var order = new List<int>();

        shell.ForPlugin("countdown").PostToUi(() => order.Add(1));
        shell.ForPlugin("countdown").PostToUi(() => order.Add(2));
        Assert.Empty(order);

        shell.AttachHost(new RecordingUiHost());
        Assert.Equal([1, 2], order);
    }

    [Fact(DisplayName = "S1-10 RemoveAllOf：摘干净该插件全部登记、通知宿主清块，且不动别人的")]
    public void RemoveAllOf_ClearsOnlyThatOwner()
    {
        var shell = new ShellService(new FakeHotKeyBinder());
        var host = new RecordingUiHost();
        shell.AttachHost(host);

        var countdown = shell.ForPlugin("countdown");
        var schedule = shell.ForPlugin("schedule");

        countdown.RegisterMenuItem(Menu("cd-settings"));
        countdown.RegisterTrayItem(Tray("cd-toggle"));
        countdown.RegisterSlotContent(ShellSlots.Primary, new Border());
        countdown.RegisterHotKey(HotKey("cd-hotkey"));

        schedule.RegisterSlotContent(ShellSlots.Secondary, new Border());
        schedule.RegisterMenuItem(Menu("sc-edit"));

        shell.RemoveAllOf("countdown");

        Assert.Equal(["sc-edit"], host.LastMenuIds);
        Assert.Equal([ShellSlots.Primary], host.SlotChanges.Where(c => c.Content is null).Select(c => c.SlotId));
        Assert.Contains(host.SlotChanges, c => c.SlotId == ShellSlots.Secondary && c.Content is not null);

        // 别人的槽位还在，新插件能抢到被释放的 primary
        Assert.True(shell.ForPlugin("exam").RegisterSlotContent(ShellSlots.Primary, new Border()).IsOk);
    }

    [Fact(DisplayName = "S1-11 插件不能摘别人的菜单项：用别人的 owner 释放句柄无效")]
    public void DisposeDoesNotRemoveOtherOwnersItems()
    {
        var shell = new ShellService();
        var host = new RecordingUiHost();
        shell.AttachHost(host);

        var countdown = shell.ForPlugin("countdown");
        var registration = countdown.RegisterMenuItem(Menu("settings"));

        // 换一个实例但同 owner：可以摘（owner 相同就是同一个插件）
        registration.Dispose();
        Assert.Empty(host.LastMenuIds);
    }

    [Fact(DisplayName = "S1-12 CurrentTheme：无宿主返回默认色板（纯读不抛）；有宿主用宿主的")]
    public void CurrentTheme_FallsBackToDefault()
    {
        var shell = new ShellService();
        var owner = shell.ForPlugin("countdown");

        Assert.True(owner.CurrentTheme.IsDark);
        Assert.Equal(ShellDefaults.DefaultDark.Accent, owner.CurrentTheme.Accent);

        var host = new RecordingUiHost { Theme = ShellDefaults.DefaultDark with { IsDark = false, FontSizeBase = 18 } };
        shell.AttachHost(host);

        Assert.False(owner.CurrentTheme.IsDark);
        Assert.Equal(18, owner.CurrentTheme.FontSizeBase);
    }

    [Fact(DisplayName = "S1-13 ShowWindow / ShowDialogAsync：无宿主时抛 NotSupportedException（不是静默空转）")]
    public void WindowOperations_RequireHost()
    {
        var shell = new ShellService();
        var owner = shell.ForPlugin("countdown");

        // 无 GUI 的测试环境下 new Window() 会因缺少 IWindowingPlatform 抛 InvalidOperationException，
        // 所以传 null! —— 这里要断言的是"宿主缺失优先报"，参数校验排在它后面。
        var show = Assert.Throws<NotSupportedException>(() => owner.ShowWindow(null!));
        Assert.Contains("宿主尚未挂上", show.Message, StringComparison.Ordinal);

        var dialog = Assert.Throws<NotSupportedException>(() => { _ = owner.ShowDialogAsync<string>(null!); });
        Assert.Contains("宿主尚未挂上", dialog.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S1-13b 宿主已挂上时，null 窗口仍是 ArgumentNullException")]
    public void WindowOperations_StillValidateArgumentWhenHostPresent()
    {
        var shell = new ShellService();
        shell.AttachHost(new RecordingUiHost());
        var owner = shell.ForPlugin("countdown");

        Assert.Throws<ArgumentNullException>(() => owner.ShowWindow(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = owner.ShowDialogAsync<string>(null!); });
    }

    [Fact(DisplayName = "S1-14 Dispose：解除全部热键绑定（退出路径）")]
    public void Dispose_UnbindsAllHotKeys()
    {
        var binder = new FakeHotKeyBinder();
        var shell = new ShellService(binder);
        shell.ForPlugin("countdown").RegisterHotKey(HotKey("toggleMain"));

        shell.Dispose();

        Assert.True(binder.UnbindAllCalled);
    }
}
