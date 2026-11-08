using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyJourney.Core.IO;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Config;

/// <summary>
/// <c>settings.json</c> 的唯一持有者（CORE_ARCHITECTURE §5.0–§5.7 / D-3 已拍板）。
///
/// 五条不可让步的行为：
/// <list type="number">
/// <item>整文件读成 <see cref="JsonObject"/> 根树，**扁平结构**，不建任何节外壳；</item>
/// <item>插件只拿 <see cref="IPluginConfig"/> 句柄：读是"按 configFields 摘出来 + 深拷贝"，写是"按字段原位写回"；</item>
/// <item>未认领 / 孤儿字段**一个都不动**（不删、不改、不重排）；</item>
/// <item>写未声明字段 → 忽略 + Warn；少写字段 → 保持原值；</item>
/// <item>**没有真实写入就绝不重写文件**（<see cref="IsDirty"/>）—— 这是 A0-3 字节级往返的前提。</item>
/// </list>
/// 落盘格式与损坏恢复分别在 <see cref="ConfigFileFormat"/> / <see cref="ConfigFileRecovery"/>。
/// </summary>
public sealed class ConfigStore
{
    private readonly IPluginLogger? _logger;
    private readonly string? _path;
    private readonly ConfigFileFormat _format;
    private JsonObject _root;
    private bool _dirty;
    private bool _roundTripWarned;

    private ConfigStore(string? path, JsonObject root, ConfigFileFormat format, string? rawForRoundTrip, IPluginLogger? logger, string? loadError)
    {
        _path = path;
        _root = root;
        _format = format;
        _logger = logger;
        LoadError = loadError;

        // 实测"这份原文件能不能被逐字节复现"：能 → 真实写入后其它字节一个不差；
        // 不能 → 只在首次真实写入时告警（A0-3 的判据不因此松动）。
        CanRoundTripBytes = rawForRoundTrip is null || format.Matches(root, rawForRoundTrip);
    }

    /// <summary>内存里的根对象。**核心内部与测试用**；插件只能通过 <see cref="CreateHandle"/> 拿句柄。</summary>
    public JsonObject Root => _root;

    /// <summary>是否发生过真实写入（未 dirty 时 <see cref="Flush"/> 不碰磁盘）。</summary>
    public bool IsDirty => _dirty;

    /// <summary>原文件的格式能否被逐字节复现（加载时实测；无原文件时为 <c>true</c>）。</summary>
    public bool CanRoundTripBytes { get; }

    /// <summary>加载阶段的问题码（<see cref="ErrorCodes.IoError"/> 或 <c>corrupted</c>）；正常为 <c>null</c>。</summary>
    public string? LoadError { get; }

    /// <summary>配置文件路径；纯内存实例为 <c>null</c>。刻意不叫 <c>Path</c>：那会遮蔽 <see cref="System.IO.Path"/>。</summary>
    public string? FilePath => _path;

    internal IPluginLogger? Logger => _logger;

    /// <summary>
    /// 从磁盘加载。**不抛异常**（§5.7 两条分支）：
    /// <list type="bullet">
    /// <item>内容不是合法 JSON / 根不是对象 → <see cref="ConfigFileRecovery"/> 备份 + 删除 + 空根；</item>
    /// <item>其它异常（IO / 权限 / 占用）→ **原文件保留**，本次用空根启动，记 Error。</item>
    /// </list>
    /// </summary>
    public static ConfigStore Load(string path, IPluginLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.Error($"读取配置文件失败，本次用默认值启动（原文件保留）：{full}", ex);
            return new ConfigStore(full, new JsonObject(), new ConfigFileFormat(false, "\r\n", string.Empty), null, logger, ErrorCodes.IoError);
        }

