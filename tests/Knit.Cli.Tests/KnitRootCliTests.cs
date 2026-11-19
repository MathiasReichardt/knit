using System.Text.Json;
using System.Text.RegularExpressions;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>knit root</c> and the redirect it writes: validated before
/// writing, flattened when written, refused (never quietly bypassed) when it
/// stops resolving, and always visible. <c>KNIT_ROOT</c> is cleared so the
/// walk — and with it the redirect — is what answers; each repo carries a
/// <c>.git</c> so the walk cannot escape the fixture.
/// </summary>
[Collection(nameof(CliCollection))]
public class KnitRootCliTests
{
    private static string Repo(TempKnitRoot root, string name)
    {
        var dir = Path.Combine(root.Path, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        var r = RunIn(root, dir, "init", "--handle", "t", "--display", "T", "--non-interactive");
        Assert.Equal(0, r.ExitCode);
        return dir;
    }

    private static TempKnitRoot Fixture()
    {
        var root = new TempKnitRoot();
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);
        return root;
    }

    private static string LocalConfig(string repo) => Path.Combine(repo, ".knit", "config.local.json");

    private static string? KnitRootIn(string repo) =>
        File.Exists(LocalConfig(repo))
        && JsonDocument.Parse(File.ReadAllText(LocalConfig(repo))).RootElement
            .TryGetProperty("knit_root", out var v)
            ? v.GetString()
            : null;

    [Fact]
    public void A_redirect_sends_reads_and_writes_to_the_target()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");

        var set = RunIn(root, checkout, "root", target);
        var add = RunIn(root, checkout, "add", "task", "Lands in the target");

