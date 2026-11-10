using System.Text.Json.Nodes;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Countdown;

/// <summary>
/// 倒计时插件（plugin id = <c>countdown</c>，M1-B）：<c>main.primary</c> 卡片 + 菜单/托盘入口。
/// <b>不注册热键</b>——<c>Ctrl+Shift+H</c> 归壳，插件注册只会拿到 <c>hotkey-taken</c>。
///
/// 三阶段：Initialize 只注册（建卡片投递到 PostToUi 队列）、Start 才起 1 秒定时器
/// （日期解析失败则不起，卡片自己显示"日期未设置"）、Stop 停表 + 释放全部句柄且幂等。
///
/// 槽位内容必须走 <c>PostToUi</c>：插件早于 Avalonia 启动就加载，此时 new 控件不安全；宿主未挂上时它会排队，窗口起来后按序执行。
/// </summary>
public sealed class CountdownPlugin : IPlugin, ISettingsContributor
{
    private const string MenuItemId = "countdown.settings";
    private const string TrayItemId = "countdown.toggle";
    private const int RefreshIntervalMs = 1000;

    /// <summary>保护 <see cref="_handles"/> 与 <see cref="_timer"/>（UI 线程建卡片、加载器线程 StopAsync）。</summary>
    private readonly object _gate = new();
    private readonly List<IDisposable> _handles = [];

    private CountdownSettings _settings = CountdownSettings.CreateDefault();
    private IPluginLogger? _logger;
    private IShellService? _shell;
    private CountdownCard? _card;
    private System.Threading.Timer? _timer;
    private DateTime? _target;
    private bool _visible = true;
    private int _tickFailures;

    /// <summary>已 Stop：此时再登记句柄必须立刻释放（UI 线程的建卡片回调可能晚于 Stop 到达）。</summary>
    private bool _stopped;

    /// <summary>与 <c>plugin.json</c> 的 <c>id</c> 一致（不一致会被加载器 id-mismatch 拒载）。</summary>
    public string Id => "countdown";

    /// <summary>与 manifest 的 <c>configFields</c> 指向同一批键。</summary>
    public string SectionKey => "countdown";