        var withBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = withBom ? 3 : 0;
        var raw = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);

        JsonObject? root = null;
        try
        {
            root = JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException ex)
        {
            logger?.Error("settings.json 不是合法 JSON，按损坏处理（备份后重建）", ex);
        }

        if (root is null)
        {
            if (!string.IsNullOrWhiteSpace(raw))
                logger?.Error("settings.json 根节点不是 JSON 对象，按损坏处理（备份后重建）");

            ConfigFileRecovery.BackupCorrupted(full, logger);
            return new ConfigStore(full, new JsonObject(), new ConfigFileFormat(false, "\r\n", string.Empty), null, logger, "corrupted");
        }

        var format = new ConfigFileFormat(
            withBom,
            ConfigFileFormat.DetectNewLine(raw),
            ConfigFileFormat.DetectTrailingNewLine(raw));

        return new ConfigStore(full, root, format, raw, logger, null);
    }

    /// <summary>纯内存实例（测试 / 没有配置文件时的降级形态）。<paramref name="json"/> 为 <c>null</c> 时是空根。</summary>
    public static ConfigStore CreateInMemory(string? json = null, IPluginLogger? logger = null)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject;
        if (root is null)
            throw new ArgumentException("json 必须是 JSON 对象", nameof(json));

        return new ConfigStore(null, root, new ConfigFileFormat(false, "\r\n", string.Empty), null, logger, null);
    }

    /// <summary>为某个插件创建配置句柄（只认识它 <c>configFields</c> 里那批键）。</summary>
    public IPluginConfig CreateHandle(string pluginId, IReadOnlyList<string> configFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(configFields);
        return new ConfigHandle(this, pluginId, configFields);
    }

    /// <summary>按 <paramref name="configFields"/> 摘出一个**临时对象**；一个键都不存在时返回 <c>null</c>。返回深拷贝。</summary>
    public JsonNode? ReadSection(IReadOnlyList<string> configFields)
    {
        ArgumentNullException.ThrowIfNull(configFields);

        JsonObject? section = null;
        foreach (var field in configFields)
        {
            if (!_root.TryGetPropertyValue(field, out var value))
                continue;

            section ??= new JsonObject();
            section[field] = value?.DeepClone();
        }

        return section;
    }

    /// <summary>
    /// 按字段**原位写回**：未声明字段忽略 + <c>Warn</c>；插件没给的键保持原值；已有键原位赋值，键顺序不变。
    /// </summary>
    public Result WriteSection(string pluginId, IReadOnlyList<string> configFields, JsonNode? section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(configFields);

        // 没有内容 = 不修改。绝不能借机"清理"根树（§5.3 第 2 条）。
        if (section is null)
            return Result.Ok();

        if (section is not JsonObject incoming)
        {
            _logger?.Warn($"插件 {pluginId} 提交的配置节不是 JSON 对象，已忽略");
            return Result.Fail(ErrorCodes.IoError, "配置节必须是 JSON 对象");
        }

        var allowed = new HashSet<string>(configFields, StringComparer.Ordinal);
        var changed = false;

        foreach (var (key, value) in incoming.ToList())
        {
            if (!allowed.Contains(key))
            {
                _logger?.Warn($"插件 {pluginId} 试图写入未声明的字段 {key}，已忽略");
                continue;
            }

            if (_root.TryGetPropertyValue(key, out var existing) && JsonNodeEquals(existing, value))
                continue;   // 值没变：不标 dirty，避免无意义落盘（A0-3 的另一半保障）

            _root[key] = value?.DeepClone();
            changed = true;
        }

        if (changed)
            _dirty = true;

        return Result.Ok();
    }

    /// <summary>列出"文件里有、但没有任何插件认领"的孤儿字段（A0-5b）。顺序 = 文件键顺序；只读不改（§5.5）。</summary>
    public IReadOnlyList<string> FindOrphanFields(IEnumerable<string> claimedFields)
    {
        ArgumentNullException.ThrowIfNull(claimedFields);

        var claimed = new HashSet<string>(claimedFields, StringComparer.Ordinal);
        return _root.Select(pair => pair.Key).Where(key => !claimed.Contains(key)).ToList();
    }

    /// <summary>把根树序列化成落盘文本（含原文件的格式与结尾换行）。</summary>
    public string SerializeToText() => _format.Serialize(_root);

    /// <summary>
    /// 落盘。**未 dirty 时是纯空操作**（连文件都不打开）——A0-3 的关键。
    /// 失败只记 Error 并保持 dirty，下次写或退出时再试（§5.11：不做重试循环）。
    /// </summary>
    public Result Flush()
    {
        if (!_dirty)
            return Result.Ok();

        if (_path is null)
        {
            _dirty = false;
            return Result.Ok();
        }

        if (!CanRoundTripBytes && !_roundTripWarned)
        {
            _roundTripWarned = true;
            _logger?.Warn(
                $"配置文件 {Path.GetFileName(_path)} 的原有格式无法逐字节复现；本次真实写入后会统一为" +
                "CRLF + 2 空格缩进 + 中文 \\uXXXX 转义（内容语义不变，仅格式归一）");
        }

        try
        {
            AtomicFile.WriteAllText(_path, SerializeToText(), _format.WithBom);
            _dirty = false;
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.Error($"写入配置文件失败（保持 dirty，稍后重试）：{_path}", ex);
            return Result.Fail(ErrorCodes.IoError, "写入 settings.json 失败：" + ex.Message, ex);
        }
    }

    private static bool JsonNodeEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        try
        {
            return string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
