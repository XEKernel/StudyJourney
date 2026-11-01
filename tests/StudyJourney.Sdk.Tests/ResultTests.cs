using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Sdk.Tests;

/// <summary>
/// 测试 5-b：<see cref="Result"/> / <see cref="Result{T}"/> 的 Ok / Fail 语义。
///
/// 这两个类型是整个边界的"失败表达"，语义歪一点，
/// 每个调用点的 <c>if (r.IsOk)</c> 就都跟着歪。
/// </summary>
public sealed class ResultTests
{
    [Fact(DisplayName = "测试5-b-1：Result.Ok 是成功、无错误码、无异常")]
    public void Ok_Is_Success()
    {
        var r = Result.Ok();

        Assert.True(r.IsOk);
        Assert.False(r.IsFailure);
        Assert.Equal(string.Empty, r.Code);
        Assert.Equal(string.Empty, r.Message);
        Assert.Null(r.Exception);
    }

    [Fact(DisplayName = "测试5-b-2：Result.Fail 带上 code / message / 可选异常")]
    public void Fail_Carries_Code_And_Message()
    {
        var ex = new InvalidOperationException("boom");
        var r = Result.Fail(ErrorCodes.FieldConflict, "字段 FontSize 已被插件 countdown 占用", ex);

        Assert.False(r.IsOk);
        Assert.True(r.IsFailure);
        Assert.Equal(ErrorCodes.FieldConflict, r.Code);
        Assert.Contains("FontSize", r.Message, StringComparison.Ordinal);
        Assert.Same(ex, r.Exception);
    }

    [Fact(DisplayName = "测试5-b-3：Result.Fail 的异常参数可省略")]
    public void Fail_Exception_Is_Optional()
    {
        var r = Result.Fail(ErrorCodes.SlotOccupied, "槽位已被占用");

        Assert.True(r.IsFailure);
        Assert.Null(r.Exception);
    }

    [Fact(DisplayName = "测试5-b-4：Result<T>.Ok 保留值且 IsOk 为真")]
    public void Generic_Ok_Keeps_Value()
    {
        var r = Result<int>.Ok(42);

        Assert.True(r.IsOk);
        Assert.False(r.IsFailure);
        Assert.Equal(42, r.Value);
        Assert.Equal(string.Empty, r.Code);
    }

    [Fact(DisplayName = "测试5-b-5：Result<T>.Fail 的 Value 为 default")]
    public void Generic_Fail_Has_Default_Value()
    {
        var r = Result<string>.Fail(ErrorCodes.ServiceMissing, "缺少 IScheduleProvider");

        Assert.True(r.IsFailure);
        Assert.Null(r.Value);
        Assert.Equal(ErrorCodes.ServiceMissing, r.Code);
        Assert.Equal("缺少 IScheduleProvider", r.Message);
    }

    [Fact(DisplayName = "测试5-b-6：Result 与 Result<T> 不是同一种类型")]
    public void Generic_And_Non_Generic_Are_Distinct()
    {
        // 这条断言守的是"不许把 Result<T> 继承自 Result"这个设计约束：
        // 继承会让"有值"的失败被当成"无值"的失败传递，丢掉 Value 却照样编译。
        Assert.False(typeof(Result).IsAssignableFrom(typeof(Result<int>)));
        Assert.False(typeof(Result<int>).IsAssignableFrom(typeof(Result)));
    }

    [Fact(DisplayName = "测试5-b-7：错误码常量覆盖 §6.8 必需项，且互不重复")]
    public void ErrorCodes_Cover_Required_Set()
    {
        string[] required =
        [
            ErrorCodes.FieldConflict,
            ErrorCodes.SlotOccupied,
            ErrorCodes.SlotUnknown,
            ErrorCodes.ReservedService,
            ErrorCodes.PluginLoadFailed,
            ErrorCodes.DependencyMissing,
        ];

        Assert.All(required, c => Assert.False(string.IsNullOrWhiteSpace(c)));

        // 去重：19 个常量必须两两不同（复制粘贴写错字的典型症状）
        var all = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }
}
