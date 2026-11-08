using System.Globalization;
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
/// **五条不可让步的行为**：
/// <list type="number">
/// <item>整文件读成 <see cref="JsonObject"/> 根树，**扁平结构**，不建任何节外壳；</item>
/// <item>插件只拿到 <see cref="IPluginConfig"/> 句柄：读是"按 <c>configFields</c> 摘出来 + 深拷贝"，
///       写是"按字段原位写回"——插件永远碰不到整棵树；</item>
/// <item>未认领 / 孤儿字段**一个都不动**（不删、不改、不重排）；</item>
/// <item>写未声明字段 → 忽略 + <c>Warn</c>；少写字段 → 保持原值；</item>
/// <item>**没有真实写入就绝不重写文件**（<see cref="IsDirty"/>）——
///       这是 A0-3 字节级往返能成立的前提（见 §7 陷阱 5）。</item>
/// </list>
///
/// **字节保真的实现方式**：<see cref="JsonSerializerOptions"/> 在 .NET 9+ 支持
/// <c>WriteIndented</c> / <c>IndentCharacter</c> / <c>IndentSize</c> / <c>NewLine</c>，
/// 配合 STJ 默认编码器（中文转 <c>\uXXXX</c>）可以**逐字节复现** v3.3.0 写出的
/// "CRLF + 2 空格 + 无 BOM + 无结尾换行"格式。加载时用 <see cref="CanRoundTripBytes"/>
/// 实测这一点：原文件若是别的格式（例如手工编辑成 UTF-8 中文原样），
/// 首次真实写入会统一成标准格式并记 <c>Warn</c> —— 不静默改格式。
/// </summary>
public sealed class ConfigStore
{
    private readonly IPluginLogger? _logger;
    private readonly string? _path;
    private readonly bool _withBom;
    private readonly string _trailingNewLine;
    private readonly JsonSerializerOptions _writeOptions;
    private JsonObject _root;
    private bool _dirty;
    private bool _roundTripWarned;

    private ConfigStore(
        string? path,
        JsonObject root,
        bool withBom,
        string newLine,
        string trailingNewLine,
        string? rawForRoundTrip,
        IPluginLogger? logger,
        string? loadError)
    {
        _path = path;
        _root = root;
        _withBom = withBom;
        _trailingNewLine = trailingNewLine;
        _logger = logger;
        LoadError = loadError;

        _writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = newLine,
        };

