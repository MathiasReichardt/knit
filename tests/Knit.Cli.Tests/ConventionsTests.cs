using System.Text.Json;
using Knit.Cli.Output;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>knit conventions</c> — the repo's file under <c>.knit/</c> and
/// the user's beside the user-level config, read together, written per scope.
/// </summary>
[Collection(nameof(CliCollection))]
public class ConventionsTests
{
    private static TempKnitRoot NewRepo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        return root;
    }

    private static string RepoFile(TempKnitRoot root) => Path.Combine(root.Path, ".knit", "conventions.md");

    private static string UserFile(TempKnitRoot root) => Path.Combine(root.ConfigHome, "conventions.md");

    [Fact]
    public void Without_files_read_says_so_and_names_both_paths()
    {
        using var root = NewRepo();

        var r = root.Run("conventions");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("No conventions", r.Stdout);
        Assert.Contains(RepoFile(root), r.Stdout);
        Assert.Contains(UserFile(root), r.Stdout);
    }

    [Fact]
    public void Read_prints_user_then_repo_under_scope_headings()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.RunWithInput("Tag bugs needs-triage.\n", "conventions", "set", "--scope", "repo").ExitCode);
        Assert.Equal(0, root.RunWithInput("Tag mine.\n", "conventions", "set", "--scope", "user").ExitCode);

        var all = root.Run("conventions").Stdout;

        var user = all.IndexOf($"# user conventions — {UserFile(root)}", StringComparison.Ordinal);
        var repo = all.IndexOf($"# repo conventions — {RepoFile(root)}", StringComparison.Ordinal);
        Assert.True(user >= 0 && repo > user, all);
        Assert.Contains("Repo conventions win", all);
        Assert.True(all.IndexOf("Tag mine.", StringComparison.Ordinal) < all.IndexOf("Tag bugs", StringComparison.Ordinal));

        var repoOnly = root.Run("conventions", "--scope", "repo").Stdout;
        Assert.Contains("Tag bugs needs-triage.", repoOnly);
        Assert.DoesNotContain("Tag mine.", repoOnly);
        Assert.DoesNotContain("win a conflict", repoOnly);
    }

    [Fact]
    public void Json_lists_every_requested_scope_with_its_path()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.RunWithInput("Rule.\n", "conventions", "set", "--scope", "repo").ExitCode);

        var rows = JsonDocument.Parse(root.Run("conventions", "--json").Stdout).RootElement.EnumerateArray().ToList();

        Assert.Equal(["user", "repo"], rows.Select(r => r.GetProperty("scope").GetString()));
        Assert.False(rows[0].GetProperty("exists").GetBoolean());
        Assert.False(rows[0].TryGetProperty("text", out _));
        Assert.Equal(UserFile(root), rows[0].GetProperty("path").GetString());
        Assert.Equal("Rule.\n", rows[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Set_writes_utf8_without_bom_with_lf_and_empty_input_removes_the_file()
    {
        using var root = NewRepo();

        var set = root.RunWithInput("a\r\nb  \r\n\r\n", "conventions", "set", "--scope", "repo");

        Assert.Equal(0, set.ExitCode);
        Assert.Equal("a\nb\n"u8.ToArray(), File.ReadAllBytes(RepoFile(root)));

        Assert.Equal(0, root.RunWithInput("  \n", "conventions", "set", "--scope", "repo").ExitCode);
        Assert.False(File.Exists(RepoFile(root)));
    }

    [Fact]
    public void Remove_deletes_the_file_and_a_missing_one_is_no_error()
    {
        using var root = NewRepo();
        Assert.Equal(0, root.RunWithInput("Rule.\n","conventions", "set", "--scope", "repo").ExitCode);

        var removed = root.Run("conventions", "remove", "--scope", "repo");
        var again = root.Run("conventions", "remove", "--scope", "repo", "--json");

        Assert.Equal(0, removed.ExitCode);
        Assert.Contains("removed repo conventions", removed.Stdout);
        Assert.False(File.Exists(RepoFile(root)));
        Assert.Equal(0, again.ExitCode);
        Assert.False(JsonDocument.Parse(again.Stdout).RootElement.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public void User_scope_lives_under_the_config_home()
    {
        using var root = NewRepo();

        Assert.Equal(0, root.RunWithInput("Mine.\n", "conventions", "set", "--scope", "user").ExitCode);

        Assert.Equal("Mine.\n", File.ReadAllText(UserFile(root)));
        Assert.False(File.Exists(RepoFile(root)));
    }

    [Fact]
    public void Set_needs_a_known_scope()
    {
        using var root = NewRepo();

        Assert.NotEqual(0, root.RunWithInput("x", "conventions", "set").ExitCode);
        var bad = root.RunWithInput("x", "conventions", "set", "--scope", "team");
        Assert.Equal((int)ErrorCode.BadArgument, bad.ExitCode);
        Assert.Contains("repo or user", bad.Stderr);
    }

    [Fact]
    public void A_redirected_checkout_reads_the_targets_conventions()
    {
        using var root = new TempKnitRoot();
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");
        Assert.Equal(0, RunIn(root, checkout, "root", target).ExitCode);

        Assert.Equal(0, RunInWith(root, checkout, "Target rule.\n", "conventions", "set", "--scope", "repo").ExitCode);

        Assert.True(File.Exists(Path.Combine(target, ".knit", "conventions.md")));
        Assert.False(File.Exists(Path.Combine(checkout, ".knit", "conventions.md")));
        Assert.Contains("Target rule.", RunIn(root, checkout, "conventions").Stdout);
    }

    [Fact]
    public void Verify_warns_on_long_conventions_without_failing()
    {
        using var root = NewRepo();
        var text = string.Join("\n", Enumerable.Range(1, 201).Select(i => $"rule {i}"));
        Assert.Equal(0, root.RunWithInput(text, "conventions", "set", "--scope", "repo").ExitCode);

        var human = root.Run("verify");
        var json = JsonDocument.Parse(root.Run("verify", "--json").Stdout).RootElement;

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("long conventions (1)", human.Stdout);
        Assert.Contains("201 lines", human.Stdout);
        var row = Assert.Single(json.GetProperty("conventions").EnumerateArray());
        Assert.Equal("repo", row.GetProperty("scope").GetString());
        Assert.Equal(201, row.GetProperty("lines").GetInt32());
        Assert.Empty(json.GetProperty("stray_files").EnumerateArray());
    }

    [Fact]
    public void Init_does_not_ignore_the_repo_file()
    {
        using var root = NewRepo();

        var ignore = File.ReadAllText(Path.Combine(root.Path, ".knit", ".gitignore"));

        Assert.DoesNotContain("conventions", ignore);
    }

    private static string Repo(TempKnitRoot root, string name)
    {
        var dir = Path.Combine(root.Path, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        Assert.Equal(0, RunIn(root, dir, "init", "--handle", "t", "--display", "T", "--non-interactive").ExitCode);
        return dir;
    }

    private static CliResult RunIn(TempKnitRoot root, string dir, params string[] args) => RunInWith(root, dir, null, args);

    private static CliResult RunInWith(TempKnitRoot root, string dir, string? input, params string[] args)
    {
        Directory.SetCurrentDirectory(dir);
        try { return input is null ? root.Run(args) : root.RunWithInput(input, args); }
        finally { Directory.SetCurrentDirectory(root.Path); }
    }
}
