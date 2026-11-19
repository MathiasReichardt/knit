using System.Text.Json;
using Knit.Core.Paths;

namespace Knit.Cli.Tests;

/// <summary>
/// The user-level config layer. Skills install globally, so one knit
/// serves many repos — but every preference was per-repo, and <c>handle</c> /
/// <c>display</c> describe the person, not the project.
/// </summary>
[Collection(nameof(CliCollection))]
public class GlobalConfigTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static JsonElement Keys(TempKnitRoot root)
    {
        var r = root.Run("config", "list", "--json");
        Assert.Equal(0, r.ExitCode);
        return JsonDocument.Parse(r.Stdout).RootElement.GetProperty("keys");
    }

    private static string RepoConfig(TempKnitRoot root) =>
        File.ReadAllText(Path.Combine(root.Path, ".knit", "config.json"));

    private static string LocalConfig(TempKnitRoot root) =>
        File.ReadAllText(Path.Combine(root.Path, ".knit", "config.local.json"));

    [Fact]
    public void The_suite_never_reads_the_developers_own_user_config()
    {
        // Pinned by TestEnvironment for the assembly and narrowed per test by
        // TempKnitRoot. Without this the suite would read a real `handle` and
        // `due_soon_days`, and `config set --global` would write them.
        Assert.StartsWith(Path.GetTempPath(), UserPaths.ConfigDir, StringComparison.OrdinalIgnoreCase);

        using var root = new TempKnitRoot();
        Assert.Equal(root.ConfigHome, UserPaths.ConfigDir);
    }

    [Fact]
    public void A_global_value_applies_in_a_repo_that_never_set_it()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"due_soon_days":7}""");
        Init(root);

        Assert.Equal("7", root.Run("config", "get", "due_soon_days").Stdout.Trim());
        Assert.Equal("global", Keys(root).GetProperty("due_soon_days").GetProperty("source").GetString());
    }

    [Fact]
    public void A_committed_repo_setting_beats_a_personal_global()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"due_soon_days":7}""");
        Init(root);

        Assert.Equal(0, root.Run("config", "set", "due_soon_days", "2").ExitCode);

        Assert.Equal("2", root.Run("config", "get", "due_soon_days").Stdout.Trim());
        Assert.Equal("config.json", Keys(root).GetProperty("due_soon_days").GetProperty("source").GetString());
    }

    [Fact]
    public void Set_global_writes_the_user_level_file_not_the_repo()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("config", "set", "--global", "due_soon_days", "9").ExitCode);

        Assert.Equal("9", root.Run("config", "get", "due_soon_days").Stdout.Trim());
        Assert.DoesNotContain("due_soon_days", RepoConfig(root));
        Assert.True(File.Exists(Path.Combine(root.ConfigHome, "config.json")));
    }

    [Fact]
    public void Set_global_refuses_a_repo_only_key()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "set", "--global", "format_version", "2");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("repo-only", r.Stderr);
    }

    [Fact]
    public void Local_and_global_name_different_files_so_both_at_once_is_refused()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "set", "--local", "--global", "due_soon_days", "4");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--local and --global", r.Stderr);
    }

    [Fact]
    public void A_hand_edited_repo_only_key_is_reported_rather_than_silently_dropped()
    {
        // `config set --global` refuses it, but nothing stops a text editor.
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"format_version":9,"handle":"alice"}""");
        Init(root);

        var r = root.Run("config", "list", "--json");
        var ignored = Assert.Single(
            JsonDocument.Parse(r.Stdout).RootElement.GetProperty("ignored_global").EnumerateArray());
        Assert.Equal("format_version", ignored.GetProperty("key").GetString());
        Assert.Contains("repo-only", ignored.GetProperty("reason").GetString());

        var verify = root.Run("verify", "--json");
        // Not a repo defect: the file belongs to the machine, so it is named
        // and the repo still verifies clean.
        Assert.Equal(0, verify.ExitCode);
        var note = Assert.Single(
            JsonDocument.Parse(verify.Stdout).RootElement.GetProperty("global_config").EnumerateArray());
        Assert.Equal("format_version", note.GetProperty("key").GetString());
    }

    [Fact]
    public void Config_list_names_the_file_behind_every_layer()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "list", "--json");
        var layers = JsonDocument.Parse(r.Stdout).RootElement.GetProperty("layers");

        Assert.Equal(
            Path.Combine(root.ConfigHome, "config.json"),
            layers.GetProperty("global").GetString());
        Assert.Contains(".knit", layers.GetProperty("config.json").GetString()!);
    }

    [Fact]
    public void A_global_mount_is_declared_once_for_every_repo()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var docs = Path.Combine(root.Path, "company-docs");
        Directory.CreateDirectory(docs);

        Assert.Equal(0, root.Run("mount", "add", "--global", "docs", docs).ExitCode);

        var r = root.Run("mount", "list", "--json");
        var mount = Assert.Single(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts").EnumerateArray());
        Assert.Equal("global", mount.GetProperty("source").GetString());
        Assert.True(mount.GetProperty("resolves").GetBoolean());
    }

    [Fact]
    public void A_global_mount_path_must_be_absolute()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("mount", "add", "--global", "docs", "../company-docs");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("absolute", r.Stderr);
    }

    [Fact]
    public void A_repo_mount_shadows_the_global_one_and_the_listing_says_so()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var shared = Path.Combine(root.Path, "shared-docs");
        var vendored = Path.Combine(root.Path, "vendored");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(vendored);
        Assert.Equal(0, root.Run("mount", "add", "--global", "docs", shared).ExitCode);

        Assert.Equal(0, root.Run("mount", "add", "docs", "vendored").ExitCode);

        var r = root.Run("mount", "list", "--json");
        var mount = Assert.Single(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts").EnumerateArray());
        Assert.Equal("repo", mount.GetProperty("source").GetString());
        var shadowed = Assert.Single(mount.GetProperty("shadowed").EnumerateArray());
        Assert.Equal("global", shadowed.GetProperty("source").GetString());
    }

    [Fact]
    public void A_global_mount_nothing_references_does_not_fail_this_repo()
    {
        // Someone else's directory, missing on this machine: only a problem
        // where a KB source actually rides on it.
        using var root = new TempKnitRoot();
        var missing = Path.Combine(root.Path, "gone").Replace(Path.DirectorySeparatorChar, '/');
        root.WriteGlobalConfig($$$"""{"mounts":{"docs":"{{{missing}}}"}}""");
        Init(root);

        var r = root.Run("verify", "--json");

        Assert.Equal(0, r.ExitCode);
        Assert.Empty(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("mounts").EnumerateArray());
    }

    [Fact]
    public void Init_inherits_the_identity_instead_of_committing_a_copy_of_it()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"handle":"alice","display":"Alice"}""");

        Assert.Equal(0, root.Run("init", "--non-interactive").ExitCode);

        // Absent from the repo file: a committed handle would tell the next
        // person who clones this repo that they are whoever ran `init`.
        Assert.DoesNotContain("handle", RepoConfig(root));
        Assert.Equal("alice", root.Run("config", "get", "handle").Stdout.Trim());
        Assert.Equal("global", Keys(root).GetProperty("handle").GetProperty("source").GetString());
    }

    [Fact]
    public void Init_keeps_a_handle_that_differs_from_the_inherited_one_out_of_the_committed_config()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"handle":"alice","display":"Alice"}""");

        Assert.Equal(0, root.Run("init", "--handle", "work-alice", "--non-interactive").ExitCode);

        Assert.DoesNotContain("handle", RepoConfig(root));
        Assert.Contains("work-alice", LocalConfig(root));
        Assert.DoesNotContain("display", LocalConfig(root));
        Assert.Equal("work-alice", root.Run("config", "get", "handle").Stdout.Trim());
    }

    [Fact]
    public void Init_names_the_global_layer_so_it_can_be_discovered()
    {
        // The only moment knit has a new user's attention — a config layer
        // nobody hears about is a layer nobody uses.
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--handle", "a", "--display", "A", "--non-interactive");

        Assert.Contains("knit config set --global", r.Stdout);
    }

    [Fact]
    public void Init_offers_nothing_and_writes_no_global_file_when_it_cannot_prompt()
    {
        using var root = new TempKnitRoot();

        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        Assert.False(File.Exists(Path.Combine(root.ConfigHome, "config.json")));
        Assert.DoesNotContain("handle", RepoConfig(root));
        Assert.Contains("\"handle\"", LocalConfig(root));
    }

    [Fact]
    public void Set_handle_without_a_layer_stays_in_this_checkout()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("config", "set", "handle", "bob").ExitCode);

        Assert.DoesNotContain("handle", RepoConfig(root));
        Assert.Contains("bob", LocalConfig(root));
        Assert.False(File.Exists(Path.Combine(root.ConfigHome, "config.json")));
    }

    [Fact]
    public void Unset_handle_without_a_layer_clears_this_checkout_not_the_repo()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("config", "set", "--repo", "handle", "team").ExitCode);

        Assert.Equal(0, root.Run("config", "unset", "handle").ExitCode);

        Assert.DoesNotContain("handle", LocalConfig(root));
        Assert.Equal("team", root.Run("config", "get", "handle").Stdout.Trim());
    }

    [Fact]
    public void Repo_flag_commits_an_identity_and_unset_repo_removes_it()
    {
        using var root = new TempKnitRoot();
        Init(root);

        Assert.Equal(0, root.Run("config", "set", "--repo", "handle", "team").ExitCode);
        Assert.Contains("team", RepoConfig(root));

        Assert.Equal(0, root.Run("config", "unset", "--repo", "handle").ExitCode);
        Assert.DoesNotContain("handle", RepoConfig(root));
    }

    [Fact]
    public void Repo_and_local_name_different_files_so_both_at_once_is_refused()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "unset", "--local", "--repo", "handle");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--local and --repo", r.Stderr);
    }

    [Fact]
    public void Init_names_the_file_each_identity_value_landed_in()
    {
        using var root = new TempKnitRoot();
        root.WriteGlobalConfig("""{"handle":"alice","display":"Alice"}""");

        var r = root.Run("init", "--handle", "work-alice", "--non-interactive");

        Assert.Contains("work-alice → .knit/config.local.json", r.Stdout);
        Assert.Contains("Alice from global", r.Stdout);
    }

    [Fact]
    public void Set_says_when_a_higher_layer_still_wins()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("config", "set", "--global", "handle", "bob");

        Assert.Contains("shadowed by config.local.json", r.Stdout);
        Assert.Contains("knit config unset --local handle", r.Stdout);
    }

    [Fact]
    public void Verify_warns_on_an_identity_in_the_committed_config()
    {
        // Older repos, or a deliberate --repo: the repo layer beats every cloner's own.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("config", "set", "--repo", "handle", "alice").ExitCode);

        var r = root.Run("verify", "--json");

        Assert.Equal(0, r.ExitCode);
        var row = Assert.Single(
            JsonDocument.Parse(r.Stdout).RootElement.GetProperty("committed_identity").EnumerateArray());
        Assert.Equal("handle", row.GetProperty("key").GetString());
        Assert.Equal("alice", row.GetProperty("value").GetString());
        Assert.NotEqual(0, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Init_without_a_global_layer_still_demands_an_identity()
    {
        using var root = new TempKnitRoot();

        var r = root.Run("init", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--handle required", r.Stderr);
    }
}
