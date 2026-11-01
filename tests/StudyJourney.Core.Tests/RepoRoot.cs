using System.Xml.Linq;

namespace StudyJourney.Core.Tests;

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

    /// <summary>读一个 csproj 并返回其 ProjectReference 的 Include 值（统一成正斜杠）。</summary>
    public static IReadOnlyList<string> ReadProjectReferences(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => ((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Where(s => s.Length > 0)
            .ToList();
    }
}
