using Avalonia.Controls;
using Avalonia.Platform;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.App;

/// <summary>
/// 托盘图标 + 托盘菜单（从 <see cref="ShellHost"/> 拆出来，M1-B）。
///
/// **为什么拆**：拆之前 <see cref="ShellHost"/> 是 251 行，撞了「单类 ≤250」的硬约束。
/// 按 §0.5 的规定，超限要先问"它是不是在兼两件事"，而不是删注释凑数——答案确实是：
/// 主窗/槽位/菜单/通知渲染是"壳内容"，托盘是"另一个 UI 生命周期对象"（它有自己的图标、
/// 自己的菜单树、自己的销毁时机，而且**建不起来时要能独立降级**）。
///
/// 托盘建失败只降级、不抛出：主窗必须照常可用（老师机器上图标资源损坏的概率不低）。
/// </summary>
internal sealed class ShellTray : IDisposable
{
    private readonly TrayIcon? _tray;
    private readonly IPluginLogger _logger;
    private readonly Action _toggleWindow;
    private readonly Action _exit;
    private IReadOnlyList<TrayItemDescriptor> _items = [];

    /// <param name="toggleWindow">"显示 / 隐藏窗口"的落点（与 Ctrl+Shift+H 同一个方法）。</param>
    /// <param name="exit">"退出"的落点（宿主负责销毁自身 + Shutdown）。</param>
    /// <param name="logger">日志。</param>
    public ShellTray(Action toggleWindow, Action exit, IPluginLogger logger)
    {
        ArgumentNullException.ThrowIfNull(toggleWindow);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(logger);

        _toggleWindow = toggleWindow;
        _exit = exit;
        _logger = logger;

        _tray = Create();
        Rebuild();
    }

    /// <summary>托盘项集合变了（插件注册/卸载）：重排后重绘。空列表 = 只剩内置两项。</summary>
    public void Update(IReadOnlyList<TrayItemDescriptor> items)
    {
        _items = items ?? [];
        Rebuild();
    }

    /// <summary>销毁托盘图标。**不隐藏会让系统托盘留一个幽灵图标，直到鼠标划过才消失。**</summary>
    public void Dispose()
    {
        if (_tray is null)
            return;

        try
        {
            _tray.IsVisible = false;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Shell] 隐藏托盘图标失败：{ex.Message}");
        }
    }

    private TrayIcon? Create()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StudyJourneyAvalonia/Assets/icon.ico"));
            return new TrayIcon
            {
                Icon = new WindowIcon(stream),
                ToolTipText = "学程",
                IsVisible = true,
            };
        }
        catch (Exception ex)
        {
            // 降级为"只有主窗"：托盘建不起来绝不能连累主窗启动。
            _logger.Error("[Shell] 托盘图标初始化失败（主窗仍可用）", ex);
            return null;
        }
    }

    private void Rebuild()
    {
        if (_tray is null)
            return;

        var menu = new NativeMenu();

        var toggle = new NativeMenuItem("显示 / 隐藏窗口");
        toggle.Click += (_, _) => InvokeSafely("shell.toggle", _toggleWindow);
        menu.Add(toggle);

        if (_items.Count > 0)
        {
            menu.Add(new NativeMenuItemSeparator());

            foreach (var item in _items)
            {
                var menuItem = new NativeMenuItem(item.Header);
                var captured = item;
                menuItem.Click += (_, _) => InvokeSafely(captured.Id, captured.Invoke);
                menu.Add(menuItem);
            }
        }

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem("退出");
        exit.Click += (_, _) => InvokeSafely("shell.exit", _exit);
        menu.Add(exit);

        _tray.Menu = menu;
    }

    /// <summary>插件回调抛异常不能打死托盘（一次点击失败不该让整个托盘菜单失灵）。</summary>
    private void InvokeSafely(string id, Action invoke)
    {
        try
        {
            invoke();
        }
        catch (Exception ex)
        {
            _logger.Error($"[Shell] 托盘项「{id}」执行失败", ex);
        }
    }
}
