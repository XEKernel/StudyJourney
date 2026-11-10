using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// 课表插件（plugin id = <c>schedule</c>，M1-C）。
///
/// 职责划分（CORE_ARCHITECTURE §3.1）：
/// <list type="bullet">
/// <item><see cref="InitializeAsync"/> **只注册**：读一次 <c>schedule.json</c>、
/// 注册 <see cref="IScheduleProvider"/>、挂菜单项、把课表栏投递到 UI 线程挂槽位；</item>
/// <item><see cref="StartAsync"/> 才开工：1 秒界面心跳 + 文件变更监视（交给
/// <see cref="ScheduleFileWatcher"/>）；</item>
/// <item><see cref="StopAsync"/> 停表、释放全部 <see cref="IDisposable"/>，幂等且容忍 null。</item>
/// </list>
///
/// ⚠ 槽位挂载必须在 <c>PostToUi</c> 里做：插件在 Avalonia 启动**之前**加载，
/// 此时直接 <c>new Control()</c> 并塞进视觉树是不安全的。
/// </summary>
public sealed class SchedulePlugin : IPlugin
{
    /// <summary>课表编辑器菜单项 Id（编辑器本身属后续里程碑，这里只占位）。</summary>
    private const string EditorMenuId = "schedule.editor";

    /// <summary>界面心跳间隔。</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>文件变更检查间隔。老师的课表可能被远程/同事改掉。</summary>
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();

    private IPluginLogger? _logger;
    private IShellService? _shell;
    private IEventBus? _eventBus;
    private ScheduleStore? _store;
    private ScheduleProvider? _provider;
    private ScheduleBar? _bar;
    private ScheduleFileWatcher? _watcher;

    private IDisposable? _slotHandle;
    private IDisposable? _menuHandle;
    private Timer? _tickTimer;

    private bool _stopped;

    /// <inheritdoc />
    public string Id => "schedule";

    /// <inheritdoc />
    public Task<Result> InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger = context.GetLogger();
        _eventBus = context.GetEventBus();

        var registry = context.GetService<IServiceRegistry>();
        if (registry is null)
            return Task.FromResult(Result.Fail(ErrorCodes.ServiceMissing, "上下文未提供 IServiceRegistry"));

        // 路径来自 HostDirectory（exe 旁），**不是** data/<id>/：schedule.json 是 v3.3.0 遗留文件，
        // 必须原地读写（D 系列"数据零迁移"），搬走会造出两份不同步的副本。
        _store = new ScheduleStore(context.GetStorage().HostDirectory, _logger);

        var provider = new ScheduleProvider(_store, _logger);
        provider.Reload();          // 读失败不阻断启动：provider 内部已兜住并返回空课表
        _provider = provider;

        var registered = registry.Register<IScheduleProvider>(provider);
        if (registered.IsFailure)
            return Task.FromResult(registered);

        var shell = context.GetUI();
        _shell = shell;

        try
        {
            _menuHandle = shell.RegisterMenuItem(new MenuItemDescriptor
            {
                Id = EditorMenuId,
                Header = "课表…",
                Order = 20,
                Invoke = OnEditorInvoked,
            });
        }
        catch (Exception ex)
        {
            // Id 重复属编程错误（IShellService 约定抛 ArgumentException）；在插件边界转成失败结果，
            // 避免一个菜单项把整个加载流程炸掉。
            _logger?.Error("注册课表菜单项失败", ex);
            return Task.FromResult(Result.Fail(ErrorCodes.PluginThrew, "课表菜单项注册失败", ex));
        }

        // 控件必须回到 UI 线程创建并挂载（插件早于 Avalonia 启动加载）。
        shell.PostToUi(() => MountSlot(shell, provider));

        _logger?.Info("课表插件已初始化（IScheduleProvider 已注册，槽位待 UI 线程挂载）");
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_stopped)
                return Task.FromResult(Result.Fail(ErrorCodes.PluginLoadFailed, "插件已停止，不能重新启动"));

            if (_store is null || _provider is null)
                return Task.FromResult(Result.Fail(ErrorCodes.PluginLoadFailed, "插件尚未初始化"));

            try
            {
                // 定时器回调里抛异常会**杀掉整个进程**，两个回调体内部都全包 try/catch。
                _tickTimer = new Timer(OnTick, state: null, TickInterval, TickInterval);
                _watcher = new ScheduleFileWatcher(WatchInterval, _store, _provider, _eventBus, _logger, PostRefresh);
            }
            catch (Exception ex)
            {
                _logger?.Error("课表定时器启动失败", ex);
                DisposeTimers();
                return Task.FromResult(Result.Fail(ErrorCodes.PluginThrew, "课表定时器启动失败", ex));
            }
        }

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_stopped)
                return Task.CompletedTask;      // 幂等：加载器卸载与程序退出都会调

            _stopped = true;

            DisposeTimers();
            DisposeAndClear(ref _slotHandle);
            DisposeAndClear(ref _menuHandle);

            _bar = null;
            _watcher = null;
            _provider = null;
            _store = null;
            _shell = null;
        }

        _logger?.Info("课表插件已停止");
        return Task.CompletedTask;
    }

    /// <summary>UI 线程：构造课表栏并挂进 <c>main.secondary</c>。</summary>
    private void MountSlot(IShellService shell, ScheduleProvider provider)
    {
        try
        {
            _bar = new ScheduleBar(provider, shell.CurrentTheme);
            _bar.Refresh(DateTime.Now);

            var slot = shell.RegisterSlotContent(ShellSlots.Secondary, _bar);
            if (slot.IsOk)
            {
                _slotHandle = slot.Value;
                _logger?.Info("课表栏已挂载到 main.secondary");
                return;
            }

            // 槽位被别人占了：不阻断，只是老师看不到课表栏（其余消费方仍走 IScheduleProvider）。
            _logger?.Warn($"课表栏挂载失败（{slot.Code}）：{slot.Message}");
            _bar = null;
        }
        catch (Exception ex)
        {
            _logger?.Error("课表栏创建失败", ex);
            _bar = null;
        }
    }

    /// <summary>每秒心跳：只做一次界面刷新。</summary>
    private void OnTick(object? state)
    {
        try
        {
            PostRefresh();
        }
        catch (Exception ex)
        {
            _logger?.Error("课表心跳回调异常", ex);
        }
    }

    /// <summary>把"刷新课表栏"投递到 UI 线程；界面异常不拖垮程序，也不刷屏记日志。</summary>
    private void PostRefresh()
    {
        var shell = _shell;
        if (shell is null)
            return;

        shell.PostToUi(() =>
        {
            try
            {
                _bar?.Refresh(DateTime.Now);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"课表栏刷新失败：{ex.Message}");
            }
        });
    }

    private void OnEditorInvoked()
        => _logger?.Info("课表编辑器入口被点击（M1-C 只注册入口，界面属后续里程碑）");

    private void DisposeTimers()
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    private static void DisposeAndClear(ref IDisposable? handle)
    {
        try
        {
            handle?.Dispose();
        }
        catch
        {
            // 卸载是尽力而为：释放失败也必须继续往下走，否则程序关不掉。
        }
        finally
        {
            handle = null;
        }
    }
}
