using System.Text.Json;
using System.Text.Json.Nodes;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Dto;
using Xunit;

namespace StudyJourney.Sdk.Tests;

/// <summary>
/// 测试 7：契约 DTO 的**序列化往返**（S4-4）。
///
/// 与 <see cref="PluginManifestTests"/> 的分工：那边测"手写 plugin.json 读得回来"，
/// 这边测"契约类型作为数据载体时，写出去再读回来会不会走形"——
/// 丢的是字段、顺序还是类型，三种走形造成的后果完全不同：
/// <list type="bullet">
/// <item>丢字段 → 消费方拿到默认值，行为静默错误；</item>
/// <item>丢顺序 → <c>configFields</c> 的"键原位写回"结果不可预测；</item>
/// <item>丢类型 → <c>PeriodType</c> 变成字符串，落盘后老版本读不了（I-4.1 破裂）。</item>
/// </list>
///
/// ⚠ **属性名大小写不是契约**：这些 DTO 是**进程内投影**，不落盘、不走 HTTP
/// （远程插件的网页 JSON 由它自己定义）。所以 <see cref="JsonSerializerOptions"/>
/// 没设 <c>PropertyNamingPolicy</c>，序列化出的键就是 C# 属性名（<c>IsOk</c> / <c>Type</c>），
/// 与 <see cref="PluginManifest"/> 那种**手写文件**用 <c>[JsonPropertyName]</c> 定 camelCase 是两回事。
/// 将来若某个 DTO 真的要落盘，那天才加 <c>[JsonPropertyName]</c>，并在这里补一条断言。
/// </summary>
public sealed class DtoRoundTripTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private static PluginManifest CountdownManifest() => new()
    {
        Id = "countdown",
        Name = "倒计时",
        Version = "1.2.3",
        Entry = "StudyJourney.Plugin.Countdown.dll",
        CoreApi = "1.x",
        Dependencies = ["schedule"],
        ConfigFields = ["CountdownTarget", "CountdownMode"],
        ConfigSection = null,
        ConfigFieldsShared = null,
        Description = "高考倒计时与每日一言",
        Author = "XEKernel",
        MinHostVersion = "4.0.0",
    };

    [Fact(DisplayName = "测试7-a：PluginManifest 全字段往返，逐字段相等（含 configFields 两项）")]
    public void Manifest_Full_RoundTrip()
    {
        var original = CountdownManifest();

        var back = JsonSerializer.Deserialize<PluginManifest>(
            JsonSerializer.Serialize(original, Options), Options);

        Assert.NotNull(back);
        Assert.Equal(original.Id, back!.Id);
        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.Version, back.Version);
        Assert.Equal(original.Entry, back.Entry);
        Assert.Equal(original.CoreApi, back.CoreApi);
        Assert.Equal(original.Description, back.Description);
        Assert.Equal(original.Author, back.Author);
        Assert.Equal(original.MinHostVersion, back.MinHostVersion);

        Assert.Equal(original.Dependencies, back.Dependencies);
        Assert.Equal(original.ConfigFields, back.ConfigFields);

        // ⚠ 不用 Assert.Equal(original, back)：record 的自动相等对
        //   IReadOnlyList<string> 走的是**引用相等**，两个内容相同的列表也不相等。
        //   逐字段比才是这里想验证的东西。
    }

    [Fact(DisplayName = "测试7-b：configSection 省略时为 null（可选字段不产生空壳）")]
    public void ConfigSection_Is_Null_When_Omitted()
    {
        const string json = """
        {
          "id": "countdown",
          "name": "倒计时",
          "version": "1.2.3",
          "entry": "StudyJourney.Plugin.Countdown.dll",
          "coreApi": "1.x",
          "configFields": ["CountdownTarget", "CountdownMode"]
        }
        """;

        var m = JsonSerializer.Deserialize<PluginManifest>(json, Options);

        Assert.NotNull(m);
        Assert.Null(m!.ConfigSection);
        Assert.Null(m.ConfigFieldsShared);
        Assert.Equal(new[] { "CountdownTarget", "CountdownMode" }, m.ConfigFields.ToArray());
    }

    [Fact(DisplayName = "测试7-c：configFields 的顺序原样保留（原位写回依赖它）")]
    public void ConfigFields_Keep_Order()
    {
        var original = CountdownManifest();

        var back = JsonSerializer.Deserialize<PluginManifest>(
            JsonSerializer.Serialize(original, Options), Options);

        // 顺序影响 §5.0「键原位写回」后的文件形态：顺序变了，字节级往返（A0-3）就不成立
        Assert.Equal(
            new[] { "CountdownTarget", "CountdownMode" },
            back!.ConfigFields.ToArray());
    }

    [Fact(DisplayName = "测试7-d：Result<int>.Ok 经 JSON 往返，值类型保真")]
    public void Generic_Ok_RoundTrips_Int()
    {
        var json = JsonSerializer.Serialize(Result<int>.Ok(42), Options);
        var back = JsonSerializer.Deserialize<Result<int>>(json, Options);

        Assert.NotNull(back);
        Assert.True(back!.IsOk);
        Assert.Equal(42, back.Value);
        Assert.Equal(string.Empty, back.Code);
    }

    [Fact(DisplayName = "测试7-e：Result<IReadOnlyList<string>>.Ok 往返，元素与顺序保真")]
    public void Generic_Ok_RoundTrips_List()
    {
        IReadOnlyList<string> payload = ["数学", "英语", "物理"];

        var json = JsonSerializer.Serialize(Result<IReadOnlyList<string>>.Ok(payload), Options);
        var back = JsonSerializer.Deserialize<Result<IReadOnlyList<string>>>(json, Options);

        Assert.NotNull(back);
        Assert.True(back!.IsOk);
        Assert.Equal(payload, back.Value);
    }

    [Fact(DisplayName = "测试7-f：Result<T>.Fail 往返，错误码与消息保真，Value 为默认值")]
    public void Generic_Fail_RoundTrips()
    {
        var original = Result<int>.Fail(ErrorCodes.FieldConflict, "字段 FontSize 已被 countdown 占用");

        var back = JsonSerializer.Deserialize<Result<int>>(
            JsonSerializer.Serialize(original, Options), Options);

        Assert.NotNull(back);
        Assert.False(back!.IsOk);
        Assert.True(back.IsFailure);
        Assert.Equal(ErrorCodes.FieldConflict, back.Code);
        Assert.Equal(original.Message, back.Message);
        Assert.Equal(0, back.Value);
    }

    [Fact(DisplayName = "测试7-g：Result（无泛型）与 Result<T> 的 JSON 形状不同，不会互相误解")]
    public void Non_Generic_Result_Json_Shape()
    {
        var failure = JsonSerializer.Serialize(Result.Fail(ErrorCodes.IoError, "写盘失败"), Options);
        var obj = JsonNode.Parse(failure)!.AsObject();

        Assert.False(obj["IsOk"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.IoError, obj["Code"]!.GetValue<string>());

        // 无泛型 Result 没有 Value 键 —— 这正是"两个独立类型"在序列化层面的体现
        Assert.False(obj.ContainsKey("Value"));
    }

    [Fact(DisplayName = "测试7-h：ScheduledPeriod 往返，PeriodType 以整数传递（I-4.1 同性质）")]
    public void ScheduledPeriod_RoundTrips_With_Int_Enum()
    {
        var original = new ScheduledPeriod
        {
            DayOfWeek = 1,
            Period = 3,
            Subject = "数学",
            StartTimeStr = "08:00",
            EndTimeStr = "08:45",
            Type = PeriodType.Evening,
        };

        var json = JsonSerializer.Serialize(original, Options);
        var node = JsonNode.Parse(json)!.AsObject();

        // 枚举必须是整数 2，不能是 "Evening"。理由与 I-4.1 同性质：
        // 字符串化会让"改名即破坏兼容"，而整数只要求"不重排"。
        Assert.Equal(2, node["Type"]!.GetValue<int>());
        Assert.DoesNotContain("\"Evening\"", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<ScheduledPeriod>(json, Options);

        Assert.Equal(original, back);
        Assert.Equal("08:00", back!.StartTimeStr);   // HH:mm 零填充，格式在往返中不变形
    }
}
