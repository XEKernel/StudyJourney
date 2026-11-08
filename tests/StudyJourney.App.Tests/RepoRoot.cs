namespace StudyJourney.App.Tests;

/// <summary>测试用的仓库根定位。从测试程序集所在目录向上找 StudyJourney.sln。</summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> _path = new(Find);

    public static string Path => _path.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "StudyJourney.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"找不到仓库根（从 {AppContext.BaseDirectory} 向上未命中 StudyJourney.sln）。");
    }

    public static string Combine(params string[] parts)
        => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
}
