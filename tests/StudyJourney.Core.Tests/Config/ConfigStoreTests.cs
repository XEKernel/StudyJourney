using System.Text;
using System.Text.Json.Nodes;
using StudyJourney.Core.Config;
using StudyJourney.Core.Tests.TestSupport;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Core.Tests.Config;

/// <summary>
/// S7：配置分发（CORE_ARCHITECTURE §5.0–§5.5）。
///
/// 夹具刻意按 v3.3.0 的真实格式构造：**CRLF + 2 空格缩进 + 中文 \uXXXX 转义 + 无 BOM + 无结尾换行**。
/// 用一个"干净的小 JSON"（LF、中文原样、末尾换行）测不出格式保真问题——那正是 S8 要防的事。
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private const string RawJson =
        "{\r\n" +
        "  \"FontSize\": 12,\r\n" +
        "  \"ChinesePrefix\": \"\\u8DDD\\u79BB \",\r\n" +
        "  \"ShowDays\": true,\r\n" +
        "  \"GaokaoDateStr\": \"2027-06-07 09:00:00\",\r\n" +
        "  \"OrphanInt\": 3\r\n" +
        "}";

    private readonly string _dir;
    private readonly RecordingLogger _log = new();

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sj-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 文件可能仍被占用 */ }
    }

    private string WriteFile(string content, string name = "settings.json")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private ConfigStore LoadRaw() => ConfigStore.Load(WriteFile(RawJson), _log);

    // ── 读：按 configFields 摘 ───────────────────────────────────────────

    [Fact(DisplayName = "S7-01 分发：句柄只认得自己认领的字段")]
    public void ReadSection_OnlyClaimsOwnFields()
    {
        var store = LoadRaw();
        var handle = store.CreateHandle("countdown", new[] { "FontSize", "ShowDays" });

        var section = handle.ReadSection();
        Assert.NotNull(section);
        Assert.Equal(new[] { "FontSize", "ShowDays" }, section!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal(12, section["FontSize"]!.GetValue<int>());
        Assert.True(section["ShowDays"]!.GetValue<bool>());
    }

    [Fact(DisplayName = "S7-02 读取返回深拷贝：改返回值不影响根树")]
    public void ReadSection_ReturnsDeepCopy()
    {
        var store = LoadRaw();
        var section = store.ReadSection(new[] { "FontSize" })!;

        section["FontSize"] = 99;

        Assert.Equal(12, store.Root["FontSize"]!.GetValue<int>());
    }

    [Fact(DisplayName = "S7-03 一个键都不存在时返回 null（不回写空节）")]
    public void ReadSection_ReturnsNull_WhenNoFieldExists()
    {
        var store = LoadRaw();
        var section = store.ReadSection(new[] { "NotThere", "AlsoNotThere" });

        Assert.Null(section);
        Assert.DoesNotContain("NotThere", store.Root.Select(p => p.Key));
    }

    // ── 写：按字段原位写回 ───────────────────────────────────────────────

    [Fact(DisplayName = "S7-04 写回原位：未认领字段不动、键顺序不变")]
    public void WriteSection_WritesInPlace_KeepsUnclaimedAndOrder()
    {
        var store = LoadRaw();
        var handle = store.CreateHandle("countdown", new[] { "FontSize" });

        var section = handle.ReadSection()!.AsObject();
        section["FontSize"] = 20;

        var result = handle.WriteSection(section);

        Assert.True(result.IsOk);
        Assert.Equal(20, store.Root["FontSize"]!.GetValue<int>());
        Assert.Equal(
            new[] { "FontSize", "ChinesePrefix", "ShowDays", "GaokaoDateStr", "OrphanInt" },
            store.Root.Select(p => p.Key).ToArray());
        Assert.Equal("\u8DDD\u79BB ", store.Root["ChinesePrefix"]!.GetValue<string>());
    }

    [Fact(DisplayName = "S7-05 写未声明字段：忽略 + Warn，根树不变")]
    public void WriteSection_IgnoresUndeclaredField_AndWarns()
    {
        var store = LoadRaw();
        var handle = store.CreateHandle("countdown", new[] { "FontSize" });

        var section = new JsonObject
        {
            ["FontSize"] = 20,
            ["OrphanInt"] = 777,
        };

        var result = handle.WriteSection(section);

        Assert.True(result.IsOk);
        Assert.Equal(20, store.Root["FontSize"]!.GetValue<int>());
        Assert.Equal(3, store.Root["OrphanInt"]!.GetValue<int>());   // 未被插件改动
        Assert.True(_log.Contains("warn", "试图写入未声明的字段 OrphanInt"));
        Assert.True(_log.Contains("warn", "countdown"));
    }

    [Fact(DisplayName = "S7-06 少写字段保持原值（不删、不清空）")]
    public void WriteSection_MissingFieldKeepsOriginalValue()
    {
        var store = LoadRaw();
        var handle = store.CreateHandle("countdown", new[] { "FontSize", "ShowDays" });

        var section = new JsonObject { ["FontSize"] = 30 };   // 刻意不写 ShowDays
        handle.WriteSection(section);

        Assert.Equal(30, store.Root["FontSize"]!.GetValue<int>());
        Assert.True(store.Root["ShowDays"]!.GetValue<bool>());
    }

    [Fact(DisplayName = "S7-07 新增认领字段追加在末尾，原有键顺序不变")]
    public void WriteSection_NewClaimedField_AppendedAtEnd()
    {
        var store = ConfigStore.CreateInMemory("{\"A\":1}", _log);
        var handle = store.CreateHandle("p", new[] { "A", "NewField" });

        handle.WriteSection(new JsonObject { ["A"] = 2, ["NewField"] = "x" });

        Assert.Equal(new[] { "A", "NewField" }, store.Root.Select(p => p.Key).ToArray());
    }

    [Fact(DisplayName = "S7-08 值没变不标 dirty（避免无意义落盘）")]
    public void WriteSection_UnchangedValue_DoesNotMarkDirty()
    {
        var store = LoadRaw();
        var handle = store.CreateHandle("countdown", new[] { "FontSize" });

        // 原样写回：值完全一致
        var section = new JsonObject { ["FontSize"] = 12 };
        handle.WriteSection(section);

        Assert.False(store.IsDirty);
    }

    // ── 孤儿字段 ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "S7-09 孤儿字段清单：文件里有、没人认领，顺序 = 文件顺序")]
    public void FindOrphanFields_ListsUnclaimedInFileOrder()
    {
        var store = LoadRaw();

        var orphans = store.FindOrphanFields(new[] { "FontSize", "ChinesePrefix" });

        Assert.Equal(new[] { "ShowDays", "GaokaoDateStr", "OrphanInt" }, orphans.ToArray());
    }

    // ── 落盘：不写就不碰文件 ─────────────────────────────────────────────

    [Fact(DisplayName = "S7-10 未 dirty 时 Flush 不打开文件（独占锁住也能成功）")]
    public void Flush_WhenClean_DoesNotTouchFile()
    {
        var path = WriteFile(RawJson);
        var store = ConfigStore.Load(path, _log);
        var before = File.ReadAllBytes(path);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // 独占锁着文件：任何真实 IO 都会 IOException。能成功 = 证明没碰文件。
            var result = store.Flush();
            Assert.True(result.IsOk, "未 dirty 的 Flush 必须是纯空操作");
        }

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact(DisplayName = "S7-11 dirty 后落盘：格式逐字节保真（CRLF/2 空格/\\uXXXX/无结尾换行）")]
    public void Flush_WhenDirty_PreservesFormatBytes()
    {
        var path = WriteFile(RawJson);
        var store = ConfigStore.Load(path, _log);
        Assert.True(store.CanRoundTripBytes, "v3.3.0 真实格式应当可以被逐字节复现");

        var handle = store.CreateHandle("countdown", new[] { "FontSize" });
        var section = handle.ReadSection()!.AsObject();
        section["FontSize"] = 14;
        handle.WriteSection(section);

        Assert.True(store.IsDirty);
        var result = store.Flush();
        Assert.True(result.IsOk);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x7B }, bytes.Take(1).ToArray());   // 无 BOM，首字节是 '{'

        var text = new UTF8Encoding(false).GetString(bytes);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.Equal(
            RawJson.Replace("\"FontSize\": 12", "\"FontSize\": 14", StringComparison.Ordinal),
            text);
        Assert.DoesNotContain((byte)0xEF, bytes.Take(3).ToArray());
        Assert.False(text.EndsWith("\n", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "S7-12 落盘失败（文件被独占锁）：返回 io-error 且保持 dirty")]
    public void Flush_WhenFileLocked_ReturnsIoErrorAndStaysDirty()
    {
        var path = WriteFile(RawJson);
        var store = ConfigStore.Load(path, _log);
        var handle = store.CreateHandle("countdown", new[] { "FontSize" });
        handle.WriteSection(new JsonObject { ["FontSize"] = 21 });

        Result result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = store.Flush();
        }

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCodes.IoError, result.Code);
        Assert.True(store.IsDirty);
        Assert.True(_log.Contains("error", "写入配置文件失败"));
    }

    // ── 损坏与降级（§5.7 两条分支）──────────────────────────────────────

    [Fact(DisplayName = "S7-13 坏 JSON：备份 .corrupted.<stamp> + 删原文件 + 默认值启动")]
    public void Load_BrokenJson_BacksUpAndRebuilds()
    {
        var path = WriteFile("{ \"FontSize\": ");

        var store = ConfigStore.Load(path, _log);

        Assert.Equal("corrupted", store.LoadError);
        Assert.Empty(store.Root);
        Assert.False(File.Exists(path));

        var backups = Directory.GetFiles(_dir, "settings.json.corrupted.*");
        Assert.Single(backups);
        Assert.Equal("{ \"FontSize\": ", File.ReadAllText(backups[0]));
    }

    [Fact(DisplayName = "S7-14 根不是对象（JSON 数组）：同样按损坏处理")]
    public void Load_NonObjectRoot_TreatedAsCorrupted()
    {
        var path = WriteFile("[1,2,3]");

        var store = ConfigStore.Load(path, _log);

        Assert.Equal("corrupted", store.LoadError);
        Assert.Empty(store.Root);
        Assert.False(File.Exists(path));
    }

    [Fact(DisplayName = "S7-15 IO 异常：原文件保留、用默认值、记 Error")]
    public void Load_IoError_KeepsOriginalFile()
    {
        var path = WriteFile(RawJson);
        var before = File.ReadAllBytes(path);

        ConfigStore store;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = ConfigStore.Load(path, _log);   // 独占锁 → 读失败
        }

        Assert.Equal(ErrorCodes.IoError, store.LoadError);
        Assert.Empty(store.Root);
        Assert.True(File.Exists(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(_log.Contains("error", "读取配置文件失败"));
    }

    [Fact(DisplayName = "S7-16 无配置文件：空根 + 不抛（等价于默认值启动）")]
    public void Load_MissingFile_DegradesToEmptyRoot()
    {
        var path = Path.Combine(_dir, "does-not-exist.json");

        var store = ConfigStore.Load(path, _log);

        Assert.Equal(ErrorCodes.IoError, store.LoadError);
        Assert.Empty(store.Root);
        Assert.False(File.Exists(path));   // 读不到也不创建
    }

    [Fact(DisplayName = "S7-17 原文件格式不可复现时：CanRoundTripBytes=false（如实上报，不粉饰）")]
    public void Load_NonCanonicalFormat_ReportsNotRoundTrippable()
    {
        // 中文原样 + LF + 末尾换行：任何标准化序列化都不可能逐字节复现
        var path = WriteFile("{\n  \"Chinese\": \"距离 \"\n}\n");

        var store = ConfigStore.Load(path, _log);

        Assert.False(store.CanRoundTripBytes);
        Assert.Equal("距离 ", store.Root["Chinese"]!.GetValue<string>());
    }
}
