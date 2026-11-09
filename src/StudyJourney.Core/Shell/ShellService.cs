using Avalonia.Controls;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 壳的门面（CORE_ARCHITECTURE §2.5）：把"插件想往壳上挂东西"翻译成**登记 + 通知宿主**。
///
/// 三个刻意的设计：
/// <list type="number">
/// <item><b>本身不是 <see cref="IShellService"/></b>：插件拿到的是 <see cref="ForPlugin"/> 返回的
///       绑定实例（owner = 插件 id）。这样"槽位被谁占了"能点名，"卸载时清干净"能自动——
///       与 <c>PluginServiceRegistry</c> 门面同构。若把本类直接给插件，登记就全记在一个 owner 名下。</item>
/// <item><b>宿主可以晚到</b>：插件在 Avalonia 启动前就加载完了（M0 的时序），那时没有窗口。
///       登记照收，<see cref="AttachHost"/> 时一次性重放菜单/托盘/槽位，<c>PostToUi</c> 排队。</item>
/// <item><b>失败语义分层</b>：菜单/托盘 Id 重复 → 抛（编程错误）；槽位占用 / 热键冲突 → <see cref="Result"/>
///       （运行期可预期，必须给人话提示）。</item>
/// </list>
/// </summary>
public sealed class ShellService : IDisposable
{
    /// <summary>
    /// 热键上限。v3.3.0 用了 7 个固定编号（1–7），这里留到 8 给 M4 的听力播放器。
    /// 超了返回 <c>hotkey-limit</c>，不静默丢弃。
    /// </summary>
    public const int MaxHotKeys = 8;

    private readonly ShellRegistry _registry = new();
    private readonly IHotKeyBinder? _binder;
    private readonly IPluginLogger? _logger;
    private readonly object _gate = new();
    private readonly List<Action> _pendingUiActions = [];
    private IUiHost? _host;

    /// <param name="binder">热键绑定层；<c>null</c> = 只做表内冲突判定（测试 / 无 GUI 环境）。</param>
    /// <param name="logger">日志；<c>null</c> = 不记。</param>
    public ShellService(IHotKeyBinder? binder = null, IPluginLogger? logger = null)
    {
        _binder = binder;
        _logger = logger;
    }

    /// <summary>宿主是否已挂上（诊断 / smoke 打印用）。</summary>
    public bool HasHost
    {
        get { lock (_gate) return _host is not null; }
    }

    /// <summary>
    /// 挂上宿主并**重放**已登记的全部内容：菜单 → 托盘 → 槽位 → 排队的 UI 动作。
    /// 运行期重复挂（换宿主）也安全：全部内容按当前登记重推一遍。
    /// </summary>
    public void AttachHost(IUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        List<Action> pending;
        lock (_gate)
        {
            _host = host;
            pending = [.. _pendingUiActions];
            _pendingUiActions.Clear();
        }

        host.OnMenuItemsChanged(_registry.MenuSnapshot());
        host.OnTrayItemsChanged(_registry.TraySnapshot());

        foreach (var (slotId, content) in _registry.SlotSnapshot())
            host.OnSlotContentChanged(slotId, content);

        foreach (var action in pending)
            host.PostToUi(action);

        _logger?.Info($"[Shell] 宿主已挂上：{_registry.MenuSnapshot().Count} 菜单 / " +
                      $"{_registry.TraySnapshot().Count} 托盘项 / {_registry.SlotSnapshot().Count} 槽位 / {pending.Count} 个待执行 UI 动作");
    }

