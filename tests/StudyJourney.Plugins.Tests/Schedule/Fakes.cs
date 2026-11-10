using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using StudyJourney.Sdk.Dto;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// 课表插件测试用的最小替身。**不引 Moq**（本工程没装，网络也装不上），
/// 手写反而更直白：每个替身只实现被测路径真正会碰到的方法。
/// </summary>
internal sealed class FakeShellService : IShellService
{
    public List<MenuItemDescriptor> MenuItems { get; } = [];

    public List<(string SlotId, Control Content)> SlotContents { get; } = [];

    /// <summary><see cref="PostToUi"/> 被调用的次数（课表栏挂载必须走它）。</summary>
    public int PostToUiCount { get; private set; }

    private readonly ThemeTokens _theme = new()
    {
        Accent = Color.Parse("#2B6CB0"),
        Surface = Color.Parse("#FFFFFF"),
        SurfaceAlt = Color.Parse("#F1F5F9"),
        TextPrimary = Color.Parse("#1A202C"),
        TextSecondary = Color.Parse("#4A5568"),
        TextDim = Color.Parse("#A0AEC0"),
        Danger = Color.Parse("#E53E3E"),
        Warning = Color.Parse("#DD6B20"),
        Success = Color.Parse("#38A169"),
        FontFamily = "Microsoft YaHei",
        FontSizeBase = 16,
        IsDark = false,
    };

    public IDisposable RegisterMenuItem(MenuItemDescriptor item)
    {
        MenuItems.Add(item);
        return new NoopDisposable();
    }

    public IDisposable RegisterTrayItem(TrayItemDescriptor item) => new NoopDisposable();

    public Result<IDisposable> RegisterHotKey(HotKeyDescriptor descriptor)
        => Result<IDisposable>.Ok(new NoopDisposable());

    public Result<IDisposable> RegisterSlotContent(string slotId, Control content)
    {
        if (!ShellSlots.IsValid(slotId))
            return Result<IDisposable>.Fail(ErrorCodes.SlotUnknown, $"未知槽位 {slotId}");

        SlotContents.Add((slotId, content));
        return Result<IDisposable>.Ok(new NoopDisposable());
    }

    public void ShowWindow(Window window) => throw new NotSupportedException("测试不需要窗口");

    public Task<TResult?> ShowDialogAsync<TResult>(Window dialog)
        => throw new NotSupportedException("测试不需要对话框");

    /// <summary>**同步执行**：与真实壳"排队到 UI 线程"不同，这里立刻跑，测试才能断言结果。</summary>
    public void PostToUi(Action action)
    {
        PostToUiCount++;
        action();
    }

    public ThemeTokens CurrentTheme => _theme;
}

/// <summary>存储替身：只给 <see cref="HostDirectory"/> 一个真实临时目录。</summary>
internal sealed class FakeStorage(string hostDirectory) : IPluginStorage
{
    public string DataDirectory => hostDirectory;

    public string PluginDirectory => hostDirectory;

    public string HostDirectory => hostDirectory;

    public string ResolvePath(string relativePath) => Path.Combine(hostDirectory, relativePath);
}

internal sealed class FakeLogger : IPluginLogger
{
    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public void Trace(string message)
    {
    }

    public void Debug(string message)
    {
    }

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}

internal sealed class FakeEventBus : IEventBus
{
    public List<object> Published { get; } = [];

    public void Publish<TEvent>(TEvent ev) where TEvent : class => Published.Add(ev);

    public Task PublishAsync<TEvent>(TEvent ev, CancellationToken cancellationToken = default) where TEvent : class
    {
        Published.Add(ev);
        return Task.CompletedTask;
    }

    public IDisposable Subscribe<TEvent>(object owner, Action<TEvent> handler) where TEvent : class
        => new NoopDisposable();

    public IDisposable SubscribeAsync<TEvent>(object owner, Func<TEvent, Task> handler) where TEvent : class
        => new NoopDisposable();

    public void DisposeOwner(object owner)
    {
    }
}

internal sealed class FakeServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<Type, object> _services = [];
    private bool _sealed;

    public Result Register<TService>(TService instance) where TService : class
    {
        if (_sealed)
            return Result.Fail(ErrorCodes.RegistrySealed, "注册表已封板");

        if (!_services.TryAdd(typeof(TService), instance))
            return Result.Fail(ErrorCodes.ServiceConflict, $"{typeof(TService).Name} 已注册");

        return Result.Ok();
    }

    public TService? Get<TService>() where TService : class
        => _services.TryGetValue(typeof(TService), out var instance) ? (TService)instance : null;

    public Result<TService> GetRequired<TService>() where TService : class
        => Get<TService>() is { } service
            ? Result<TService>.Ok(service)
            : Result<TService>.Fail(ErrorCodes.ServiceMissing, $"{typeof(TService).Name} 未注册");

    public bool IsRegistered<TService>() where TService : class => _services.ContainsKey(typeof(TService));

    public void Seal() => _sealed = true;
}

internal sealed class FakePluginConfig : IPluginConfig
{
    public JsonNode? ReadSection() => null;

    public Result<T> ReadSection<T>(JsonSerializerContext context) where T : class, new()
        => Result<T>.Ok(new T());

    public Result WriteSection(JsonNode section) => Result.Ok();
}

/// <summary>插件上下文替身：六个出口各给一个最小实现。</summary>
internal sealed class FakePluginContext(FakeShellService shell, IPluginStorage storage) : IPluginContext
{
    public FakeServiceRegistry Registry { get; } = new();

    public FakeEventBus Bus { get; } = new();

    public FakeLogger Logger { get; } = new();

    public FakeShellService Shell => shell;

    public T? GetService<T>() where T : class
    {
        // 真实宿主把 `IServiceRegistry` 这一层自己的注册表也当服务暴露给插件
        // （插件的 Initialize 第一件事就是 `GetService<IServiceRegistry>()`）。
        if (typeof(T) == typeof(IServiceRegistry))
            return (T)(object)Registry;

        return Registry.Get<T>();
    }

    public IEventBus GetEventBus() => Bus;

    public IPluginConfig GetConfig() => new FakePluginConfig();

    public IPluginStorage GetStorage() => storage;

    public IPluginLogger GetLogger() => Logger;

    public IShellService GetUI() => shell;
}

internal sealed class NoopDisposable : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