        Assert.Equal(0, set.ExitCode);
        Assert.Equal(target, KnitRootIn(checkout));
        Assert.Equal(0, add.ExitCode);
        // Set on purpose, so not announced on every call — only when asked.
        Assert.Equal("", add.Stderr);
        Assert.Contains("redirected", RunIn(root, checkout, "list", "--verbose").Stderr);
        Assert.Contains("Lands in the target", RunIn(root, target, "list").Stdout);
        Assert.False(Directory.Exists(Path.Combine(checkout, ".knit", "items")));
    }

    [Fact]
    public void A_target_that_is_not_a_repo_is_refused_and_nothing_is_written()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var plain = Path.Combine(root.Path, "plain");
        Directory.CreateDirectory(plain);

        var missing = RunIn(root, checkout, "root", Path.Combine(root.Path, "nowhere"));
        var notARepo = RunIn(root, checkout, "root", plain);

        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("does not exist", missing.Stderr);
        Assert.NotEqual(0, notARepo.ExitCode);
        Assert.Contains("knit init' there first", notARepo.Stderr);
        Assert.Null(KnitRootIn(checkout));
    }

    [Fact]
    public void Pointing_at_a_repo_that_redirects_writes_the_end_of_the_chain()
    {
        using var root = Fixture();
        var a = Repo(root, "a");
        var b = Repo(root, "b");
        var c = Repo(root, "c");
        Assert.Equal(0, RunIn(root, b, "root", c).ExitCode);

        var r = RunIn(root, a, "root", b);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(c, KnitRootIn(a));
        Assert.Contains("redirects to", r.Stdout);
    }

    [Fact]
    public void A_chain_met_at_runtime_refuses_and_names_both_paths()
    {
        using var root = Fixture();
        var a = Repo(root, "a");
        var b = Repo(root, "b");
        var c = Repo(root, "c");
        Assert.Equal(0, RunIn(root, a, "root", b).ExitCode);
        Assert.Equal(0, RunIn(root, b, "root", c).ExitCode);

        // --json: the error is one unwrapped line, so the paths survive intact.
        var r = RunIn(root, a, "list", "--json");

        Assert.NotEqual(0, r.ExitCode);
        var message = JsonDocument.Parse(r.Stderr).RootElement.GetProperty("message").GetString()!;
        Assert.Contains(b, message);
        Assert.Contains(c, message);
        Assert.Contains("one hop", message);
    }

    [Fact]
    public void A_broken_redirect_refuses_but_the_diagnostic_and_the_escape_still_run()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");
        Assert.Equal(0, RunIn(root, checkout, "root", target).ExitCode);
        Directory.Delete(target, recursive: true);

        var list = RunIn(root, checkout, "list");
        var show = RunIn(root, checkout, "root");
        var off = RunIn(root, checkout, "root", "--off");
        var after = RunIn(root, checkout, "list");

        Assert.NotEqual(0, list.ExitCode);
        Assert.Contains("does not exist", list.Stderr);
        // Ranked: restore before repoint before --off.
        Assert.True(list.Stderr.IndexOf("reconnect", StringComparison.Ordinal)
                    < list.Stderr.IndexOf("--off", StringComparison.Ordinal));
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("(unresolved)", show.Stdout);
        Assert.Equal(0, off.ExitCode);
        Assert.Equal(0, after.ExitCode);
        Assert.Null(KnitRootIn(checkout));
    }

    [Fact]
    public void Local_is_a_third_state_and_off_returns_to_undecided()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");

        Assert.Equal(0, RunIn(root, checkout, "root", "--local").ExitCode);
        Assert.Equal(".", KnitRootIn(checkout));
        var local = JsonDocument.Parse(RunIn(root, checkout, "root", "--json").Stdout).RootElement;
        Assert.Equal("local", local.GetProperty("origin").GetString());

        Assert.Equal(0, RunIn(root, checkout, "root", "--off").ExitCode);
        var walk = JsonDocument.Parse(RunIn(root, checkout, "root", "--json").Stdout).RootElement;
        Assert.Equal("walk", walk.GetProperty("origin").GetString());
        Assert.Equal(JsonValueKind.Null, walk.GetProperty("knit_root").ValueKind);
    }

    [Fact]
    public void Redirecting_a_checkout_with_items_says_they_are_hidden_not_moved()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");
        Assert.Equal(0, RunIn(root, checkout, "add", "task", "Stays behind").ExitCode);

        var r = RunIn(root, checkout, "root", target);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("1 item in this checkout's .knit/items/ is now hidden — not moved", r.Stdout);
        Assert.Contains("knit config set requires.knit_root", r.Stdout);
    }

    [Fact]
    public void Worktree_mode_points_at_the_main_checkout()
    {
        using var root = Fixture();
        var main = Repo(root, "main");
        var wt = WorktreeOf(root, main, "wt");

        var r = RunIn(root, wt, "root", "--worktree");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(main, KnitRootIn(wt));
    }

    [Fact]
    public void Worktree_mode_states_the_case_when_the_main_checkout_has_no_knit()
    {
        using var root = Fixture();
        var main = Path.Combine(root.Path, "main");
        Directory.CreateDirectory(Path.Combine(main, ".git"));
        var wt = WorktreeOf(root, main, "wt");

        var r = RunIn(root, wt, "root", "--worktree");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("this worktree's own is in use", r.Stderr);
        Assert.Contains("knit root --local", r.Stderr);
    }

    [Fact]
    public void Knit_root_without_a_knit_dir_names_init_and_scaffolds_nothing()
    {
        using var root = Fixture();
        var bare = Path.Combine(root.Path, "bare");
        Directory.CreateDirectory(Path.Combine(bare, ".git"));

        var r = RunIn(root, bare, "root", "--local");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit init", r.Stderr);
        Assert.False(Directory.Exists(Path.Combine(bare, ".knit")));
    }

    [Fact]
    public void Init_refuses_below_a_redirecting_checkout()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");
        Assert.Equal(0, RunIn(root, checkout, "root", target).ExitCode);
        var sub = Path.Combine(checkout, "sub");
        Directory.CreateDirectory(sub);

        var r = RunIn(root, sub, "init", "--handle", "t", "--display", "T", "--non-interactive");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit root --off", r.Stderr);
        Assert.False(Directory.Exists(Path.Combine(sub, ".knit")));
    }

    [Fact]
    public void Status_states_the_redirect()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");
        var target = Repo(root, "target");
        Assert.Equal(0, RunIn(root, checkout, "root", target).ExitCode);

        var human = RunIn(root, checkout, "status");
        var json = JsonDocument.Parse(RunIn(root, checkout, "status", "--json").Stdout).RootElement;

        Assert.Contains("redirected from", human.Stdout);
        Assert.Equal(checkout, json.GetProperty("redirected_from").GetString());
    }

    [Fact]
    public void Config_set_refuses_knit_root_and_names_the_verb()
    {
        using var root = Fixture();
        var checkout = Repo(root, "checkout");

        var r = RunIn(root, checkout, "config", "set", "--local", "knit_root", root.Path);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("knit root", r.Stderr);
    }

    [Fact]
    public void The_not_found_message_names_init_then_init_external_then_the_overrides()
    {
        using var root = Fixture();
        var bare = Path.Combine(root.Path, "bare");
        Directory.CreateDirectory(Path.Combine(bare, ".git"));

        var err = RunIn(root, bare, "list").Stderr;

        var init = err.IndexOf("'knit init'", StringComparison.Ordinal);
        var external = err.IndexOf("knit init --external", StringComparison.Ordinal);
        var overrides = err.IndexOf("KNIT_ROOT", StringComparison.Ordinal);
        Assert.True(init >= 0 && init < external && external < overrides, err);
    }

    [Fact]
    public void An_undecided_worktree_is_nudged_until_it_decides()
    {
        using var root = Fixture();
        var main = Repo(root, "main");
        var wt = WorktreeOf(root, main, "wt");

        var nudged = RunIn(root, wt, "list");
        Assert.Equal(0, RunIn(root, wt, "root", "--local").ExitCode);
        var local = RunIn(root, wt, "list");
        Assert.Equal(0, RunIn(root, wt, "root", "--worktree").ExitCode);
        var shared = RunIn(root, wt, "list");

        Assert.Equal(0, nudged.ExitCode);
        Assert.Contains("this git worktree uses its own .knit/", nudged.Stderr);
        Assert.Contains("knit root --worktree", nudged.Stderr);
        Assert.Equal("", local.Stderr);
        Assert.Equal("", shared.Stderr);
    }

    [Fact]
    public void No_nudge_when_the_main_checkout_has_no_knit_or_outside_a_worktree()
    {
        using var root = Fixture();
        var main = Path.Combine(root.Path, "main");
        Directory.CreateDirectory(Path.Combine(main, ".git"));
        var wt = WorktreeOf(root, main, "wt");
        var plain = Repo(root, "plain");

        Assert.Equal("", RunIn(root, wt, "list").Stderr);
        Assert.Equal("", RunIn(root, plain, "list").Stderr);
    }

    [Fact]
    public void A_worktree_branch_without_knit_is_told_to_merge_main_not_to_init()
    {
        using var root = Fixture();
        var main = Repo(root, "main");
        var wt = WorktreeOf(root, main, "wt", init: false);

        var list = RunIn(root, wt, "list");
        var redirect = RunIn(root, wt, "root", "--worktree");

        Assert.NotEqual(0, list.ExitCode);
        Assert.Contains("this branch has no .knit/", list.Stderr);
        Assert.Contains("merge main into this branch, then 'knit root --worktree'", list.Stderr);
        Assert.Contains("not 'knit init' here", list.Stderr);
        Assert.NotEqual(0, redirect.ExitCode);
        Assert.Contains("merge main into this branch", redirect.Stderr);
    }

    // Bare repo at proj/.git, every branch a sibling worktree.

    private static string BareRepo(TempKnitRoot root, string head = "ref: refs/heads/main")
    {
        var common = Path.Combine(root.Path, "proj", ".git");
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(common, "config"), "[core]\n\tbare = true\n");
        File.WriteAllText(Path.Combine(common, "HEAD"), head + "\n");
        return common;
    }

    private static string BareWorktree(TempKnitRoot root, string common, string name, string branch, bool init = true)
    {
        var wt = Path.Combine(root.Path, "proj", name);
        var admin = Path.Combine(common, "worktrees", name);
        Directory.CreateDirectory(wt);
        Directory.CreateDirectory(admin);
        File.WriteAllText(Path.Combine(admin, "commondir"), "../..\n");
        File.WriteAllText(Path.Combine(admin, "HEAD"), $"ref: refs/heads/{branch}\n");
        File.WriteAllText(Path.Combine(admin, "gitdir"), Path.Combine(wt, ".git") + "\n");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {admin}\n");
        if (init) Assert.Equal(0, RunIn(root, wt, "init", "--handle", "t", "--display", "T", "--non-interactive").ExitCode);
        return wt;
    }

    [Fact]
    public void A_bare_repos_worktree_is_nudged_toward_the_default_branchs_worktree_by_path()
    {
        using var root = Fixture();
        var common = BareRepo(root);
        var main = BareWorktree(root, common, "main", "main");
        var feat = BareWorktree(root, common, "feat", "feat");

        var nudged = RunIn(root, feat, "list");
        var atDefault = RunIn(root, main, "list");
        var set = RunIn(root, feat, "root", main);
        var after = RunIn(root, feat, "list");

        Assert.Contains("the default branch's worktree", nudged.Stderr);
        Assert.Contains(Tight($"'knit root {main}'"), Tight(nudged.Stderr));
        Assert.Contains("'knit root --local'", nudged.Stderr);
        Assert.Equal("", atDefault.Stderr);
        Assert.Equal(0, set.ExitCode);
        Assert.Contains(Tight("keep 'main' checked out there"), Tight(set.Stdout));
        Assert.Equal("", after.Stderr);
    }

    [Fact]
    public void Worktree_mode_in_a_bare_repo_is_refused_and_names_the_path()
    {
        using var root = Fixture();
        var common = BareRepo(root);
        var main = BareWorktree(root, common, "main", "main");
        var feat = BareWorktree(root, common, "feat", "feat");

        var r = RunIn(root, feat, "root", "--worktree");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("bare repo", r.Stderr);
        Assert.Contains(Tight($"'knit root {main}'"), Tight(r.Stderr));
        Assert.Null(KnitRootIn(feat));
    }

    [Fact]
    public void Under_reftable_the_bare_nudge_names_no_path()
    {
        using var root = Fixture();
        var common = BareRepo(root, head: "ref: refs/heads/.invalid");
        BareWorktree(root, common, "main", ".invalid");
        var feat = BareWorktree(root, common, "feat", ".invalid");

        var r = RunIn(root, feat, "list");

        Assert.Contains("other worktrees of this bare repo have one too", r.Stderr);
        Assert.Contains("'knit root <path>'", r.Stderr);
    }

    [Fact]
    public void A_bare_repos_branch_without_knit_is_told_the_default_worktrees_path()
    {
        using var root = Fixture();
        var common = BareRepo(root);
        var main = BareWorktree(root, common, "main", "main");
        var feat = BareWorktree(root, common, "feat", "feat", init: false);

        var r = RunIn(root, feat, "list");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains(Tight($"merge main into this branch, then 'knit root {main}'"), Tight(r.Stderr));
        Assert.Contains("not 'knit init' here", r.Stderr);
    }

    private static string WorktreeOf(TempKnitRoot root, string main, string name, bool init = true)
    {
        var wt = Path.Combine(root.Path, name);
        Directory.CreateDirectory(wt);
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", name)}\n");
        if (!init) return wt;
        var r = RunIn(root, wt, "init", "--handle", "t", "--display", "T", "--non-interactive");
        Assert.Equal(0, r.ExitCode);
        return wt;
    }

    /// <summary>
    /// Runs from <paramref name="dir"/>. Output is whitespace-collapsed: the
    /// fixture's temp paths push messages past the 80-column wrap, and the
    /// assertions are about words, not line breaks. Still valid JSON.
    /// </summary>
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

    // A long path wraps mid-token, so path assertions compare without whitespace.
    private static string Tight(string s) => Regex.Replace(s, @"\s+", "");
}
