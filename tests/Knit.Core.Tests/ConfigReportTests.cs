using System.Reflection;
using System.Text.Json.Serialization;
using Knit.Core.Config;

namespace Knit.Core.Tests;

/// <summary>
/// The key table sits beside <see cref="KnitConfig"/> rather than replacing
/// it, so something has to keep the two in agreement — two lists maintained by
/// hand is the failure this exists to stop.
/// </summary>
public class ConfigKeyTableTests
{
    [Fact]
    public void Every_record_key_has_a_table_entry()
    {
        var recordKeys = typeof(KnitConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(n => n is not null)
            .ToList();

        Assert.NotEmpty(recordKeys);
        foreach (var key in recordKeys)
            Assert.True(ConfigKeys.Find(key!) is not null, $"config key '{key}' has no ConfigKeys entry");
    }

    [Fact]
    public void Every_table_entry_names_a_record_key()
    {
        var recordKeys = typeof(KnitConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .ToHashSet();

        foreach (var key in ConfigKeys.All)
            Assert.Contains(key.Name, recordKeys);
    }

    [Fact]
    public void Defaults_come_from_the_record_so_they_cannot_drift()
    {
        Assert.Equal(new KnitConfig().DueSoonDays.ToString(), ConfigKeys.Find("due_soon_days")!.Default);
        Assert.Equal("", ConfigKeys.Find("handle")!.Default);
    }

    [Fact]
    public void Format_version_and_mounts_are_not_settable()
    {
        Assert.False(ConfigKeys.Find("format_version")!.Settable);
        Assert.False(ConfigKeys.Find("mounts")!.Settable);
        Assert.True(ConfigKeys.Find("handle")!.Settable);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("soon")]
    [InlineData("")]
    public void Due_soon_days_refuses_what_it_cannot_mean(string raw)
        => Assert.NotNull(ConfigKeys.Find("due_soon_days")!.Parse(raw).Error);

    [Fact]
    public void Handle_refuses_whitespace_because_it_is_matched_against_assignees()
    {
        Assert.NotNull(ConfigKeys.Find("handle")!.Parse("two words").Error);
        Assert.Null(ConfigKeys.Find("handle")!.Parse(" alice ").Error);
    }
}

public class ConfigReportTests : IDisposable
{
    private readonly MountTempDir _t = new();

    public void Dispose() => _t.Dispose();

    private ConfigEntry Key(string name) => ConfigReport.Build(_t.Root).Keys.Single(e => e.Name == name);

    [Fact]
    public void An_unset_key_reads_as_default()
    {
        var entry = Key("due_soon_days");

        Assert.False(entry.IsSet);
        Assert.Equal("default", entry.SourceLabel);
        Assert.Equal("3", entry.Value);
    }

    [Fact]
    public void A_key_set_to_its_default_still_reports_the_file_it_came_from()
    {
        _t.Write(ConfigLoader.FileName, """{"due_soon_days":3}""");

        var entry = Key("due_soon_days");

        Assert.True(entry.IsSet);
        Assert.Equal(ConfigLoader.FileName, entry.SourceLabel);
    }

    [Fact]
    public void The_local_layer_wins_and_says_so()
    {
        _t.Write(ConfigLoader.FileName, """{"due_soon_days":5}""");
        _t.Write(ConfigLoader.LocalFileName, """{"due_soon_days":9}""");

        var entry = Key("due_soon_days");

        Assert.Equal("9", entry.Value);
        Assert.Equal(ConfigLoader.LocalFileName, entry.SourceLabel);
    }

    [Fact]
    public void An_unknown_key_is_listed_not_dropped()
    {
        _t.Write(ConfigLoader.FileName, """{"due_soon_day":4}""");

        var entry = ConfigReport.Build(_t.Root).Keys.Single(e => e.Name == "due_soon_day");

        Assert.False(entry.Known);
        Assert.Equal(ConfigLoader.FileName, entry.SourceLabel);
        Assert.Equal("4", entry.Value);
    }

    [Fact]
    public void A_broken_layer_is_named_rather_than_read_as_defaults()
    {
        _t.Write(ConfigLoader.FileName, "{ not json");

        Assert.Equal([ConfigLoader.FileName], ConfigReport.Build(_t.Root).Unreadable);
    }

    [Fact]
    public void Mounts_are_rows_not_a_value()
    {
        _t.Sibling("docs-dir");
        _t.Write(ConfigLoader.FileName, """{"mounts":{"docs":"../docs-dir"}}""");

        var snapshot = ConfigReport.Build(_t.Root);

        Assert.DoesNotContain(snapshot.Keys, e => e.Name == "mounts");
        var mount = Assert.Single(snapshot.Mounts);
        Assert.Equal("docs", mount.Name);
        Assert.True(mount.Resolves);
    }
}
