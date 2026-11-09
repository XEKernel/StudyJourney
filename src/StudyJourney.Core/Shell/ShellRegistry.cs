using Avalonia.Controls;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Core.Shell;

/// <summary>
/// 壳的四张登记表的**唯一持有者**：菜单 / 托盘 / 槽位 / 热键。
///
/// 每条登记都带 <c>Owner</c>（插件 id 或 <see cref="CoreOwner"/>），插件卸载时
/// <see cref="RemoveAllOf"/> 一次清干净——与 <c>ServiceRegistry.RemoveAllOf</c> 同构，
/// 否则会出现"插件卸载了、菜单项还在、点了报空引用"。
///
/// 本类**只做数据与冲突判定**，不碰 UI：宿主回调由 <see cref="ShellService"/> 在锁外触发。
/// 这样"槽位占用 / 热键冲突"能在无 GUI 的测试里断言。
/// </summary>
internal sealed class ShellRegistry
{
    /// <summary>核心自身的 owner 名（与 <c>ServiceRegistry.CoreOwner</c> 同值，插件 id 不允许叫它）。</summary>
    public const string CoreOwner = "core";

    private readonly object _gate = new();
    private readonly Dictionary<string, Owned<MenuItemDescriptor>> _menu = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Owned<TrayItemDescriptor>> _tray = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Owned<Control>> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Owned<HotKeyBinding>> _hotKeys = new(StringComparer.Ordinal);

    // ── 菜单 ────────────────────────────────────────────────────────────

    /// <summary>登记菜单项；Id 已存在返回 <c>false</c>（调用方转成 ArgumentException）。</summary>
    public bool TryAddMenu(string owner, MenuItemDescriptor item)
    {
        lock (_gate)
            return _menu.TryAdd(item.Id, new Owned<MenuItemDescriptor>(owner, item));
    }

    /// <summary>移除菜单项；**只有 owner 匹配才删**（插件不能摘别人的项）。</summary>
    public bool RemoveMenu(string owner, string id) => RemoveOwned(_menu, owner, id);

    /// <summary>按 Order 升序、Id 字典序兜底（排序稳定，宿主直接渲染）。</summary>
    public IReadOnlyList<MenuItemDescriptor> MenuSnapshot()
    {
        lock (_gate)
            return _menu.Values
                .OrderBy(e => e.Value.Order).ThenBy(e => e.Value.Id, StringComparer.Ordinal)
                .Select(e => e.Value).ToList();
    }

    // ── 托盘 ────────────────────────────────────────────────────────────

    public bool TryAddTray(string owner, TrayItemDescriptor item)
    {
        lock (_gate)
            return _tray.TryAdd(item.Id, new Owned<TrayItemDescriptor>(owner, item));
    }

    public bool RemoveTray(string owner, string id) => RemoveOwned(_tray, owner, id);

    public IReadOnlyList<TrayItemDescriptor> TraySnapshot()
    {
        lock (_gate)
            return _tray.Values
                .OrderBy(e => e.Value.Order).ThenBy(e => e.Value.Id, StringComparer.Ordinal)
                .Select(e => e.Value).ToList();
    }

    // ── 槽位 ────────────────────────────────────────────────────────────

