using Avalonia.Controls;
using Avalonia.Media;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>
/// 手写壳替身（工程里没有 Moq，也不打算为一条断言引入 mock 库）。
///
/// ⚠ <see cref="PostToUi"/> **同步执行**而不是排队：插件把"建卡片"投递进来，
/// 若这里也排队，测试里卡片永远不会建，槽位断言就永远是空的。
/// </summary>
internal sealed class FakeShellService : IShellService
{
    internal List<MenuItemDescriptor> MenuItems { get; } = [];

    internal List<TrayItemDescriptor> TrayItems { get; } = [];

    internal List<HotKeyDescriptor> HotKeys { get; } = [];

    internal List<(string SlotId, Control Content)> SlotContents { get; } = [];

    internal List<Window> ShownWindows { get; } = [];

    internal int PostToUiCount { get; private set; }

    internal int DisposedHandles { get; private set; }

    public ThemeTokens CurrentTheme { get; } = new()
    {
        Accent = Color.Parse("#2B6CB0"),
        Surface = Color.Parse("#FFFFFFFF"),
        SurfaceAlt = Color.Parse("#FFF1F3F5"),
        TextPrimary = Color.Parse("#FF1F2328"),
        TextSecondary = Color.Parse("#FF4A5568"),
        TextDim = Color.Parse("#FF8A94A6"),
        Danger = Color.Parse("#FFC53030"),
        Warning = Color.Parse("#FFB7791F"),
        Success = Color.Parse("#FF2F855A"),
        FontFamily = "Microsoft YaHei UI",
        FontSizeBase = 16,
        IsDark = false,
    };

    public IDisposable RegisterMenuItem(MenuItemDescriptor item)
    {
        MenuItems.Add(item);
        return new Handle(this);
    }

    public IDisposable RegisterTrayItem(TrayItemDescriptor item)
    {
        TrayItems.Add(item);
        return new Handle(this);
    }

    public Result<IDisposable> RegisterHotKey(HotKeyDescriptor descriptor)
    {
        HotKeys.Add(descriptor);
        return Result<IDisposable>.Ok(new Handle(this));
    }

    public Result<IDisposable> RegisterSlotContent(string slotId, Control content)
    {
        SlotContents.Add((slotId, content));
        return Result<IDisposable>.Ok(new Handle(this));
    }

    public void ShowWindow(Window window) => ShownWindows.Add(window);

    public Task<TResult?> ShowDialogAsync<TResult>(Window dialog) => Task.FromResult<TResult?>(default);

    /// <summary>直接同步执行（见类注释）。</summary>
    public void PostToUi(Action action)
    {
        PostToUiCount++;
        action();
    }

    /// <summary>注册句柄替身：只记"被释放过几次"，供 StopAsync 幂等断言。</summary>
    private sealed class Handle(FakeShellService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.DisposedHandles++;
        }
    }
}
