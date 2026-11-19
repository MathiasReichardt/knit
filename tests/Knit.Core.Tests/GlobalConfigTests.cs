using System.Text.Json.Nodes;
using Knit.Core.Config;
using Knit.Core.Paths;

namespace Knit.Core.Tests;

/// <summary>
/// What a user-level config may say. The split is the point — a
/// blind merge of the whole record is the bug it exists to avoid — so the rule
/// is tested as a rule, without a machine-wide file in the way.
/// </summary>
public class GlobalConfigTests
{
    private static GlobalLayer Sanitize(string json) =>
        ConfigLoader.SanitizeGlobal((JsonObject)JsonNode.Parse(json)!);

    [Fact]
    public void Person_scoped_keys_pass_through()
    {
        var layer = Sanitize("""{"handle":"alice","due_soon_days":7}""");

        Assert.Empty(layer.Ignored);
        Assert.Equal("alice", layer.Node!["handle"]!.GetValue<string>());
    }

    [Fact]
    public void A_repo_only_key_is_dropped_and_named()
    {
        var layer = Sanitize("""{"format_version":1,"handle":"alice"}""");

        Assert.Null(layer.Node!["format_version"]);
        var ignored = Assert.Single(layer.Ignored);
        Assert.Equal("format_version", ignored.Name);
        Assert.Contains("repo-only", ignored.Reason);
    }

    [Fact]
    public void An_absolute_mount_is_kept()
    {
        var path = Path.Combine(Path.GetTempPath(), "company-docs").Replace(Path.DirectorySeparatorChar, '/');
        var layer = Sanitize($$$"""{"mounts":{"docs":"{{{path}}}"}}""");

        Assert.Empty(layer.Ignored);
        Assert.NotNull(layer.Node!["mounts"]!["docs"]);
    }

    [Fact]
    public void A_home_anchored_mount_is_kept()
    {
        var layer = Sanitize("""{"mounts":{"docs":"~/company-docs"}}""");

        Assert.Empty(layer.Ignored);
        Assert.NotNull(layer.Node!["mounts"]!["docs"]);
    }

    [Fact]
    public void A_relative_mount_would_mean_a_different_directory_per_repo_so_it_is_dropped()
    {
        var layer = Sanitize("""{"mounts":{"docs":"../company-docs"},"handle":"alice"}""");

        // The key goes with it when nothing usable is left, so no caller sees
        // an empty mounts table that reads as "declared".
        Assert.Null(layer.Node!["mounts"]);
        Assert.Equal("alice", layer.Node["handle"]!.GetValue<string>());
        var ignored = Assert.Single(layer.Ignored);
        Assert.Equal("mounts.docs", ignored.Name);
    }

    [Fact]
    public void A_mixed_mounts_map_keeps_only_the_absolute_ones()
    {
        var layer = Sanitize("""{"mounts":{"docs":"~/docs","specs":"../specs"}}""");

        var mounts = (JsonObject)layer.Node!["mounts"]!;
        Assert.True(mounts.ContainsKey("docs"));
        Assert.False(mounts.ContainsKey("specs"));
        Assert.Equal("mounts.specs", Assert.Single(layer.Ignored).Name);
    }
}

/// <summary>Where the user-level config lives.</summary>
public class UserPathsTests
{
    [Fact]
    public void The_suite_never_reads_the_developers_own_user_config()
    {
        Assert.Equal(TestEnvironment.ConfigHome, UserPaths.ConfigDir);
        Assert.StartsWith(Path.GetTempPath(), UserPaths.ConfigDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_override_replaces_the_os_location_wholesale()
    {
        var previous = Environment.GetEnvironmentVariable(UserPaths.ConfigHomeVariable);
        try
        {
            Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, Path.Combine(Path.GetTempPath(), "knit-cfg"));
            Assert.Equal(Path.Combine(Path.GetTempPath(), "knit-cfg"), UserPaths.ConfigDir);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "knit-cfg", "config.json"), UserPaths.GlobalConfigFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, previous);
        }
    }

    [Fact]
    public void Without_an_override_it_is_a_knit_folder_under_the_os_config_root()
    {
        var previous = Environment.GetEnvironmentVariable(UserPaths.ConfigHomeVariable);
        try
        {
            Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, null);
            Assert.Equal("knit", Path.GetFileName(UserPaths.ConfigDir));
            Assert.True(Path.IsPathRooted(UserPaths.ConfigDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserPaths.ConfigHomeVariable, previous);
        }
    }
}
