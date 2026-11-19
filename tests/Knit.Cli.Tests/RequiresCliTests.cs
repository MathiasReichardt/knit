using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

/// <summary>
/// A project declares the local-only settings it needs
/// (<c>requires</c>), a checkout that has not filled them in refuses until it
/// does, <c>knit config setup</c> fills them in, <c>knit init --external</c>
/// starts a repo whose tracker lives elsewhere, and an unset handle is a state
/// that refuses writes rather than authoring them by nobody.
/// </summary>
[Collection(nameof(CliCollection))]
public class RequiresCliTests
{
    [Fact]
    public void A_declared_knit_root_refuses_until_filled_in_but_the_repair_verbs_run()
    {
        using var root = Fixture();
        var target = Repo(root, "tracker");
        var clone = Repo(root, "clone");
        Declare(clone, "knit_root", "clone the team tracker anywhere");

        var refused = RunIn(root, clone, "list");
        var diagnostic = RunIn(root, clone, "root");
        var configList = RunIn(root, clone, "config", "list");
        var set = RunIn(root, clone, "config", "setup", "--knit-root", target, "--non-interactive");
        var after = RunIn(root, clone, "list");

        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("knit_root — \"clone the team tracker anywhere\"", refused.Stderr);
        Assert.Contains("knit config setup", refused.Stderr);
        Assert.Equal(0, diagnostic.ExitCode);
        Assert.Contains("not set here", configList.Stdout);
        Assert.Equal(0, set.ExitCode);
        Assert.Equal(0, after.ExitCode);
    }

    [Fact]
    public void Local_on_purpose_satisfies_it_and_status_keeps_the_choice_visible()
    {
        using var root = Fixture();
        var clone = Repo(root, "clone");
        Declare(clone, "knit_root", "on the share");

        Assert.Equal(0, RunIn(root, clone, "root", "--local").ExitCode);
        var status = RunIn(root, clone, "status");
        var json = JsonDocument.Parse(RunIn(root, clone, "status", "--json").Stdout).RootElement;

        Assert.Equal(0, status.ExitCode);
        Assert.Contains("local (declared external, overridden here)", status.Stdout);
        Assert.True(json.GetProperty("local_override").GetBoolean());
        Assert.Contains("declared external, overridden here", RunIn(root, clone, "root").Stdout);
    }

