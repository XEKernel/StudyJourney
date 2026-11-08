using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Config;

/// <summary>
/// <see cref="IPluginConfig"/> 的实现：**插件与 <c>settings.json</c> 之间唯一的一层**（§2.9）。
///
/// 句柄只认识构造时传入的那批 <c>configFields</c>：
/// 读 = 从根树摘出来 + 深拷贝；写 = 按字段原位写回。插件拿不到整棵树，也拿不到文件路径。
/// </summary>
internal sealed class ConfigHandle : IPluginConfig
{
    private readonly ConfigStore _store;
    private readonly string _pluginId;
    private readonly IReadOnlyList<string> _configFields;

    public ConfigHandle(ConfigStore store, string pluginId, IReadOnlyList<string> configFields)
    {
        _store = store;
        _pluginId = pluginId;
        _configFields = configFields;
    }

    /// <inheritdoc />
    public JsonNode? ReadSection() => _store.ReadSection(_configFields);

    /// <inheritdoc />
    public Result<T> ReadSection<T>(JsonSerializerContext context) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(context);

        var section = ReadSection();
        if (section is null)
            return Result<T>.Ok(new T());   // 节不存在 → 默认值，**不回写**（§5.2 第 4 步）

        try
        {
            var typeInfo = context.GetTypeInfo(typeof(T));
            if (typeInfo is null)
            {
                Warn($"插件 {_pluginId} 的 {typeof(T).Name} 不在自己的 JsonSerializerContext 里，已回落默认值");
                return Result<T>.Ok(new T());
            }

            return JsonSerializer.Deserialize(section, typeInfo) is T value
                ? Result<T>.Ok(value)
                : Result<T>.Ok(new T());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // §5.8：类型不符 / 文件被手工改坏 → 回落默认值并 Warn，**绝不抛**（一个字段错不该让插件起不来）。
            Warn($"插件 {_pluginId} 的配置反序列化为 {typeof(T).Name} 失败，已回落默认值：{ex.Message}");
            return Result<T>.Ok(new T());
        }
    }

    /// <inheritdoc />
    public Result WriteSection(JsonNode section) => _store.WriteSection(_pluginId, _configFields, section);

    private void Warn(string message) => _store.Logger?.Warn(message);
}
