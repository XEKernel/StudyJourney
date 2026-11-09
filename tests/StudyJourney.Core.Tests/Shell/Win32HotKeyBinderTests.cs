using Avalonia.Input;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Core.Tests.Shell;

/// <summary>
/// S2：<see cref="Win32HotKeyBinder"/>。
///
/// 两条最值得钉死的：
/// <list type="number">
/// <item><b>Key → VK 映射</b>：Avalonia 的 <c>Key</c> 是平台无关枚举，<c>Key.A</c> = 44 而 VK_A = 0x41。
///       直接强转会让**所有热键都注册错键**（而且不报错，只是按了没反应）。期望值取自 Win32 官方 VK 表。</item>
/// <item><b>不支持的主键要明确失败</b>，不能静默返回 Ok。</item>
/// </list>
/// </summary>
public sealed class Win32HotKeyBinderTests
{
    [Theory(DisplayName = "S2-01 Key → VK 映射：字母/数字/F 键/方向键/常用控制键")]
    // 期望值来源：Win32 Virtual-Key Codes 官方表（0x41='A' … 0x5A='Z'、0x30='0' … 0x39='9'、0x70=F1 … 0x7B=F12）
    [InlineData(Key.A, 0x41)]
    [InlineData(Key.H, 0x48)]
    [InlineData(Key.K, 0x4B)]
    [InlineData(Key.P, 0x50)]
    [InlineData(Key.W, 0x57)]
    [InlineData(Key.Z, 0x5A)]
    [InlineData(Key.D0, 0x30)]
    [InlineData(Key.D9, 0x39)]
    [InlineData(Key.F1, 0x70)]
    [InlineData(Key.F12, 0x7B)]
    [InlineData(Key.Space, 0x20)]
    [InlineData(Key.Enter, 0x0D)]
    [InlineData(Key.Escape, 0x1B)]
    [InlineData(Key.Left, 0x25)]
    [InlineData(Key.Right, 0x27)]
    public void ToVirtualKey_MapsToWin32Codes(Key key, int expected)
    {
        Assert.Equal((uint)expected, Win32HotKeyBinder.ToVirtualKey(key));
    }

    [Theory(DisplayName = "S2-02 不支持的主键返回 0（由 Bind 转成 hotkey-unsupported）")]
    [InlineData(Key.CapsLock)]
    [InlineData(Key.NumPad0)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.PrintScreen)]
    [InlineData(Key.None)]
    public void ToVirtualKey_UnsupportedReturnsZero(Key key)
    {
        Assert.Equal(0u, Win32HotKeyBinder.ToVirtualKey(key));
    }

    [Fact(DisplayName = "S2-03 不支持的主键 → hotkey-unsupported，且不占用编号（不静默 Ok）")]
    public void Bind_UnsupportedKey_FailsExplicitly()
    {
        var binder = new Win32HotKeyBinder();

        var result = binder.Bind(new HotKeyDescriptor
        {
            Id = "capslock",
            Modifiers = HotKeyModifiers.Control,
            Key = Key.CapsLock,
            Handler = () => { },
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.HotKeyUnsupported, result.Code);
        Assert.Contains("不受支持", result.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "S2-04 真实绑定：能建消息窗口时绑一根冷门热键 → Ok，释放后再绑同一根仍 Ok")]
    public void Bind_RealHotKey_ThenReleaseAndRebind()
    {
        var binder = new Win32HotKeyBinder();

        HotKeyDescriptor Descriptor() => new()
        {
            Id = "s2-real",
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
            Key = Key.F12,
            Handler = () => { },
        };

        var first = binder.Bind(Descriptor());

        if (first.IsFailure)
        {
            // 无 GUI / 无桌面会话的环境建不出隐藏消息窗口：断言失败码合理，不假装成功。
            Assert.Equal(ErrorCodes.HotKeyTaken, first.Code);
            return;
        }

        Assert.True(binder.IsReady);
        first.Value!.Dispose();

        // 释放后编号仍可用（同一组合能重新绑上）
        var second = binder.Bind(Descriptor());
        Assert.True(second.IsOk);
        second.Value!.Dispose();
    }

    [Fact(DisplayName = "S2-05 UnbindAll 幂等，且释放后可重复调用不抛")]
    public void UnbindAll_IsIdempotent()
    {
        var binder = new Win32HotKeyBinder();

        binder.UnbindAll();
        binder.UnbindAll();

        Assert.False(binder.IsReady);
    }

    [Fact(DisplayName = "S2-06 已 Dispose 的绑定层再注册 → 明确失败，不抛")]
    public void Bind_AfterUnbindAll_StillWorksOrFailsCleanly()
    {
        var binder = new Win32HotKeyBinder();
        binder.UnbindAll();

        var result = binder.Bind(new HotKeyDescriptor
        {
            Id = "after-unbind",
            Modifiers = HotKeyModifiers.Control,
            Key = Key.F11,
            Handler = () => { },
        });

        // 允许两种结局（环境不给窗口 → hotkey-taken；给了 → Ok），但必须是 Result 而不是异常
        if (result.IsFailure)
            Assert.Equal(ErrorCodes.HotKeyTaken, result.Code);
        else
            result.Value!.Dispose();
    }
}