    /// <summary>给某个插件（或核心自己）一个绑定到该 owner 的壳接口。</summary>
    public IShellService ForPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new ScopedShell(this, pluginId);
    }

    /// <summary>摘掉某插件的全部壳登记（卸载时由加载器调用），并通知宿主隐藏对应块。</summary>
    public void RemoveAllOf(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var cleanup = _registry.RemoveAllOf(pluginId);

        foreach (var handle in cleanup.HotKeyHandles)
        {
            try { handle.Dispose(); }
            catch (Exception ex) { _logger?.Warn($"[Shell] 解除 {pluginId} 的热键绑定时失败：{ex.Message}"); }
        }

        if (Host is not { } host)
            return;   // 宿主未挂：登记已清，AttachHost 时自然不会重放

        host.OnMenuItemsChanged(_registry.MenuSnapshot());
        host.OnTrayItemsChanged(_registry.TraySnapshot());

        foreach (var slotId in cleanup.SlotIds)
            host.OnSlotContentChanged(slotId, null);

        _logger?.Info($"[Shell] 已摘除 {pluginId} 的壳登记：{cleanup.SlotIds.Count} 个槽位 / {cleanup.HotKeyHandles.Count} 个热键");
    }

    /// <summary>退出时解除全部热键绑定（幂等）。</summary>
    public void Dispose() => _binder?.UnbindAll();

    // ── 以下是 ScopedShell 的内部入口 ───────────────────────────────────

    internal IDisposable AddMenu(string owner, MenuItemDescriptor item)
    {
        if (!_registry.TryAddMenu(owner, item))
            throw new ArgumentException($"菜单项 Id「{item.Id}」已存在（§2.5：Id 重复属编程错误）", nameof(item));

        Host?.OnMenuItemsChanged(_registry.MenuSnapshot());

        return new Registration(() =>
        {
            if (!_registry.RemoveMenu(owner, item.Id))
                return;

            Host?.OnMenuItemsChanged(_registry.MenuSnapshot());
        });
    }

    internal IDisposable AddTray(string owner, TrayItemDescriptor item)
    {
        if (!_registry.TryAddTray(owner, item))
            throw new ArgumentException($"托盘项 Id「{item.Id}」已存在（§2.5：Id 重复属编程错误）", nameof(item));

        Host?.OnTrayItemsChanged(_registry.TraySnapshot());

        return new Registration(() =>
        {
            if (!_registry.RemoveTray(owner, item.Id))
                return;

            Host?.OnTrayItemsChanged(_registry.TraySnapshot());
        });
    }

    internal Result<IDisposable> AddSlot(string owner, string slotId, Control content)
    {
        var added = _registry.TryAddSlot(owner, slotId, content);
        if (added.IsFailure)
            return Result<IDisposable>.Fail(added.Code, added.Message, added.Exception);

        Host?.OnSlotContentChanged(slotId, content);

        return Result<IDisposable>.Ok(new Registration(() =>
        {
            if (!_registry.RemoveSlot(owner, slotId))
                return;

            Host?.OnSlotContentChanged(slotId, null);
        }));
    }

    internal Result<IDisposable> AddHotKey(string owner, HotKeyDescriptor descriptor)
    {
        if (_registry.HotKeyCount >= MaxHotKeys)
        {
            return Result<IDisposable>.Fail(ErrorCodes.HotKeyLimit,
                $"热键数量已达上限 {MaxHotKeys} 个，无法注册「{descriptor.Id}」");
        }

        if (_registry.HasHotKey(descriptor.Id))
        {
            return Result<IDisposable>.Fail(ErrorCodes.HotKeyTaken,
                $"热键「{descriptor.Id}」已被注册（同一 Id 不允许重复）");
        }

        Result<IDisposable>? bound = _binder?.Bind(descriptor);
        if (bound is { IsFailure: true } failure)
        {
            // A1-4：冲突必须让人看见，不静默失败。
            _logger?.Warn($"[Shell] 热键「{descriptor.Id}」注册失败：[{failure.Code}] {failure.Message}");
            return Result<IDisposable>.Fail(failure.Code, failure.Message, failure.Exception);
        }

        _registry.AddHotKey(owner, descriptor.Id, descriptor, bound?.Value);

        return Result<IDisposable>.Ok(new Registration(() =>
        {
            _registry.RemoveHotKey(owner, descriptor.Id)?.Dispose();
        }));
    }

    // 先判宿主、再判参数：宿主缺失是更基础的环境错误，报它比报 ArgumentNullException 有用。
    internal void Show(Window window)
    {
        var host = RequireHost();
        ArgumentNullException.ThrowIfNull(window);
        host.ShowWindow(window);
    }

    internal Task<TResult?> ShowDialogAsync<TResult>(Window dialog)
    {
        var host = RequireHost();
        ArgumentNullException.ThrowIfNull(dialog);
        return host.ShowDialogAsync<TResult>(dialog);
    }

    internal void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Host is { } host)
        {
            host.PostToUi(action);
            return;
        }

        // 宿主还没挂上（插件在 Avalonia 启动前加载）：排队，AttachHost 时补投。
        lock (_gate)
            _pendingUiActions.Add(action);
    }

    internal ThemeTokens Theme => Host?.CurrentTheme ?? ShellDefaults.DefaultDark;

    // ── 内部工具 ───────────────────────────────────────────────────────

    private IUiHost? Host
    {
        get { lock (_gate) return _host; }
    }

    private IUiHost RequireHost()
        => Host ?? throw new NotSupportedException(
            "壳宿主尚未挂上：ShowWindow / ShowDialogAsync 需要窗口系统，只能等 Avalonia 初始化完成后调用");

    private sealed class Registration(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
