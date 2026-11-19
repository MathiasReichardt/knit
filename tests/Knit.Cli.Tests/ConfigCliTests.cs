using System.Text.Json;
using Knit.Core.Config;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit config` — the surface every config key had shipped
/// without: what a key reads as, and whether that value was chosen or fell
/// back to a default.
/// </summary>
[Collection(nameof(CliCollection))]
public class ConfigCliTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "A", "--non-interactive").ExitCode);

    private static JsonElement Keys(TempKnitRoot root)
    {
        var r = root.Run("config", "list", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("keys");
    }

    private static string ConfigText(TempKnitRoot root, string file) =>
        File.ReadAllText(Path.Combine(root.Path, ".knit", file));

    [Fact]
    public void An_unset_key_reads_default_and_a_set_one_names_its_file()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var keys = Keys(root);
        Assert.Equal("default", keys.GetProperty("due_soon_days").GetProperty("source").GetString());
        Assert.False(keys.GetProperty("due_soon_days").GetProperty("is_set").GetBoolean());
        Assert.Equal(ConfigLoader.LocalFileName, keys.GetProperty("handle").GetProperty("source").GetString());
    }

    [Fact]
    public void Set_writes_only_that_key_and_the_rest_stay_absent()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("config", "set", "due_soon_days", "7").ExitCode);

        var text = ConfigText(root, ConfigLoader.FileName);
        Assert.Contains("due_soon_days", text);
        Assert.DoesNotContain("mounts", text);
        Assert.Equal("7", Keys(root).GetProperty("due_soon_days").GetProperty("value").GetString());
    }

    [Fact]
    public void Local_beats_repo_and_the_source_column_says_which()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("config", "set", "due_soon_days", "5").ExitCode);
        Assert.Equal(0, root.Run("config", "set", "--local", "due_soon_days", "9").ExitCode);

        var entry = Keys(root).GetProperty("due_soon_days");
        Assert.Equal("9", entry.GetProperty("value").GetString());
        Assert.Equal(ConfigLoader.LocalFileName, entry.GetProperty("source").GetString());
    }

    [Fact]
    public void Unset_restores_the_default()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("config", "set", "due_soon_days", "7").ExitCode);

        Assert.Equal(0, root.Run("config", "unset", "due_soon_days").ExitCode);

        var entry = Keys(root).GetProperty("due_soon_days");
        Assert.Equal("3", entry.GetProperty("value").GetString());
        Assert.Equal("default", entry.GetProperty("source").GetString());
    }

    [Fact]
    public void Unset_of_a_key_that_is_not_set_says_so_instead_of_succeeding()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "unset", "due_soon_days", "--json");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("not set", r.Stderr);
    }

    [Fact]
    public void Get_prints_the_bare_value_so_it_can_be_captured()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "get", "handle");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("alice", r.Stdout.Trim());
    }

    [Fact]
    public void An_invalid_value_is_refused_by_name_and_range()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "set", "due_soon_days", "-3", "--json");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("due_soon_days", r.Stderr);
        Assert.DoesNotContain("due_soon_days", ConfigText(root, ConfigLoader.FileName));
    }

    [Fact]
    public void Format_version_is_refused_with_the_reason()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "set", "format_version", "9", "--json");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("cannot be set", r.Stderr);
    }

    [Fact]
    public void An_unknown_key_is_refused_on_write_and_listed_on_read()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var typo = root.Run("config", "set", "due_soon_day", "4", "--json");
        Assert.NotEqual(0, typo.ExitCode);
        Assert.Contains("unknown config key", typo.Stderr);

        File.WriteAllText(
            Path.Combine(root.Path, ".knit", ConfigLoader.FileName),
            """{"handle":"alice","due_soon_day":4}""");

        var entry = Keys(root).GetProperty("due_soon_day");
        Assert.False(entry.GetProperty("known").GetBoolean());
        Assert.Equal(ConfigLoader.FileName, entry.GetProperty("source").GetString());
    }

    [Fact]
    public void A_config_that_does_not_parse_is_reported_by_list_and_by_verify()
    {
        using var root = new TempKnitRoot();
        Init(root);
        File.WriteAllText(Path.Combine(root.Path, ".knit", ConfigLoader.LocalFileName), "{ not json");

        var list = root.Run("config", "list", "--json");
        Assert.NotEqual(0, list.ExitCode);
        Assert.Contains(ConfigLoader.LocalFileName, list.Stderr);

        var verify = root.Run("verify", "--json");
        var strays = JsonDocument.Parse(verify.Stdout).RootElement.GetProperty("stray_files")
            .EnumerateArray().Select(s => s.GetProperty("source").GetString()!).ToList();
        Assert.Contains(strays, s => s.EndsWith(ConfigLoader.LocalFileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ConfigLoader.FileName, "mount", "add", "docs", "../docs")]
    [InlineData(ConfigLoader.FileName, "mount", "remove", "docs")]
    [InlineData(ConfigLoader.FileName, "config", "set", "requires.knit_root", "clone it")]
    [InlineData(ConfigLoader.FileName, "config", "unset", "requires.knit_root")]
    [InlineData(ConfigLoader.LocalFileName, "mount", "add", "docs", "../docs", "--local")]
    [InlineData(ConfigLoader.LocalFileName, "root", "--local")]
    public void A_writer_refuses_a_config_file_that_does_not_parse(string file, params string[] args)
    {
        using var root = new TempKnitRoot();
        Init(root);
        File.WriteAllText(Path.Combine(root.Path, ".knit", file), "{ not json");

        var r = root.Run(args);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("does not parse", r.Stderr);
        Assert.Equal("{ not json", ConfigText(root, file));
    }

    [Fact]
    public void Setup_does_not_overwrite_a_global_config_that_does_not_parse()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var global = Path.Combine(root.ConfigHome, ConfigLoader.FileName);
        Directory.CreateDirectory(root.ConfigHome);
        File.WriteAllText(global, "{ not json");

        var r = root.Run("config", "setup", "--handle", "bob", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Equal("{ not json", File.ReadAllText(global));
    }

    [Fact]
    public void Mounts_are_listed_as_rows_not_as_a_value()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var dir = $"../docs-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(root.Path, dir)));
        Assert.Equal(0, root.Run("mount", "add", "docs", dir).ExitCode);

        var payload = JsonDocument.Parse(root.Run("config", "list", "--json").Stdout).RootElement;

        Assert.False(payload.GetProperty("keys").TryGetProperty("mounts", out _));
        Assert.Equal("docs", payload.GetProperty("mounts")[0].GetProperty("name").GetString());
    }
}