    /// <inheritdoc />
    public Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _stopped = false;   // 允许"卸载后重新加载"（加载器复用本实例时）
        }

        _logger = context.GetLogger();
        _settings = CountdownSettings.FromSection(context.GetConfig().ReadSection(), _logger);
        _target = CountdownMath.TryParseDateTime(_settings.GaokaoDateStr, out var target) ? target : null;

        if (_target is null)
            _logger.Warn($"高考日期 \"{_settings.GaokaoDateStr}\" 无法解析，倒计时不启动（卡片显示\"日期未设置\"）。");

        var shell = context.GetUI();
        _shell = shell;

        AddHandle(shell.RegisterMenuItem(new MenuItemDescriptor
        {
            Id = MenuItemId,
            Header = "倒计时…",
            Order = 10,
            Invoke = OnSettingsRequested,
        }));

        AddHandle(shell.RegisterTrayItem(new TrayItemDescriptor
        {
            Id = TrayItemId,
            Header = "显示/隐藏倒计时",
            Order = 10,
            Invoke = ToggleVisibility,
        }));

        // 只排队，不在这里 new 控件（见类注释）。
        shell.PostToUi(CreateCard);

        _logger.Info("倒计时已注册：菜单 + 托盘，卡片待 UI 线程挂载到 " + ShellSlots.Primary);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_target is null)
            return Task.FromResult(Result.Ok());   // 已在 Initialize 记过 Warn，这里只需不启动定时器

        lock (_gate)
        {
            _timer ??= new System.Threading.Timer(
                OnTick,
                state: null,
                dueTime: TimeSpan.Zero,
                period: TimeSpan.FromMilliseconds(RefreshIntervalMs));
        }

        _logger?.Info("倒计时定时器已启动（1 秒）");
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _stopped = true;
            _timer?.Dispose();
            _timer = null;
        }

        _card = null;

        IDisposable[] handles;
        lock (_gate)
        {
            handles = [.. _handles];
            _handles.Clear();
        }

        foreach (var handle in handles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception ex)
            {
                // 卸载是尽力而为：单项释放失败不能阻断退出（IPlugin.StopAsync 的契约）。
                _logger?.Warn($"释放倒计时注册项失败：{ex.Message}");
            }
        }

        _shell = null;
        return Task.CompletedTask;
    }

    /// <summary>设置页是后续里程碑的事，这里返回空列表（侧栏不会为倒计时出现一个空页）。</summary>
    public IReadOnlyList<SettingsPageDescriptor> GetPages() => [];

    /// <inheritdoc />
    public ResetPlan BuildResetPlan() => new()
    {
        Summary = "倒计时将恢复为默认的高考日期与显示选项",
        Items =
        [
            new ResetPlanItem
            {
                Group = ResetItemGroup.Reset, Description = "高考日期与备考起始日",
                Detail = $"恢复为 {CountdownSettings.DefaultGaokaoDate} / {CountdownSettings.DefaultStartDate}",
            },
            new ResetPlanItem
            {
                Group = ResetItemGroup.Reset, Description = "天数 / 时分秒 / 进度条的显示开关与版式",
                Detail = "全部恢复为显示，进度条版式恢复为横向",
            },
        ],
    };

    /// <inheritdoc />
    public void ResetToDefaults(JsonNode section)
    {
        // 契约是"按 configFields 摘出来的临时 JsonObject"；形状不对就什么都不做（不越权改别人的树）。
        if (section is not JsonObject target)
            return;

        CountdownSettings.WriteDefaults(target);
    }

    /// <summary>UI 线程：建卡片并挂到 <c>main.primary</c>。</summary>
    private void CreateCard()
    {
        var shell = _shell;
        if (shell is null)
            return;

        try
        {
            var card = new CountdownCard(_settings, shell.CurrentTheme) { IsVisible = _visible };
            var registered = shell.RegisterSlotContent(ShellSlots.Primary, card);

            if (registered.IsFailure || registered.Value is null)
            {
                _logger?.Warn($"倒计时卡片未能挂到 {ShellSlots.Primary}：{registered.Code} {registered.Message}");
                return;
            }

            _card = card;
            AddHandle(registered.Value);
            card.Refresh(DateTime.Now);
        }
        catch (Exception ex)
        {
            // 这个回调跑在 UI 线程上，异常逃出去会杀掉进程——必须兜住。
            _logger?.Error("倒计时卡片创建失败", ex);
        }
    }

    /// <summary>托盘点击：切换卡片可见性。卡片还没建时只记意图，CreateCard 会按它初始化。</summary>
    private void ToggleVisibility()
    {
        _visible = !_visible;

        if (_card is not null)
            _card.IsVisible = _visible;

        _logger?.Info(_visible ? "倒计时卡片已显示" : "倒计时卡片已隐藏");
    }

    /// <summary>菜单点击：M1 只记日志（设置窗在后续里程碑，不在这里自造窗口）。</summary>
    private void OnSettingsRequested() => _logger?.Info("倒计时设置入口被点击（设置窗待 M1 后续实现）");

    /// <summary>定时器回调（后台线程）：只做一件事——把刷新投递到 UI 线程。</summary>
    private void OnTick(object? state)
    {
        try
        {
            _shell?.PostToUi(RefreshCard);
            Interlocked.Exchange(ref _tickFailures, 0);
        }
        catch (Exception ex)
        {
            ReportTickFailure(ex);
        }
    }

    /// <summary>UI 线程：刷新卡片文本与进度。</summary>
    private void RefreshCard()
    {
        try
        {
            _card?.Refresh(DateTime.Now);
        }
        catch (Exception ex)
        {
            ReportTickFailure(ex);
        }
    }

    /// <summary>每秒一次的失败不能每秒写一条日志（§6.9）：首次 + 每 60 次汇总。</summary>
    private void ReportTickFailure(Exception ex)
    {
        var count = Interlocked.Increment(ref _tickFailures);
        if (count == 1 || count % 60 == 0)
            _logger?.Error($"倒计时刷新失败（累计 {count} 次）", ex);
    }

    /// <summary>登记待释放句柄（可能在 UI 线程被调用，与 StopAsync 用同一把锁）。</summary>
    private void AddHandle(IDisposable handle)
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _handles.Add(handle);
                return;
            }
        }

        // 已经 Stop 了（建卡片的 UI 回调晚到）：当场释放，别让它留在壳上。
        handle.Dispose();
    }
}
