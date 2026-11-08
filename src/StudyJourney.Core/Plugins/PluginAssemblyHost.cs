using System.Reflection;
using System.Runtime.CompilerServices;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Core.Plugins;

/// <summary>
/// 程序集层（CORE_ARCHITECTURE §3.1 第 10 步 / §3.2 E6、E10–E13、E18）：
/// 建 ALC → <c>LoadFromAssemblyPath</c> → 找**唯一** <see cref="IPlugin"/> 实现 → 建实例 → 校验 id。
///
/// 与 <see cref="PluginLoadContext"/> 的分工：后者只管「程序集从哪来」，
/// 本类管「拿它做什么」。**不碰服务注册 / 事件 / 生命周期**——那是 <see cref="PluginLoader"/> 的事。
/// </summary>
internal static class PluginAssemblyHost
{
    /// <summary>加载成功时把 <c>Alc</c>/<c>Plugin</c> 写回 <paramref name="slot"/>；失败时置状态并记录原因。</summary>
    public static void Load(DiscoveredPlugin slot, IPluginLogger logger)
    {
        var manifest = slot.Manifest!;
        var entry = Path.Combine(slot.Directory, manifest.Entry);

        if (!File.Exists(entry))
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.EntryMissing, $"入口文件缺失：{manifest.Entry}");
            logger.Error($"插件 {manifest.Id} 的入口文件缺失：{entry}");
            return;
        }

        var alc = new PluginLoadContext(manifest.Id, slot.Directory);
        slot.Alc = alc;

        Assembly assembly;
        try
        {
            assembly = alc.LoadFromAssemblyPath(entry);
        }
        catch (Exception ex)
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed, $"程序集加载失败：{ex.GetType().Name}: {ex.Message}");
            logger.Error($"插件 {manifest.Id} 程序集加载失败", ex);
            Unload(slot);
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed,
                "程序集类型加载失败：" + string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message)));
            logger.Error($"插件 {manifest.Id} 的类型加载失败", ex);
            Unload(slot);
            return;
        }

        // E11：必须恰好一个 IPlugin 实现（抽象类/接口不算）。
        var pluginTypes = types
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(IPlugin).IsAssignableFrom(t))
            .ToList();

        if (pluginTypes.Count != 1)
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.PluginTypeAmbiguous,
                $"程序集里应恰好有 1 个 IPlugin 实现，实际 {pluginTypes.Count} 个");
            logger.Error($"插件 {manifest.Id} 的入口类型不唯一（{pluginTypes.Count} 个）");
            Unload(slot);
            return;
        }

        IPlugin plugin;
        try
        {
            plugin = (IPlugin)Activator.CreateInstance(pluginTypes[0])!;
        }
        catch (Exception ex)
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.PluginLoadFailed, $"插件实例创建失败：{ex.GetType().Name}: {ex.Message}");
            logger.Error($"插件 {manifest.Id} 的实例创建失败", ex);
            Unload(slot);
            return;
        }

        // E13：插件声明的 Id 必须与清单一致。
        if (!string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
        {
            slot.Fail(PluginState.LoadFailed, ErrorCodes.IdMismatch, $"插件 Id「{plugin.Id}」与清单 id「{manifest.Id}」不一致");
            logger.Error($"插件 {manifest.Id} 的 Id 与清单不一致，已拒载");
            Unload(slot);
            return;
        }

        slot.Plugin = plugin;
        slot.State = PluginState.Loaded;
    }

    /// <summary>
    /// 卸载并把**三处强引用**（插件实例 / 上下文 / ALC）全部丢掉，只返回弱引用。
    ///
    /// ⚠ **禁止内联**：持 ALC 强引用的帧必须先返回，回收判定才能观测到（§7 陷阱 11）。
    /// 少清任何一个引用都会让 ALC 永远不回收 —— 这不是理论问题，S10 首版就栽在漏清 Plugin 上。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference? Unload(DiscoveredPlugin slot)
    {
        slot.Plugin = null;
        slot.Context = null;

        var alc = slot.Alc;
        slot.Alc = null;
        if (alc is null)
            return null;

        var weak = new WeakReference(alc);
        alc.Unload();
        return weak;
    }

    /// <summary>
    /// 回收判定（§3.4 第 2.9 步）：只记 Warn、不阻断。判据是「功能注销成功 + 下次不再加载」，
    /// 不是「内存立刻下降」。
    /// </summary>
    public static void VerifyCollected(string pluginId, WeakReference weak, IPluginLogger logger)
    {
        if (PluginLoadContext.VerifyUnloaded(weak, out var detail))
            logger.Debug($"插件 {pluginId} 的 ALC 已回收");
        else
            logger.Warn($"插件 {pluginId} 已卸载但内存未释放（不阻断）：{detail}");
    }
}
