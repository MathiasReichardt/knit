using Knit.Core.Config;

namespace Knit.Core.Tests;

/// <summary>
/// The one write path for a config key — <c>knit config set</c>/<c>unset</c>
/// and the web API's rename both go through it.
/// </summary>
public class ConfigWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "knit-configwriter-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ConfigWriterTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, ConfigLoader.FileName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void An_unreadable_layer_is_refused_not_replaced()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Equal(ConfigWriteFailure.Unreadable, ConfigWriter.Set(_path, ConfigLoader.FileName, false, "repo_name", "App").Failure);
        Assert.Equal(ConfigWriteFailure.Unreadable, ConfigWriter.Unset(_path, ConfigLoader.FileName, "repo_name").Failure);
        Assert.Equal("{ not json", File.ReadAllText(_path));
    }

    [Fact]
    public void A_repo_only_key_is_refused_for_the_global_layer() =>
        Assert.Equal(ConfigWriteFailure.Invalid, ConfigWriter.Set(_path, "global", true, "repo_name", "App").Failure);

    [Fact]
    public void Set_keeps_keys_knit_does_not_model()
    {
        File.WriteAllText(_path, """{"custom":1}""");

        Assert.True(ConfigWriter.Set(_path, ConfigLoader.FileName, false, "repo_name", " App ").Succeeded);

        var node = ConfigLoader.ReadFile(_path).Node!;
        Assert.Equal("App", node["repo_name"]!.GetValue<string>());
        Assert.Equal(1, node["custom"]!.GetValue<int>());
    }
}
