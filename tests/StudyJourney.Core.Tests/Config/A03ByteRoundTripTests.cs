using System.Text;
using System.Text.Json.Nodes;
using StudyJourney.Core.Config;
using StudyJourney.Core.Tests.TestSupport;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;
using Xunit;

namespace StudyJourney.Core.Tests.Config;

/// <summary>
/// **S8 / A0-3：M0 的第二条否决项（字节级往返）**，外加 A0-4 / A0-5 / A0-5b / A0-5c 四条附加判据
/// （CORE_ARCHITECTURE §9.1）。
///
/// 输入刻意用**真实的 v3.3.0 <c>settings.json</c>**（<c>tests/fixtures/config/</c>，
/// 一份仓库根配置 3,067 字节、一份老师机运行期配置 4,901 字节，后者只做了"等长脱敏"）。
/// 自己造的干净小 JSON（LF / 中文原样 / 末尾换行）测不出格式保真——
/// 那正是 A0-3 要防的事：<c>ToJsonString()</c> 默认会丢缩进、CRLF 与中文转义。
///
/// **不通过 → M0 不算完成，不得进 M1。**
/// </summary>
public sealed class A03ByteRoundTripTests : IDisposable
{
    private const string RootFixture = "v330-root-settings.json";
    private const string TeacherFixture = "v330-teacher-settings.json";