    [Fact]
    public void Setup_without_answers_names_what_is_missing_and_the_flags()
    {
        using var root = Fixture();
        var clone = Repo(root, "clone");
        Declare(clone, "knit_root", "on the share");
        Declare(clone, "mounts.docs", "the docs checkout");

        var r = RunIn(root, clone, "config", "setup", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("still not set: knit_root, mounts.docs", r.Stderr);
        Assert.Contains("--knit-root <path>", r.Stderr);
        Assert.Contains("--mount docs=<path>", r.Stderr);
    }

    [Fact]
    public void A_required_mount_is_filled_in_locally()
    {
        using var root = Fixture();
        var clone = Repo(root, "clone");
        Declare(clone, "mounts.docs", "the docs checkout");
        Assert.NotEqual(0, RunIn(root, clone, "list").ExitCode);

        var r = RunIn(root, clone, "config", "setup", "--mount", "docs=../docs", "--json");

        Assert.Equal(0, r.ExitCode);
        var local = JsonNode.Parse(File.ReadAllText(Path.Combine(clone, ".knit", "config.local.json")))!;
        Assert.Equal("../docs", local["mounts"]!["docs"]!.GetValue<string>());
        Assert.True(JsonDocument.Parse(r.Stdout).RootElement.GetProperty("requires")[0].GetProperty("met").GetBoolean());
        Assert.Equal(0, RunIn(root, clone, "list").ExitCode);
    }

    [Fact]
    public void A_requirement_is_declared_in_the_checkouts_committed_config_only()
    {
        using var root = Fixture();
        var target = Repo(root, "tracker");
        var checkout = Repo(root, "checkout");
        Assert.Equal(0, RunIn(root, checkout, "root", target).ExitCode);

        var local = RunIn(root, checkout, "config", "set", "requires.knit_root", "x", "--local");
        var unknown = RunIn(root, checkout, "config", "set", "requires.due_soon_days", "x");
        var set = RunIn(root, checkout, "config", "set", "requires.knit_root", "clone the tracker");

        Assert.NotEqual(0, local.ExitCode);
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Equal(0, set.ExitCode);
        // Under the redirect, the checkout — not the target — carries it.
        Assert.Equal("clone the tracker", Requires(checkout)?["knit_root"]?.GetValue<string>());
        Assert.Null(Requires(target));

        Assert.Equal(0, RunIn(root, checkout, "config", "unset", "requires.knit_root").ExitCode);
        Assert.Null(Requires(checkout));
    }

    [Fact]
    public void Init_external_writes_only_the_declaration_and_points_at_the_root()
    {
        using var root = Fixture();
        var tracker = Repo(root, "tracker");
        var fresh = Path.Combine(root.Path, "fresh");
        Directory.CreateDirectory(Path.Combine(fresh, ".git"));

        var r = RunIn(root, fresh, "init", "--external", tracker, "--hint", "clone the tracker anywhere");
        var add = RunIn(root, fresh, "add", "task", "Lands in the tracker");

        Assert.Equal(0, r.ExitCode);
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(fresh, ".knit", "config.json")))!.AsObject();
        Assert.Equal(["format_version", "requires"], config.Select(kv => kv.Key).Order().ToArray());
        Assert.False(Directory.Exists(Path.Combine(fresh, ".knit", "templates")));
        Assert.Equal(0, add.ExitCode);
        Assert.Contains("Lands in the tracker", RunIn(root, tracker, "list").Stdout);
    }

    [Fact]
    public void Init_external_at_a_bad_root_or_without_a_hint_writes_nothing()
    {
        using var root = Fixture();
        var tracker = Repo(root, "tracker");
        var fresh = Path.Combine(root.Path, "fresh");
        Directory.CreateDirectory(Path.Combine(fresh, ".git"));

        var bad = RunIn(root, fresh, "init", "--external", Path.Combine(root.Path, "nowhere"), "--hint", "h");
        var noHint = RunIn(root, fresh, "init", "--external", tracker, "--non-interactive");

        Assert.NotEqual(0, bad.ExitCode);
        Assert.NotEqual(0, noHint.ExitCode);
        Assert.Contains("--hint required", noHint.Stderr);
        Assert.False(Directory.Exists(Path.Combine(fresh, ".knit")));
    }

    [Fact]
    public void An_unset_handle_refuses_writes_and_mine_but_not_reads()
    {
        using var root = Fixture();
        var repo = Repo(root, "repo");
        var add = RunIn(root, repo, "add", "task", "Seed", "--json");
        var id = JsonDocument.Parse(add.Stdout).RootElement.GetProperty("id").GetString()!;
        // A hand-edited "" means unset.
        WriteLocalConfig(repo, """{ "handle": "" }""");

        var anonymousAdd = RunIn(root, repo, "add", "task", "By nobody");
        var comment = RunIn(root, repo, "comment", id, "hi");
        var start = RunIn(root, repo, "start", id);
        var mine = RunIn(root, repo, "list", "--mine");
        var list = RunIn(root, repo, "list");

        Assert.Equal(0, add.ExitCode);
        foreach (var r in new[] { anonymousAdd, comment, start, mine })
        {
            Assert.True(r.ExitCode != 0, r.Stdout + r.Stderr);
            Assert.Contains("knit config set --global handle <you>", r.Stderr);
        }
        Assert.Equal(0, list.ExitCode);
        Assert.Equal(0, RunIn(root, repo, "start", id, "--keep-assignee").ExitCode);
    }

    [Fact]
    public void Setup_saves_the_handle_for_the_machine_and_an_empty_repo_one_does_not_shadow_it()
    {
        using var root = Fixture();
        var repo = Repo(root, "repo");
        WriteLocalConfig(repo, """{ "handle": "" }""");

        var setup = RunIn(root, repo, "config", "setup", "--handle", "ada", "--non-interactive");
        var add = RunIn(root, repo, "add", "task", "By ada", "--json");

        Assert.Equal(0, setup.ExitCode);
        Assert.True(File.Exists(Path.Combine(root.ConfigHome, "config.json")));
        Assert.Equal(0, add.ExitCode);
        Assert.Equal("ada", RunIn(root, repo, "config", "get", "handle").Stdout.Trim());
    }

    private static TempKnitRoot Fixture()
    {
        var root = new TempKnitRoot();
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);
        return root;
    }

    private static string Repo(TempKnitRoot root, string name)
    {
        var dir = Path.Combine(root.Path, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        Assert.Equal(0, RunIn(root, dir, "init", "--handle", "t", "--display", "T", "--non-interactive").ExitCode);
        return dir;
    }

    private static void Declare(string repo, string setting, string hint)
    {
        var path = Path.Combine(repo, ".knit", "config.json");
        var config = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (config["requires"] is not JsonObject requires) config["requires"] = requires = new JsonObject();
        requires[setting] = hint;
        File.WriteAllText(path, config.ToJsonString());
    }

    private static JsonObject? Requires(string repo) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(repo, ".knit", "config.json")))!["requires"] as JsonObject;

    private static void WriteConfig(string repo, string json) =>
        File.WriteAllText(Path.Combine(repo, ".knit", "config.json"), json);

    private static void WriteLocalConfig(string repo, string json) =>
        File.WriteAllText(Path.Combine(repo, ".knit", "config.local.json"), json);

    /// <summary>Runs from <paramref name="dir"/>, whitespace-collapsed like <c>KnitRootCliTests</c>.</summary>
    private static CliResult RunIn(TempKnitRoot root, string dir, params string[] args)
    {
        Directory.SetCurrentDirectory(dir);
        try
        {
            var r = root.Run(args);
            return r with { Stdout = Flat(r.Stdout), Stderr = Flat(r.Stderr) };
        }
        finally { Directory.SetCurrentDirectory(root.Path); }
    }

    private static string Flat(string s) => Regex.Replace(s, @"\s+", " ");
}