    /// <summary>占一个槽位；已占用返回带占用者名字的失败（一槽一内容，绝不叠放）。</summary>
    public Result TryAddSlot(string owner, string slotId, Control content)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(slotId, out var existing))
            {
                return Result.Fail(ErrorCodes.SlotOccupied,
                    $"槽位 {slotId} 已被「{existing.Owner}」占用（一槽一内容，不允许叠放）");
            }

            _slots[slotId] = new Owned<Control>(owner, content);
            return Result.Ok();
        }
    }

    public bool RemoveSlot(string owner, string slotId) => RemoveOwned(_slots, owner, slotId);

    /// <summary>某个槽位当前的内容；未占用为 <c>null</c>。</summary>
    public Control? SlotContent(string slotId)
    {
        lock (_gate)
            return _slots.TryGetValue(slotId, out var entry) ? entry.Value : null;
    }

    /// <summary>全部已占槽位的快照（宿主 <c>AttachHost</c> 时重放用）。</summary>
    public IReadOnlyList<(string SlotId, Control Content)> SlotSnapshot()
    {
        lock (_gate)
            return _slots.Select(pair => (pair.Key, pair.Value.Value)).ToList();
    }

    // ── 热键 ────────────────────────────────────────────────────────────

    /// <summary>热键 Id 是否已注册（同 Id 重复注册视为占用）。</summary>
    public bool HasHotKey(string id)
    {
        lock (_gate)
            return _hotKeys.ContainsKey(id);
    }

    /// <summary>当前热键数量（用于 <c>hotkey-limit</c>）。</summary>
    public int HotKeyCount
    {
        get { lock (_gate) return _hotKeys.Count; }
    }

    public void AddHotKey(string owner, string id, HotKeyDescriptor descriptor, IDisposable? handle)
    {
        lock (_gate)
            _hotKeys[id] = new Owned<HotKeyBinding>(owner, new HotKeyBinding(descriptor, handle));
    }

    /// <summary>移除并返回需要调用方释放的绑定句柄。</summary>
    public IDisposable? RemoveHotKey(string owner, string id)
    {
        lock (_gate)
        {
            if (!_hotKeys.TryGetValue(id, out var entry) || !OwnerMatches(entry, owner))
                return null;

            _hotKeys.Remove(id);
            return entry.Value.Handle;
        }
    }

    // ── 按 owner 批量清理 ───────────────────────────────────────────────

    /// <summary>
    /// 摘掉某 owner 的全部登记，返回"需要宿主跟着清掉的东西"：
    /// 被释放的槽位 ID（宿主应隐藏那几块）与热键句柄（调用方负责 Unbind）。
    /// </summary>
    public ShellCleanup RemoveAllOf(string owner)
    {
        lock (_gate)
        {
            var slotIds = _slots.Where(p => OwnerMatches(p.Value, owner)).Select(p => p.Key).ToList();
            foreach (var id in slotIds)
                _slots.Remove(id);

            var handles = new List<IDisposable>();

            foreach (var id in _hotKeys.Where(p => OwnerMatches(p.Value, owner)).Select(p => p.Key).ToList())
            {
                if (_hotKeys[id].Value.Handle is { } handle)
                    handles.Add(handle);
                _hotKeys.Remove(id);
            }

            foreach (var id in _menu.Where(p => OwnerMatches(p.Value, owner)).Select(p => p.Key).ToList())
                _menu.Remove(id);

            foreach (var id in _tray.Where(p => OwnerMatches(p.Value, owner)).Select(p => p.Key).ToList())
                _tray.Remove(id);

            return new ShellCleanup(slotIds, handles);
        }
    }

    private bool RemoveOwned<T>(Dictionary<string, Owned<T>> table, string owner, string id)
    {
        lock (_gate)
        {
            if (!table.TryGetValue(id, out var entry) || !OwnerMatches(entry, owner))
                return false;

            return table.Remove(id);
        }
    }

    private static bool OwnerMatches<T>(Owned<T> entry, string owner)
        => string.Equals(entry.Owner, owner, StringComparison.Ordinal);

    /// <summary>带归属的一条登记。</summary>
    internal sealed record Owned<T>(string Owner, T Value);

    /// <summary>热键登记：描述符 + 真实绑定句柄（<c>null</c> = 未启用绑定层，仅记表）。</summary>
    internal sealed record HotKeyBinding(HotKeyDescriptor Descriptor, IDisposable? Handle);
}

/// <summary>按 owner 清理后需要宿主跟进的动作。</summary>
internal sealed record ShellCleanup(IReadOnlyList<string> SlotIds, IReadOnlyList<IDisposable> HotKeyHandles);
