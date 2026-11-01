using System.Reflection;
using StudyJourney.Sdk;
using Xunit;

namespace StudyJourney.Sdk.Tests;

/// <summary>
/// 测试 6：<see cref="IPluginContext"/> 的「六类别各恰好一个出口」契约。
///
/// 守的是 E1 那条最容易失守的纪律：这个接口每长一个成员，所有插件都要重新理解它。
/// 它历史上差一点变成"属性袋"（<c>ctx.Schedule</c> / <c>ctx.Notifications</c> …），
/// 那就是 <c>App.axaml.cs</c> 的老路换了个名字。这组测试把"六个"钉成硬约束：
/// 想加第 7 个，必须先改这里 —— 而改这里就必须写下理由。
/// </summary>
public sealed class IPluginContextContractTests
{
    /// <summary>
    /// ⚠ 必须排除 <c>IsSpecialName</c>：属性访问器（<c>get_X</c>）在反射里也是 <see cref="MethodInfo"/>。
    /// 这个坑在 <c>SettingsPageContractTests</c> 上已经踩过一次（把"2 方法 + 1 属性"数成"3 方法"）。
    /// </summary>
    private static readonly MethodInfo[] Methods = typeof(IPluginContext)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => !m.IsSpecialName)
        .ToArray();

    [Fact(DisplayName = "测试6-a：恰好 6 个成员（六类别各一个出口）")]
    public void Has_Exactly_Six_Members()
    {
        Assert.Equal(6, Methods.Length);
    }

    [Fact(DisplayName = "测试6-b：六个成员的名字与顺序固定")]
    public void Member_Names_Are_Fixed()
    {
        // 顺序即契约：读接口的人按这个顺序理解"服务 / 通信 / 配置 / 存储 / 日志 / UI"
        var expected = new[]
        {
            "GetService", "GetEventBus", "GetConfig", "GetStorage", "GetLogger", "GetUI",
        };

        Assert.Equal(expected, Methods.Select(m => m.Name).ToArray());
    }

    [Fact(DisplayName = "测试6-c：GetUI 返回 IShellService（第 6 类是 UI，不是业务服务）")]
    public void GetUI_Returns_IShellService()
    {
        var getUi = Methods.Single(m => m.Name == "GetUI");

        Assert.Equal(typeof(IShellService), getUi.ReturnType);
        Assert.Empty(getUi.GetParameters());
    }

    [Fact(DisplayName = "测试6-d：五类基础设施都是无参方法，只有 GetService 是泛型")]
    public void Only_GetService_Is_Generic()
    {
        var generic = Methods.Where(m => m.IsGenericMethodDefinition).ToArray();

        Assert.Single(generic);
        Assert.Equal("GetService", generic[0].Name);
        Assert.Single(generic[0].GetGenericArguments());

        foreach (var m in Methods.Where(m => m.Name != "GetService"))
            Assert.Empty(m.GetParameters());
    }

    [Fact(DisplayName = "测试6-e：没有 GetService(Type) 重载（按运行期类型查找已删）")]
    public void No_RuntimeType_Overload()
    {
        // 只允许一个 GetService：泛型的那个。它返回 object?（T 替换前）不带 Type 参数。
        var overloads = Methods.Where(m => m.Name == "GetService").ToArray();

        Assert.Single(overloads);
        Assert.True(overloads[0].IsGenericMethodDefinition);
        Assert.DoesNotContain(
            overloads,
            m => m.GetParameters().Any(p => p.ParameterType == typeof(Type)));
    }

    [Fact(DisplayName = "测试6-f：全部是方法，没有任何属性（不是属性袋）")]
    public void Has_No_Properties()
    {
        var properties = typeof(IPluginContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(properties);

        // 反向：也没有字段、没有事件
        Assert.Empty(typeof(IPluginContext).GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(IPluginContext).GetEvents(BindingFlags.Public | BindingFlags.Instance));
    }
}
