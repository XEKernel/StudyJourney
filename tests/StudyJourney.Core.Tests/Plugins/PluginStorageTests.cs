using StudyJourney.Core.Plugins;
using Xunit;

namespace StudyJourney.Core.Tests.Plugins;

/// <summary>
/// M1-B 前置：<see cref="PluginStorage"/> 三个目录语义。
///
/// 重点在 <c>HostDirectory</c>——课表插件靠它读写程序目录的 `schedule.json`。
/// 这个属性是 2026-11-10 新增的（原 <c>IPluginStorage</c> 只暴露 data/ 与 plugins/，
/// 导致课表插件无法在原地读写 v3.3.0 的遗留数据文件）。
/// </summary>
public sealed class PluginStorageTests : IDisposable
{
    private readonly string _root;
    private readonly string _pluginsRoot;
    private readonly string _dataRoot;

    public PluginStorageTests()
    {
        // 造一个仿真分发目录：<root>/plugins/ 与 <root>/data/ 同级
        _root = Path.Combine(Path.GetTempPath(), "sj-storage-" + Guid.NewGuid().ToString("N")[..8]);
        _pluginsRoot = Path.Combine(_root, "plugins");
        _dataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(_pluginsRoot);
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* 临时目录清不掉不影响断言 */ }
    }

    [Fact(DisplayName = "M1B-01 HostDirectory = data/ 的上一级（即程序根），不是 data 本身")]
    public void HostDirectory_IsProgramRoot()
    {
        var storage = new PluginStorage("schedule", _pluginsRoot, _dataRoot);

        Assert.Equal(Path.GetFullPath(_root), storage.HostDirectory);
        Assert.NotEqual(Path.GetFullPath(_dataRoot), storage.HostDirectory);
    }

    [Fact(DisplayName = "M1B-02 三个目录互不重叠，且 DataDirectory 被自动创建")]
    public void Directories_AreDistinct_AndDataDirectoryCreated()
    {
        var storage = new PluginStorage("schedule", _pluginsRoot, _dataRoot);

        Assert.Equal(Path.Combine(Path.GetFullPath(_pluginsRoot), "schedule"), storage.PluginDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(_dataRoot), "schedule"), storage.DataDirectory);
        Assert.True(Directory.Exists(storage.DataDirectory));

        // 三个路径两两不同——写错一个就会"配置和数据落在同一个目录"
        var all = new[] { storage.HostDirectory, storage.PluginDirectory, storage.DataDirectory };
        Assert.Equal(3, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact(DisplayName = "M1B-03 HostDirectory 能拼到 schedule.json：这条链就是课表插件的数据入口")]
    public void HostDirectory_ReachesLegacyScheduleFile()
    {
        var legacyFile = Path.Combine(_root, "schedule.json");
        File.WriteAllText(legacyFile, "{\"Entries\":[]}");

        var storage = new PluginStorage("schedule", _pluginsRoot, _dataRoot);
        var resolved = Path.Combine(storage.HostDirectory, "schedule.json");

        Assert.True(File.Exists(resolved), "课表插件必须能按 HostDirectory 找到遗留 schedule.json");
        Assert.Equal(Path.GetFullPath(legacyFile), Path.GetFullPath(resolved));
    }

    [Theory(DisplayName = "M1B-04 ResolvePath 拒绝越界（../ 与绝对路径）")]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("sub/../../escape.txt")]
    public void ResolvePath_RejectsEscape(string relative)
    {
        var storage = new PluginStorage("schedule", _pluginsRoot, _dataRoot);
        Assert.Throws<ArgumentException>(() => storage.ResolvePath(relative));
    }

    [Fact(DisplayName = "M1B-05 ResolvePath 正常相对路径落在 DataDirectory 内")]
    public void ResolvePath_AllowsInnerPath()
    {
        var storage = new PluginStorage("schedule", _pluginsRoot, _dataRoot);
        var path = storage.ResolvePath(Path.Combine("cache", "today.json"));

        Assert.StartsWith(storage.DataDirectory, path, StringComparison.OrdinalIgnoreCase);
    }
}
