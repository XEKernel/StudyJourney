using Avalonia.Controls;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Tests.Shell;

/// <summary>记录宿主回调的替身：不建窗口、不碰 UI 线程。</summary>
internal sealed class RecordingUiHost : IUiHost
{
    public List<IReadOnlyList<MenuItemDescriptor>> MenuSnapshots { get; } = [];

    public List<IReadOnlyList<TrayItemDescriptor>> TraySnapshots { get; } = [];

    public List<(string SlotId, Control? Content)> SlotChanges { get; } = [];

    public List<Action> Posted { get; } = [];

    public List<string> Events { get; } = [];

    public ThemeTokens Theme { get; set; } = ShellDefaults.DefaultDark;

    public ThemeTokens CurrentTheme => Theme;

    public void OnMenuItemsChanged(IReadOnlyList<MenuItemDescriptor> items)
    {
        MenuSnapshots.Add(items);
        Events.Add("menu:" + string.Join(",", items.Select(i => i.Id)));
    }

    public void OnTrayItemsChanged(IReadOnlyList<TrayItemDescriptor> items)
    {
        TraySnapshots.Add(items);
        Events.Add("tray:" + string.Join(",", items.Select(i => i.Id)));
    }

    public void OnSlotContentChanged(string slotId, Control? content)
    {
        SlotChanges.Add((slotId, content));
        Events.Add($"slot:{slotId}={(content is null ? "null" : content.GetType().Name)}");
    }

    public void ShowWindow(Window window) => Events.Add("show:" + window.GetType().Name);

    public Task<TResult?> ShowDialogAsync<TResult>(Window dialog) => Task.FromResult<TResult?>(default);

    public void PostToUi(Action action)
    {
        Posted.Add(action);
        action();
    }

    public IReadOnlyList<string> LastMenuIds => MenuSnapshots.Count == 0 ? [] : MenuSnapshots[^1].Select(i => i.Id).ToList();
}

/// <summary>热键绑定层替身：可以按组合设定"已被占用"。</summary>
internal sealed class FakeHotKeyBinder : IHotKeyBinder
{
    private readonly HashSet<string> _taken;

    /// <param name="takenIds">这些 <c>Id</c> 的绑定会失败（模拟被别的程序占用）。</param>
    public FakeHotKeyBinder(params string[] takenIds) => _taken = [.. takenIds];

    public List<HotKeyDescriptor> Bound { get; } = [];

    public List<string> Unbound { get; } = [];

    public bool UnbindAllCalled { get; private set; }

    public Result<IDisposable> Bind(HotKeyDescriptor descriptor)
    {
        if (_taken.Contains(descriptor.Id))
            return Result<IDisposable>.Fail(ErrorCodes.HotKeyTaken, $"组合被其它程序占用（{descriptor.Id}）");

        Bound.Add(descriptor);
        return Result<IDisposable>.Ok(new Handle(() => Unbound.Add(descriptor.Id)));
    }

    public void UnbindAll()
    {
        UnbindAllCalled = true;
        Unbound.AddRange(Bound.Select(b => b.Id));
    }

    private sealed class Handle(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
