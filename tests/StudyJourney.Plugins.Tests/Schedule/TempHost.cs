using System.IO;

namespace StudyJourney.Plugins.Tests.Schedule;

/// <summary>
/// 临时"程序目录"：每个测试一个独立目录，<see cref="ScheduleStore"/> 只认目录不认别的东西。
/// 用完递归删除；删不掉也不让测试失败（Windows 上偶发句柄占用）。
/// </summary>
internal sealed class TempHost : IDisposable
{
    public TempHost()
    {
        Root = Path.Combine(Path.GetTempPath(), "sj-schedule-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>临时目录（扮演 <c>IPluginStorage.HostDirectory</c>）。</summary>
    public string Root { get; }

    /// <summary>该目录下的 <c>schedule.json</c> 路径。</summary>
    public string SchedulePath => Path.Combine(Root, ScheduleFileName);

    private const string ScheduleFileName = "schedule.json";

    /// <summary>把真实样本复制进来（测试只读副本，绝不碰仓库里的原文件）。</summary>
    public void CopyRealSample() => File.Copy(RepoPaths.RealScheduleJson, SchedulePath, overwrite: true);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录残留不影响结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>从测试程序集位置向上找仓库根，定位 v3.3.0 的真实 <c>schedule.json</c> 样本。</summary>
internal static class RepoPaths
{
    /// <summary>真实样本绝对路径（79 节课 / 2 场考试 / 14 个时间模板）。</summary>
    public static string RealScheduleJson { get; } = LocateRealSchedule();

    private static string LocateRealSchedule()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyJourney.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("找不到仓库根（StudyJourney.sln），无法定位真实课表样本。");

        string[] candidates =
        [
            Path.Combine(dir.FullName, "StudyJourney.Avalonia", "bin", "Release", "net10.0", "schedule.json"),
            Path.Combine(dir.FullName, "StudyJourney.Avalonia", "bin", "Debug", "net10.0", "schedule.json"),
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "找不到真实课表样本。请先构建一次旧工程（StudyJourney.Avalonia）以生成 bin/*/net10.0/schedule.json。" +
            $"已尝试：{string.Join(" / ", candidates)}");
    }
}
