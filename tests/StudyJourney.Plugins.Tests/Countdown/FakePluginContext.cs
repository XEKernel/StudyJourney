using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Plugins.Tests.Countdown;

/// <summary>插件上下文替身：只提供倒计时真正用到的那几样（配置 / 日志 / 壳）。</summary>
internal sealed class FakePluginContext : IPluginContext
{
    private readonly IShellService _shell;
    private readonly FakePluginStorage _storage = new();

    internal FakePluginContext(IShellService shell, JsonNode? section = null)
    {
        _shell = shell;
        Config = new FakePluginConfig(section);
    }

    internal FakePluginLogger Logger { get; } = new();

    internal FakePluginConfig Config { get; }

    internal FakeEventBus EventBus { get; } = new();

    public T? GetService<T>() where T : class => null;

    public IEventBus GetEventBus() => EventBus;

    public IPluginConfig GetConfig() => Config;

    public IPluginStorage GetStorage() => _storage;

    public IPluginLogger GetLogger() => Logger;

    public IShellService GetUI() => _shell;
}

/// <summary>配置句柄替身：按契约返回深拷贝，写回只记最后一次。</summary>
internal sealed class FakePluginConfig(JsonNode? section) : IPluginConfig
{
    internal JsonNode? LastWritten { get; private set; }

    public JsonNode? ReadSection() => section?.DeepClone();

    /// <summary>倒计时不走这条重载（AOT 不友好），测试里直接标记为不可用。</summary>
    public Result<T> ReadSection<T>(JsonSerializerContext context) where T : class, new()
        => Result<T>.Fail("not-supported", "测试替身不支持反序列化重载");

    public Result WriteSection(JsonNode section)
    {
        LastWritten = section;
        return Result.Ok();
    }
}

/// <summary>日志替身：把每一条都留在内存里，供断言"类型不符记了 Warn"。</summary>
internal sealed class FakePluginLogger : IPluginLogger
{
    internal List<string> Traces { get; } = [];

    internal List<string> Debugs { get; } = [];

    internal List<string> Infos { get; } = [];

    internal List<string> Warns { get; } = [];

    internal List<string> Errors { get; } = [];

    public void Trace(string message) => Traces.Add(message);

    public void Debug(string message) => Debugs.Add(message);

    public void Info(string message) => Infos.Add(message);

    public void Warn(string message) => Warns.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}

/// <summary>事件总线替身：只记录发布的内容（倒计时目前不发布也不订阅）。</summary>
internal sealed class FakeEventBus : IEventBus
{
    internal List<object> Published { get; } = [];

    public void Publish<TEvent>(TEvent ev) where TEvent : class => Published.Add(ev);

    public Task PublishAsync<TEvent>(TEvent ev, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        Published.Add(ev);
        return Task.CompletedTask;
    }

    public IDisposable Subscribe<TEvent>(object owner, Action<TEvent> handler) where TEvent : class
        => new NoopHandle();

    public IDisposable SubscribeAsync<TEvent>(object owner, Func<TEvent, Task> handler) where TEvent : class
        => new NoopHandle();

    public void DisposeOwner(object owner)
    {
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>存储句柄替身：路径指向临时目录，测试不落盘。</summary>
internal sealed class FakePluginStorage : IPluginStorage
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "studyjourney-tests", "countdown");

    public string DataDirectory => _root;

    public string PluginDirectory => _root;

    public string HostDirectory => _root;

    public string ResolvePath(string relativePath) => Path.Combine(_root, relativePath);
}
