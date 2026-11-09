using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using StudyJourney.Core.Notifications;
using StudyJourney.Core.Shell;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.App;

/// <summary>
/// 宿主层的壳实现（<see cref="IUiHost"/>）：**窗口、托盘、对话框、主题**归这里，
/// 注册表与失败语义留在 Core 的 <see cref="ShellService"/>（CORE_ARCHITECTURE §1.3）。
///
/// 判据照抄 §1.3：需要知道"窗口在哪"的代码属于 App。托盘图标、主窗槽位、ContextMenu
/// 全部符合；热键（隐藏窗口 + Win32）不符合，所以它在 Core。
/// </summary>
public sealed class ShellHost : IUiHost, INotificationRenderer, IDisposable
{
    private readonly MainWindow _window;
    private readonly IPluginLogger _logger;
    private readonly TrayIcon? _tray;
    private IReadOnlyList<TrayItemDescriptor> _trayItems = [];

    public ShellHost(MainWindow window, IPluginLogger logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(logger);

        _window = window;
        _logger = logger;
        _tray = CreateTray();
        RebuildTrayMenu();
        OnSlotContentChanged(ShellSlots.Primary, null);   // 主区占位（没有插件挂内容时也不空白）
        FillFooter();
    }

    /// <summary>当前色板。M1-A 固定暗色；明暗切换与资源字典属 M1 后续（§0.6 第二刀）。</summary>
    public ThemeTokens CurrentTheme => ShellDefaults.DefaultDark;

    // ── IUiHost ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void OnMenuItemsChanged(IReadOnlyList<MenuItemDescriptor> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            _window.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();

        foreach (var item in items)
        {
            var menuItem = new MenuItem { Header = item.Header };
            var captured = item;
            menuItem.Click += (_, _) => InvokeSafely(captured.Id, captured.Invoke);
            menu.Items.Add(menuItem);
        }

        _window.ContextMenu = menu;
    }

    /// <inheritdoc />
    public void OnTrayItemsChanged(IReadOnlyList<TrayItemDescriptor> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _trayItems = items;
        RebuildTrayMenu();
    }

    /// <inheritdoc />
    public void OnSlotContentChanged(string slotId, Control? content)
    {
        var host = _window.SlotHost(slotId);
        if (host is null)
        {
            _logger.Warn($"[Shell] 未知槽位「{slotId}」，内容未挂载");
            return;
        }

        // 主区空置时显示"学程"占位（§1.2.9）：空白主窗看起来像启动失败。
        host.Content = content ?? (string.Equals(slotId, ShellSlots.Primary, StringComparison.Ordinal)
            ? CreatePrimaryPlaceholder()
            : null);

        // 底栏常驻（核心状态），其余槽位空置时整块隐藏，不留空框。
        if (!string.Equals(slotId, ShellSlots.Footer, StringComparison.Ordinal))
            host.IsVisible = host.Content is not null;
    }

    private static TextBlock CreatePrimaryPlaceholder() => new()
    {
        Text = "学程",
        FontSize = 56,
        Opacity = 0.35,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    /// <inheritdoc />
    public void ShowWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Show();
    }

    /// <inheritdoc />
    public Task<TResult?> ShowDialogAsync<TResult>(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return dialog.ShowDialog<TResult?>(_window);
    }

    /// <inheritdoc />
    public void PostToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    // ── INotificationRenderer（M1-A 只有系统气泡；胶囊要等主窗有布局，M1-D）──────

    /// <inheritdoc />
    public Task<Result> ShowAsync(NotificationRequest request, NotificationHandle handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // M1-A：统一走托盘气泡。胶囊渲染（NotificationStyle.Capsule）在 M1-D 做，
        // 这里不假装实现——把 style 记进日志，避免"以为走了胶囊"。
        _logger.Info($"[Shell] 通知（{request.PreferredStyle}）{request.Title}：{request.Body}");
        SystemToast.Show(request.Title, request.Body);

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task WithdrawAsync(NotificationHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>退出：销毁托盘图标（不销毁会让系统托盘留一个幽灵图标直到鼠标划过）。</summary>
    public void Dispose()
    {
        if (_tray is null)
            return;

        try { _tray.IsVisible = false; }
        catch (Exception ex) { _logger.Warn($"[Shell] 隐藏托盘图标失败：{ex.Message}"); }
    }

    // ── 内部 ───────────────────────────────────────────────────────────

    private TrayIcon? CreateTray()
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
            // 托盘建不起来不能让主窗起不来：降级为"只有主窗"。
            _logger.Error("[Shell] 托盘图标初始化失败（主窗仍可用）", ex);
            return null;
        }
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null)
            return;

        var menu = new NativeMenu();

        var toggle = new NativeMenuItem("显示 / 隐藏窗口");
        toggle.Click += (_, _) => ToggleWindow();
        menu.Add(toggle);

        if (_trayItems.Count > 0)
        {
            menu.Add(new NativeMenuItemSeparator());

            foreach (var item in _trayItems)
            {
                var menuItem = new NativeMenuItem(item.Header);
                var captured = item;
                menuItem.Click += (_, _) => InvokeSafely(captured.Id, captured.Invoke);
                menu.Add(menuItem);
            }
        }

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();
        menu.Add(exit);

        _tray.Menu = menu;
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            return;
        }

        _window.Show();
        _window.Activate();
    }

    private void ExitApplication()
    {
        Dispose();

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>底栏常驻内容：这是**核心自己**的槽位（§1.2.9），插件不该占。</summary>
    private void FillFooter()
    {
        if (_window.SlotHost(ShellSlots.Footer) is not { } footer)
            return;

        footer.Content = new TextBlock
        {
            Text = $"学程 StudyJourney · 微内核 {CoreInfo.ApiVersion} · M1",
            FontSize = 12,
            Opacity = 0.85,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
    }

    /// <summary>插件回调抛异常不能打死壳（一次点击失败不该让整个 UI 僵住）。</summary>
    private void InvokeSafely(string id, Action invoke)
    {
        try
        {
            invoke();
        }
        catch (Exception ex)
        {
            _logger.Error($"[Shell] 菜单/托盘项「{id}」的回调抛出异常", ex);
        }
    }
}
