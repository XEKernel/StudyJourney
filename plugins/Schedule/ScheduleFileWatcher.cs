using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// <c>schedule.json</c> 的变更监视：低频比对 mtime，变了就 Reload 并广播
/// <see cref="ScheduleChangedEvent"/>。
///
/// 为什么需要它：课表可能被**远程接口**或同事直接改文件，而不是走本插件的保存路径。
/// 只靠"谁写谁通知"会漏掉这两条通道，主窗就会一整天显示旧课表。
///
/// ⚠ 定时器回调抛异常会直接杀掉进程（.NET 的 Timer 回调跑在线程池上，无人接住），
/// 所以 <see cref="Check"/> 的整个函数体必须被 try/catch 包住。
/// </summary>
internal sealed class ScheduleFileWatcher : IDisposable
{
    private readonly ScheduleStore _store;
    private readonly ScheduleProvider _provider;
    private readonly IEventBus? _eventBus;
    private readonly IPluginLogger? _logger;

    /// <summary>重载完成后的回调（插件用它把界面刷新投递到 UI 线程）。</summary>
    private readonly Action _onReloaded;

    private readonly Timer _timer;
    private DateTime _lastWriteUtc;
    private bool _disposed;

    public ScheduleFileWatcher(
        TimeSpan interval,
        ScheduleStore store,
        ScheduleProvider provider,
        IEventBus? eventBus,
        IPluginLogger? logger,
        Action onReloaded)
    {
        _store = store;
        _provider = provider;
        _eventBus = eventBus;
        _logger = logger;
        _onReloaded = onReloaded;

        // 以"构造时刻"为基线：插件启动时已经读过一次盘，不该立刻再重载一遍。
        _lastWriteUtc = store.GetLastWriteTimeUtc();
        _timer = new Timer(Check, state: null, interval, interval);
    }

    private void Check(object? state)
    {
        try
        {
            CheckCore();
        }
        catch (Exception ex)
        {
            _logger?.Error("课表文件监视回调异常", ex);
        }
    }

    private void CheckCore()
    {
        var stamp = _store.GetLastWriteTimeUtc();
        if (stamp == DateTime.MinValue || stamp == _lastWriteUtc)
            return;

        _lastWriteUtc = stamp;

        // ⚠ 顺序：先自增版本，再发事件（§2.6.5 要求订阅方在同一次事件里看到一致版本）。
        var version = _provider.Reload();
        _logger?.Info($"检测到 schedule.json 变更，已重载（版本 {version}）");

        try
        {
            _eventBus?.Publish(new ScheduleChangedEvent
            {
                Kind = ScheduleChangeKind.Reloaded,
                Date = null,
                Version = version,
            });
        }
        catch (Exception ex)
        {
            // 事件发不出去不该阻断后续的界面刷新。
            _logger?.Warn($"课表变更事件发布失败：{ex.Message}");
        }

        _onReloaded();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
    }
}
