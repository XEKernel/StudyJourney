using System.Text.Json;
using StudyJourney.Plugin.Schedule.Models;
using StudyJourney.Sdk;
using StudyJourney.Sdk.Context;

namespace StudyJourney.Plugin.Schedule;

/// <summary>
/// <c>schedule.json</c> 的读写（位置由 <see cref="IPluginStorage.HostDirectory"/> 给出，即 exe 旁）。
///
/// 两条硬规则：
/// <list type="number">
/// <item><b>读只读</b>：文件缺失 / 损坏 / 半截 → 返回空数据 + 记 Error，**绝不改写或删除原文件**。
/// 老师的课表只有一份，读失败时把它覆盖成空文件是不可接受的数据灾难。</item>
/// <item><b>写要原子</b>：先写同目录 <c>.tmp</c> 并 flush 到盘，再 <see cref="File.Replace(string,string,string?)"/>
/// 换名。断电/崩溃时最坏结果是留下一个 <c>.tmp</c>，原文件永远完整可读。</item>
/// </list>
/// </summary>
internal sealed class ScheduleStore
{
    /// <summary>文件名。v3.3.0 遗留位置的固定名字。</summary>
    public const string FileName = "schedule.json";

    private const string TempSuffix = ".tmp";

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private readonly string _path;
    private readonly IPluginLogger? _logger;

    /// <param name="hostDirectory">程序所在目录（<see cref="IPluginStorage.HostDirectory"/>）。</param>
    /// <param name="logger">可选的日志器；测试可传 <c>null</c>。</param>
    public ScheduleStore(string hostDirectory, IPluginLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);
        _path = Path.Combine(hostDirectory, FileName);
        _logger = logger;
    }

    /// <summary>实际读写的绝对路径（诊断 / 日志用）。</summary>
    public string FilePath => _path;

    /// <summary>文件最后写入时刻（UTC）。文件不存在或读取失败 → <see cref="DateTime.MinValue"/>。</summary>
    public DateTime GetLastWriteTimeUtc()
    {
        try
        {
            return File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"读取 schedule.json 时间戳失败：{ex.Message}");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 读整份课表。<b>任何失败都返回空数据，不抛、不改文件。</b>
    /// </summary>
    public ScheduleData Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _logger?.Info($"课表文件不存在，按空课表启动：{_path}");
                return new ScheduleData();
            }

            var bytes = File.ReadAllBytes(_path);
            var span = bytes.AsSpan();
            // 老师可能用记事本另存过（带 BOM）。Utf8JsonReader 不认 BOM，得自己剥掉。
            if (span.StartsWith(Utf8Bom))
                span = span[Utf8Bom.Length..];

            if (span.IsEmpty)
            {
                _logger?.Warn($"课表文件为空：{_path}");
                return new ScheduleData();
            }

            var data = JsonSerializer.Deserialize(span, ScheduleJsonContext.Default.ScheduleData);
            if (data is null)
            {
                _logger?.Warn($"课表文件内容为 null：{_path}");
                return new ScheduleData();
            }

            data.Normalize();
            _logger?.Info($"课表已加载：{data.Entries.Count} 节课 / {data.Exams.Count} 场考试 / " +
                          $"{data.TimeTemplates.Count} 个时间模板 / {data.MakeupDays.Count} 条调休");
            return data;
        }
        catch (Exception ex)
        {
            // 注意：这里**没有**任何写盘动作 —— 损坏的文件必须原样留着，等人来修/来找回。
            _logger?.Error($"课表文件损坏或无法读取，已按空课表继续（原文件未改动）：{_path}", ex);
            return new ScheduleData();
        }
    }

    /// <summary>
    /// 原子写整份课表。<b>调用方负责不要丢字段</b>（尤其是 <see cref="ScheduleData.Exams"/>）。
    /// </summary>
    public Result Save(ScheduleData data)
    {
        if (data is null)
            return Result.Fail(ErrorCodes.IoError, "课表数据为空，未写入");

        var tempPath = _path + TempSuffix;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var payload = JsonSerializer.SerializeToUtf8Bytes(data, ScheduleJsonContext.Default.ScheduleData);

            // ① 全量写进同目录临时文件，并 flush 到物理盘 —— 此时原文件仍是旧的完整版本。
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            // ② 原子换名。备份名传 null：不产生 .bak 垃圾。
            if (File.Exists(_path))
                File.Replace(tempPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, _path);

            _logger?.Info($"课表已保存：{_path}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            TryDeleteTemp(tempPath);
            _logger?.Error($"课表保存失败：{_path}", ex);
            return Result.Fail(ErrorCodes.IoError, "课表保存失败，原文件未受影响", ex);
        }
    }

    private void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"清理临时文件失败：{tempPath}（{ex.Message}）");
        }
    }
}
