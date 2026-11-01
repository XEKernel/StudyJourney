using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StudyJourney.Sdk.Context;

/// <summary>
/// 插件配置句柄（CORE_ARCHITECTURE §2.9 / §5.0）。**只认识自己 <c>configFields</c> 那批键**，
/// 读写都经过核心的"摘出来 / 原位写回"，插件拿不到整棵树。
/// </summary>
public interface IPluginConfig
{
    /// <summary>读取本插件的配置节（**深拷贝**，调用方可随意修改，不影响树）。节不存在返回 <c>null</c>。</summary>
    /// <remarks>消费者：所有带配置的插件（Initialize 时读一次，之后按需）</remarks>
    JsonNode? ReadSection();

    /// <summary>读取并反序列化为强类型；节不存在时返回 <c>new T()</c>。类型不符必须回落默认值并记 Warn，不许抛。</summary>
    /// <param name="context">插件自带的 <see cref="JsonSerializerContext"/>（AOT/裁剪友好，避免反射）。</param>
    /// <remarks>消费者：同上（绝大多数插件用这个便捷重载）</remarks>
    Result<T> ReadSection<T>(JsonSerializerContext context) where T : class, new();

    /// <summary>节级写回：只替换自己那批键，其余节点原样保留。</summary>
    /// <remarks>消费者：设置页 Apply、自动化规则保存、远程账号变更</remarks>
    Result WriteSection(JsonNode section);
}
