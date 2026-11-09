using Avalonia.Controls;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 绑定到某个 owner（插件 id）的 <see cref="IShellService"/>：8 个成员全部带上 owner 再转发。
///
/// 为什么插件拿到的不是 <see cref="ShellService"/> 本身（与 <c>PluginServiceRegistry</c> 同构）：
/// <list type="bullet">
/// <item>「槽位被谁占了」能点名，用户看得懂；</item>
/// <item>插件卸载时按 owner 一次摘干净，不会留下"点了就空引用"的菜单项；</item>
/// <item>插件**删不掉别人的**登记（<see cref="ShellRegistry.RemoveMenu"/> 校验 owner）。</item>
/// </list>
/// 插件感知不到这一层：它只看到 <see cref="IShellService"/>。
/// </summary>
internal sealed class ScopedShell(ShellService shell, string ownerId) : IShellService
{
    /// <inheritdoc />
    public IDisposable RegisterMenuItem(MenuItemDescriptor item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        return shell.AddMenu(ownerId, item);
    }

    /// <inheritdoc />
    public IDisposable RegisterTrayItem(TrayItemDescriptor item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        return shell.AddTray(ownerId, item);
    }

    /// <inheritdoc />
    public Result<IDisposable> RegisterHotKey(HotKeyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
        return shell.AddHotKey(ownerId, descriptor);
    }

    /// <inheritdoc />
    public Result<IDisposable> RegisterSlotContent(string slotId, Control content)
    {
        if (!ShellSlots.IsValid(slotId))
        {
            return Result<IDisposable>.Fail(ErrorCodes.SlotUnknown,
                $"未知槽位「{slotId}」（只能是 {string.Join(" / ", ShellSlots.AllSlots)}）");
        }

        ArgumentNullException.ThrowIfNull(content);
        return shell.AddSlot(ownerId, slotId, content);
    }

    /// <inheritdoc />
    // 参数校验交给 ShellService：它先判宿主再判参数（宿主缺失是更基础的环境错误）。
    public void ShowWindow(Window window) => shell.Show(window);

    /// <inheritdoc />
    public Task<TResult?> ShowDialogAsync<TResult>(Window dialog) => shell.ShowDialogAsync<TResult>(dialog);

    /// <inheritdoc />
    public void PostToUi(Action action) => shell.Post(action);

    /// <inheritdoc />
    public ThemeTokens CurrentTheme => shell.Theme;
}