        // 实测"这份原文件能不能被逐字节复现"。能 → 真实写入后其它字节一个不差；
        // 不能 → 只告警，不改写语义（A0-3 的判据仍然是逐字节相等，不对它开后门）。
        CanRoundTripBytes = rawForRoundTrip is null
            || string.Equals(SerializeToText(), rawForRoundTrip, StringComparison.Ordinal);
    }

    /// <summary>内存里的根对象。**核心内部与测试用**；插件只能通过 <see cref="CreateHandle"/> 拿句柄。</summary>
    public JsonObject Root => _root;

    /// <summary>是否发生过真实写入（未 dirty 时 <see cref="Flush"/> 不碰磁盘）。</summary>
    public bool IsDirty => _dirty;

    /// <summary>原文件的格式能否被逐字节复现（加载时实测；无原文件时为 <c>true</c>）。</summary>
    public bool CanRoundTripBytes { get; }

    /// <summary>加载阶段的问题码（<see cref="ErrorCodes.IoError"/> 或 <c>corrupted</c>）；正常为 <c>null</c>。</summary>
    public string? LoadError { get; }

    /// <summary>配置文件路径；纯内存实例为 <c>null</c>。**刻意不叫 <c>Path</c>**：那会在类作用域内遮蔽 <see cref="System.IO.Path"/>。</summary>
    public string? FilePath => _path;

    internal IPluginLogger? Logger => _logger;

    /// <summary>
    /// 从磁盘加载。**本方法不抛异常**（§5.7 的两个分支）：
    /// <list type="bullet">
    /// <item><see cref="JsonException"/>（内容真不是 JSON / 根不是对象）→ 备份 <c>.corrupted.&lt;stamp&gt;</c>
    ///       + 删原文件 + 用默认值（空根）启动，保留最近 3 份备份；</item>
    /// <item>其它异常（IO / 权限 / 被杀软占用）→ **原文件保留**，本次用默认值，记 <c>Error</c>。</item>
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
            return new ConfigStore(full, new JsonObject(), false, "\r\n", string.Empty, null, logger,
                ErrorCodes.IoError);
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

            BackupCorrupted(full, logger);
            return new ConfigStore(full, new JsonObject(), false, "\r\n", string.Empty, null, logger, "corrupted");
        }

        var newLine = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailing = raw.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : raw.EndsWith("\n", StringComparison.Ordinal) ? "\n"
            : string.Empty;

        return new ConfigStore(full, root, withBom, newLine, trailing, raw, logger, null);
    }

    /// <summary>纯内存实例（测试 / 没有配置文件时的降级形态）。<paramref name="json"/> 为 <c>null</c> 时是空根。</summary>
    public static ConfigStore CreateInMemory(string? json = null, IPluginLogger? logger = null)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject;
        if (root is null)
            throw new ArgumentException("json 必须是 JSON 对象", nameof(json));

        return new ConfigStore(null, root, false, "\r\n", string.Empty, null, logger, null);
    }

    /// <summary>为某个插件创建配置句柄（只认识它 <c>configFields</c> 里那批键）。</summary>
    public IPluginConfig CreateHandle(string pluginId, IReadOnlyList<string> configFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(configFields);
        return new ConfigHandle(this, pluginId, configFields);
    }

    /// <summary>
    /// 按 <paramref name="configFields"/> 从根树摘出一个**临时对象**返回；一个键都不存在时返回 <c>null</c>。
    /// 返回的是**深拷贝**——插件随便改都不会影响根树（§5.2）。
    /// </summary>
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
    /// 按字段**原位写回**（§5.0 的"写入"行）。
    /// 未声明字段忽略 + <c>Warn</c>；插件没给的键保持原值；已有键原位赋值，键顺序不变。
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
                continue;   // 值没变：不标 dirty，避免无意义的落盘（A0-3 的另一半保障）

            _root[key] = value?.DeepClone();
            changed = true;
        }

        if (changed)
            _dirty = true;

        return Result.Ok();
    }

    /// <summary>
    /// 列出"文件里有、但没有任何插件认领"的孤儿字段（A0-5b）。顺序 = 文件里的键顺序。
    /// **本方法只读不改**：孤儿字段"永不自动清理"（§5.5）。
    /// </summary>
    public IReadOnlyList<string> FindOrphanFields(IEnumerable<string> claimedFields)
    {
        ArgumentNullException.ThrowIfNull(claimedFields);

        var claimed = new HashSet<string>(claimedFields, StringComparer.Ordinal);
        return _root.Select(pair => pair.Key).Where(key => !claimed.Contains(key)).ToList();
    }

    /// <summary>把根树序列化成落盘文本（含原文件的结尾换行风格）。</summary>
    public string SerializeToText() => JsonSerializer.Serialize(_root, _writeOptions) + _trailingNewLine;

    /// <summary>
    /// 落盘。**未 dirty 时是纯空操作**（连文件都不打开）——A0-3 的关键。
    /// 失败只记 <c>Error</c> 并保持 dirty，下次写或退出时再试（§5.11 第 3 条：不重试循环）。
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
            AtomicFile.WriteAllText(_path, SerializeToText(), _withBom);
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

    /// <summary>§5.7 分支一：备份 → 删原文件 → 保留最近 3 份。</summary>
    private static void BackupCorrupted(string path, IPluginLogger? logger)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var backup = path + ".corrupted." + stamp;
            File.Copy(path, backup, overwrite: true);
            File.Delete(path);
            logger?.Error($"已把损坏的 settings.json 备份为 {Path.GetFileName(backup)}，并用默认值启动");

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                return;

            var prefix = Path.GetFileName(path) + ".corrupted.";
            var stale = Directory.GetFiles(dir, prefix + "*")
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .Skip(3);
            foreach (var file in stale)
            {
                try { File.Delete(file); }
                catch { /* 备份清理失败不影响启动 */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Error("备份损坏的配置文件失败（原文件保留，本次用默认值启动）", ex);
        }
    }
}