    private readonly string _dir;
    private readonly RecordingLogger _log = new();
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public A03ByteRoundTripTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _out = output;
        _dir = Path.Combine(Path.GetTempPath(), "sj-a03-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }



    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 文件可能仍被占用 */ }
    }

    /// <summary>夹具元数据：文件名 / 字节数 / CRLF 个数 / 顶层键数。</summary>
    public static TheoryData<string, int, int, int> RealFiles => new()
    {
        { RootFixture, 3067, 94, 93 },
        { TeacherFixture, 4901, 147, 96 },
    };

    private static string FixturePath(string name) => RepoRoot.Combine("tests", "fixtures", "config", name);

    /// <summary>复制到临时目录再操作 —— 夹具本身绝不能被测试改动。</summary>
    private string CopyFixture(string name)
    {
        var dest = Path.Combine(_dir, name);
        File.Copy(FixturePath(name), dest);
        return dest;
    }

    /// <summary>
    /// 模拟 §5.1 的 8 个认领方：把全部顶层键按轮转切成 8 份 <c>configFields</c>。
    /// 这样"分发"覆盖到真实文件的**每一个**键，而不是挑三个字段做做样子。
    /// </summary>
    private static List<IPluginConfig> DistributeKeys(ConfigStore store, int owners = 8)
    {
        var keys = store.Root.Select(pair => pair.Key).ToList();
        var handles = new List<IPluginConfig>(owners);
        for (var i = 0; i < owners; i++)
        {
            var mine = keys.Where((_, index) => index % owners == i).ToList();
            handles.Add(store.CreateHandle("plugin" + i, mine));
        }

        return handles;
    }

    // ── A0-3 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "A0-3 字节级往返：真实 settings.json 读入→分发→插件原样写回→落盘，逐字节相等")]
    [MemberData(nameof(RealFiles))]
    public void A03_RealFile_RoundTrip_IsByteIdentical(string file, int expectedBytes, int expectedCrlf, int expectedKeys)
    {
        var source = FixturePath(file);
        var originalBytes = File.ReadAllBytes(source);
        var originalText = new UTF8Encoding(false).GetString(originalBytes);

        // ① 先钉死"真源样本"的格式特征：防止哪天有人把夹具换成干净小 JSON 让测试变松
        Assert.Equal(expectedBytes, originalBytes.Length);
        Assert.Equal(expectedCrlf, originalText.Split("\r\n").Length - 1);
        Assert.DoesNotContain('\n', originalText.Replace("\r\n", string.Empty));   // 无裸 LF
        Assert.DoesNotContain('\r', originalText.Replace("\r\n", string.Empty));   // 无裸 CR
        Assert.False(originalText.Any(c => c > 127), "真实文件里的中文应全部是 \\uXXXX 转义");
        Assert.False(originalText.StartsWith("\uFEFF", StringComparison.Ordinal));
        Assert.False(originalText.Length > 0 && originalText[0] == '\uFEFF', "真实文件无 BOM");
        Assert.False(originalText.EndsWith("\n", StringComparison.Ordinal), "真实文件无结尾换行");

        // ② 读入
        var work = CopyFixture(file);
        var store = ConfigStore.Load(work, _log);
        Assert.Null(store.LoadError);
        Assert.Equal(expectedKeys, store.Root.Count);
        Assert.True(store.CanRoundTripBytes,
            $"{file}：加载时实测无法逐字节复现，说明序列化格式与原文件不一致（A0-3 否决项）");

        // ③ 分发：8 个认领方各拿一份临时 JsonObject
        var handles = DistributeKeys(store);
        Assert.Equal(8, handles.Count);

        // ④ 插件**不做任何修改**：读一遍、原样写回
        foreach (var handle in handles)
        {
            var section = handle.ReadSection();
            Assert.NotNull(section);
            Assert.True(handle.WriteSection(section!).IsOk);
        }

        Assert.False(store.IsDirty, "原样写回不应产生 dirty —— 值没变就不该触发落盘（§5.11）");
        Assert.True(store.Flush().IsOk);

        // ⑤ 落盘后逐字节比较
        var afterBytes = File.ReadAllBytes(work);
        Assert.Equal(originalBytes, afterBytes);

        // ⑥ 比"文件没被动过"更硬的证据：把树重新序列化一遍，结果必须与原文逐字节相同。
        //    这条才真正证明"写路径"保真（前一条在 dirty=false 时本来就不会碰文件）。
        Assert.Equal(originalText, store.SerializeToText());

        _out.WriteLine($"[A0-3] 夹具                         : {file}");
        _out.WriteLine($"[A0-3] 原文件字节                   : {originalBytes.Length}");
        _out.WriteLine($"[A0-3] 顶层键 / CRLF                : {store.Root.Count} / {expectedCrlf}");
        _out.WriteLine($"[A0-3] 认领方数量                   : {handles.Count}");
        _out.WriteLine($"[A0-3] CanRoundTripBytes            : {store.CanRoundTripBytes}");
        _out.WriteLine($"[A0-3] IsDirty（原样写回后）         : {store.IsDirty}");
        _out.WriteLine($"[A0-3] 落盘字节 == 原字节            : {originalBytes.SequenceEqual(afterBytes)}");
        _out.WriteLine($"[A0-3] 重新序列化 == 原文            : {string.Equals(originalText, store.SerializeToText(), StringComparison.Ordinal)}");
    }

    // ── A0-4 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-4 漏写/漏认领：已存在字段保持原值，claim 了但文件里没有的字段不被创建，其余字节不变")]
    public void A04_OmittedField_KeepsOriginal_AndDoesNotCreateNewField()
    {
        var work = CopyFixture(TeacherFixture);
        var originalText = File.ReadAllText(work);
        var store = ConfigStore.Load(work, _log);

        // BrandNewField 在 configFields 里，但文件里没有（新插件新增的配置项）
        var handle = store.CreateHandle("countdown", new[] { "FontSize", "BrandNewField" });

        // 插件只写 FontSize —— 刻意"省略" BrandNewField 与其它所有字段
        Assert.True(handle.WriteSection(new JsonObject { ["FontSize"] = 14 }).IsOk);
        Assert.True(store.IsDirty);
        Assert.True(store.Flush().IsOk);

        var afterText = File.ReadAllText(work);
        Assert.DoesNotContain("BrandNewField", afterText, StringComparison.Ordinal);
        Assert.Equal(originalText.Replace("\"FontSize\": 12", "\"FontSize\": 14", StringComparison.Ordinal), afterText);
    }

    // ── A0-5 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-5 只替换 1 组：另 2 组字节不变 + 键顺序不变（原位赋值）")]
    public void A05_ChangingOneGroup_LeavesOtherGroupsByteIdentical()
    {
        var work = CopyFixture(RootFixture);
        var originalText = File.ReadAllText(work);
        var store = ConfigStore.Load(work, _log);
        var orderBefore = store.Root.Select(pair => pair.Key).ToList();

        var groupA = store.CreateHandle("countdown", new[] { "ChinesePrefix", "FontSize", "NumberColorHex" });
        var groupB = store.CreateHandle("schedule", new[] { "GaokaoDateStr", "StartDateStr" });
        var groupC = store.CreateHandle("weather", new[] { "WeatherCity", "WeatherFontSize" });

        var bBefore = groupB.ReadSection()!.ToJsonString();
        var cBefore = groupC.ReadSection()!.ToJsonString();

        // 只改 A 组的一个值
        var section = groupA.ReadSection()!.AsObject();
        section["FontSize"] = 60;
        Assert.True(groupA.WriteSection(section).IsOk);
        Assert.True(store.Flush().IsOk);

        var afterText = File.ReadAllText(work);

        // 期望值来自**原文件文本 + 单点替换**，不是"再跑一遍实现"（避免测试与实现共谋）
        Assert.Equal(originalText.Replace("\"FontSize\": 72", "\"FontSize\": 60", StringComparison.Ordinal), afterText);

        // B / C 组语义值不变，且键顺序完全不变
        Assert.Equal(bBefore, store.ReadSection(new[] { "GaokaoDateStr", "StartDateStr" })!.ToJsonString());
        Assert.Equal(cBefore, store.ReadSection(new[] { "WeatherCity", "WeatherFontSize" })!.ToJsonString());
        var orderAfter = JsonNode.Parse(afterText)!.AsObject().Select(pair => pair.Key).ToList();
        Assert.Equal(orderBefore, orderAfter);
    }

    // ── A0-5b ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-5b configFields 覆盖率：孤儿字段逐条列出且原样保留")]
    public void A05b_OrphanFields_ListedAndPreserved()
    {
        var work = CopyFixture(TeacherFixture);
        var store = ConfigStore.Load(work, _log);

        // 真实认领清单的一个子集（字段名逐字取自 v3.3.0，不许改名）
        var claimed = new[] { "FontSize", "FontFamily", "GaokaoDateStr" };
        var orphans = store.FindOrphanFields(claimed);

        Assert.Equal(96 - claimed.Length, orphans.Count);
        Assert.DoesNotContain("FontSize", orphans);
        foreach (var field in orphans)
            Assert.True(store.Root.ContainsKey(field), $"孤儿字段 {field} 应原样保留在树里");

        // 真实写入一次（改一个自己认领的字段）后，孤儿字段仍在文件里
        var handle = store.CreateHandle("countdown", claimed);
        var section = handle.ReadSection()!.AsObject();
        section["FontSize"] = 16;
        Assert.True(handle.WriteSection(section).IsOk);
        Assert.True(store.Flush().IsOk);

        var afterText = File.ReadAllText(work);
        foreach (var field in orphans)
            Assert.Contains($"\"{field}\":", afterText, StringComparison.Ordinal);
    }

    // ── A0-5c ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "A0-5c 字段冲突：两个插件都认领 FontSize → 后者 field-conflict，提示点名双方")]
    public void A05c_FieldConflict_RejectsLaterPlugin()
    {
        var countdown = new PluginManifest
        {
            Id = "countdown",
            Name = "倒计时",
            Version = "1.0.0",
            Entry = "StudyJourney.Plugin.Countdown.dll",
            CoreApi = "1.x",
            ConfigFields = new[] { "FontSize", "FontFamily", "GaokaoDateStr" },
        };
        var exam = new PluginManifest
        {
            Id = "exam",
            Name = "考试模式",
            Version = "1.0.0",
            Entry = "StudyJourney.Plugin.Exam.dll",
            CoreApi = "1.x",
            ConfigFields = new[] { "FontSize", "ExamModeFontSize" },
        };

        // 输入顺序故意 exam 在前：判定结果必须与加载顺序无关（字典序 countdown < exam）
        var report = ConfigFieldClaimReport.Resolve(new[] { exam, countdown });

        Assert.Equal("countdown", report.FieldOwners["FontSize"]);
        Assert.Contains("exam", report.RejectedPluginIds);
        Assert.DoesNotContain("countdown", report.RejectedPluginIds);

        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("FontSize", conflict.FieldName);
        Assert.Equal("countdown", conflict.FirstPluginId);
        Assert.Equal("exam", conflict.SecondPluginId);
        Assert.Contains("countdown", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("exam", conflict.Message, StringComparison.Ordinal);

        // 被拒载的插件不许"半认领"：ExamModeFontSize 也不能记到它名下
        Assert.False(report.FieldOwners.ContainsKey("ExamModeFontSize"));
    }
}
