using Knit.Core.Config;
using Knit.Core.Discovery;

namespace Knit.Core.Tests;

/// <summary>
/// Discovery used to accept any ancestor holding a directory
/// *named* <c>.knit</c>, which let an unpacked dotfolder (and the home
/// directory) capture every path below it. The root must be a repo, the walk
/// must stop at the git boundary, and a set-but-unresolvable override must be
/// distinguishable from finding nothing.
/// </summary>
public class RootDiscoveryTests : IDisposable
{
    private readonly string _base;
    private readonly string? _previousEnv;

    public RootDiscoveryTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "knit-discovery-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
        // The developer's own KNIT_ROOT must not answer for the walk.
        _previousEnv = Environment.GetEnvironmentVariable(RepoDiscovery.EnvOverride);
        Environment.SetEnvironmentVariable(RepoDiscovery.EnvOverride, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoDiscovery.EnvOverride, _previousEnv);
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private string Dir(params string[] parts)
    {
        var p = Path.Combine([_base, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    private string Repo(params string[] parts)
    {
        var p = Dir(parts);
        Directory.CreateDirectory(RepoDiscovery.KnitDir(p));
        File.WriteAllText(Path.Combine(RepoDiscovery.KnitDir(p), "config.json"), "{}");
        return p;
    }

    [Fact]
    public void A_knit_directory_without_a_config_is_not_a_root()
    {
        var fake = Dir("home");
        Directory.CreateDirectory(RepoDiscovery.KnitDir(fake));
        File.WriteAllText(Path.Combine(RepoDiscovery.KnitDir(fake), "knit.exe"), "binary");

        var r = RepoDiscovery.ResolveDetailed(Dir("home", "work", "project"));

        Assert.Null(r.Root);
        Assert.Contains(RepoDiscovery.KnitDir(fake), r.Skipped);
    }

    [Fact]
    public void A_real_repo_resolves_from_any_subdirectory()
    {
        var repo = Repo("project");
        var deep = Dir("project", "src", "nested");

        Assert.Equal(repo, RepoDiscovery.Resolve(deep));
    }

    [Fact]
    public void The_walk_stops_at_the_git_repo_root()
    {
        Repo("outer");
        var inner = Dir("outer", "inner");
        Directory.CreateDirectory(Path.Combine(inner, ".git"));
        var deep = Dir("outer", "inner", "src");

        var r = RepoDiscovery.ResolveDetailed(deep);

        Assert.Null(r.Root);
        Assert.Equal(inner, r.GitRoot);
    }

    [Fact]
    public void A_worktrees_git_file_bounds_the_walk_too()
    {
        Repo("outer");
        var inner = Dir("outer", "worktree");
        File.WriteAllText(Path.Combine(inner, ".git"), "gitdir: ../.git/worktrees/wt");

        Assert.Null(RepoDiscovery.Resolve(inner));
    }

    [Fact]
    public void A_repo_at_its_own_git_root_still_resolves()
    {
        var repo = Repo("project");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));

        Assert.Equal(repo, RepoDiscovery.Resolve(Dir("project", "src")));
    }

    [Fact]
    public void Outside_a_git_repo_the_walk_is_still_unbounded()
    {
        var repo = Repo("plain");
        var deep = Dir("plain", "a", "b", "c");

        var r = RepoDiscovery.ResolveDetailed(deep);

        Assert.Equal(repo, r.Root);
    }

    [Fact]
    public void An_explicit_root_that_is_not_a_repo_resolves_to_nothing_but_says_what_it_was()
    {
        var target = Dir("elsewhere");

        var r = RepoDiscovery.ResolveDetailed(Repo("project"), target);

        Assert.Null(r.Root);
        Assert.Equal(RepoDiscovery.RootOrigin.Explicit, r.Origin);
        Assert.Equal(target, r.OverridePath);
    }

    [Fact]
    public void A_stale_env_override_is_reported_as_the_override_not_as_no_repo()
    {
        var target = Dir("gone");
        Environment.SetEnvironmentVariable(RepoDiscovery.EnvOverride, target);

        var r = RepoDiscovery.ResolveDetailed(Repo("project"));

        Assert.Null(r.Root);
        Assert.Equal(RepoDiscovery.RootOrigin.Environment, r.Origin);
        Assert.Equal(target, r.OverridePath);
    }

    // The checkout's knit_root redirects, one hop, from config.local.json only.

    private static void SetKnitRoot(string repo, string value) =>
        File.WriteAllText(Path.Combine(RepoDiscovery.KnitDir(repo), "config.local.json"),
            $$"""{ "knit_root": {{System.Text.Json.JsonSerializer.Serialize(value)}} }""");

    [Fact]
    public void The_walk_follows_the_checkouts_knit_root_and_keeps_the_checkout()
    {
        var checkout = Repo("wt");
        var target = Repo("main");
        SetKnitRoot(checkout, target);

        var r = RepoDiscovery.ResolveDetailed(Dir("wt", "src"));

        Assert.Equal(target, r.Root);
        Assert.Equal(checkout, r.Checkout);
        Assert.Equal(RepoDiscovery.RootOrigin.Redirect, r.Origin);
    }

    [Fact]
    public void A_committed_knit_root_is_not_a_redirect()
    {
        var checkout = Repo("wt");
        var target = Repo("main");
        File.WriteAllText(Path.Combine(RepoDiscovery.KnitDir(checkout), "config.json"),
            $$"""{ "knit_root": {{System.Text.Json.JsonSerializer.Serialize(target)}} }""");

        Assert.Equal(checkout, RepoDiscovery.Resolve(checkout));
    }

    [Fact]
    public void Dot_means_this_checkout_on_purpose()
    {
        var checkout = Repo("wt");
        SetKnitRoot(checkout, ".");

        var r = RepoDiscovery.ResolveDetailed(checkout);

        Assert.Equal(checkout, r.Root);
        Assert.Equal(RepoDiscovery.RootOrigin.Walk, r.Origin);
        Assert.Equal(".", r.KnitRoot);
    }

    [Fact]
    public void A_missing_target_is_told_apart_from_one_without_a_repo()
    {
        var gone = Repo("a");
        SetKnitRoot(gone, Path.Combine(_base, "unplugged"));
        var empty = Repo("b");
        SetKnitRoot(empty, Dir("plain"));

        var missing = RepoDiscovery.ResolveDetailed(gone);
        var notARepo = RepoDiscovery.ResolveDetailed(empty);

        Assert.Null(missing.Root);
        Assert.Equal(RepoDiscovery.RedirectFailure.TargetMissing, missing.Failure);
        Assert.Null(notARepo.Root);
        Assert.Equal(RepoDiscovery.RedirectFailure.TargetNotARepo, notARepo.Failure);
        Assert.Equal(gone, missing.Checkout);
    }

    [Fact]
    public void A_target_that_redirects_again_is_refused_not_followed()
    {
        var a = Repo("a");
        var b = Repo("b");
        var c = Repo("c");
        SetKnitRoot(a, b);
        SetKnitRoot(b, c);

        var r = RepoDiscovery.ResolveDetailed(a);

        Assert.Null(r.Root);
        Assert.Equal(RepoDiscovery.RedirectFailure.Chained, r.Failure);
        Assert.Equal(b, r.OverridePath);
        Assert.Equal(c, r.ChainTarget);
    }

    [Fact]
    public void An_override_names_a_root_directly_and_ignores_its_knit_root()
    {
        var checkout = Repo("wt");
        SetKnitRoot(checkout, Path.Combine(_base, "unplugged"));

        var r = RepoDiscovery.ResolveDetailed(Dir("elsewhere"), checkout);

        Assert.Equal(checkout, r.Root);
        Assert.Equal(RepoDiscovery.RootOrigin.Explicit, r.Origin);
    }

    [Fact]
    public void A_worktrees_git_file_names_the_main_checkout()
    {
        var main = Dir("main");
        var wt = Dir("wt");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", "wt")}\n");

        Assert.Equal(main, GitWorktree.MainCheckout(wt));
    }

    [Fact]
    public void A_relative_gitdir_resolves_against_the_worktree()
    {
        var main = Dir("main");
        var wt = Dir("main", "trees", "wt");
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: ../../.git/worktrees/wt");

        Assert.Equal(main, GitWorktree.MainCheckout(wt));
    }

    [Fact]
    public void A_separate_git_dir_is_not_a_worktree()
    {
        var checkout = Dir("plain");
        File.WriteAllText(Path.Combine(checkout, ".git"), $"gitdir: {Path.Combine(_base, "git-store", "plain.git")}");

        Assert.Null(GitWorktree.MainCheckout(checkout));
    }

    [Fact]
    public void A_worktree_subdirectory_maps_to_the_same_one_in_the_main_checkout()
    {
        var main = Dir("main");
        var wt = Dir("wt");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", "wt")}\n");

        Assert.Equal(Path.Combine(main, "app"), GitWorktree.Counterpart(Dir("wt", "app")));
        Assert.Null(GitWorktree.Counterpart(Dir("main", "app")));
    }

    [Fact]
    public void The_main_checkouts_knit_repo_is_found_by_walking_up_from_the_counterpart()
    {
        var main = Dir("main");
        Directory.CreateDirectory(Path.Combine(main, ".git"));
        var tracker = Repo("main", "app");
        var wt = Dir("wt");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {Path.Combine(main, ".git", "worktrees", "wt")}\n");

        // A branch-only directory has no counterpart on disk; the walk still climbs.
        Assert.Equal(tracker, GitWorktree.MainKnitRepo(Dir("wt", "app", "branch-only")));
        Assert.Null(GitWorktree.MainKnitRepo(wt));
    }

    // Bare repo, every branch a worktree, the shapes git writes.

    private string BareRepo(string container, string repoDir = ".git", string head = "ref: refs/heads/main")
    {
        var common = Dir(container, repoDir);
        File.WriteAllText(Path.Combine(common, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = true\n");
        File.WriteAllText(Path.Combine(common, "HEAD"), head + "\n");
        return common;
    }

    private static string BareWorktree(string common, string root, string head, bool relative = false)
    {
        Directory.CreateDirectory(root);
        var admin = Path.Combine(common, "worktrees", Path.GetFileName(root));
        Directory.CreateDirectory(admin);
        File.WriteAllText(Path.Combine(admin, "commondir"), "../..\n");
        File.WriteAllText(Path.Combine(admin, "HEAD"), head + "\n");
        var dotGit = Path.Combine(root, ".git");
        File.WriteAllText(Path.Combine(admin, "gitdir"), (relative ? Path.GetRelativePath(admin, dotGit) : dotGit) + "\n");
        File.WriteAllText(dotGit, $"gitdir: {(relative ? Path.GetRelativePath(root, admin) : admin)}\n");
        return root;
    }

    [Theory]
    [InlineData(".git", false)]
    [InlineData(".bare", false)]
    [InlineData(".git", true)]
    public void A_bare_repos_worktrees_map_to_the_default_branchs_worktree_not_the_container(string repoDir, bool relative)
    {
        var common = BareRepo("proj", repoDir);
        var main = BareWorktree(common, Path.Combine(_base, "proj", "main"), "ref: refs/heads/main", relative);
        var feat = BareWorktree(common, Path.Combine(_base, "proj", "feat"), "ref: refs/heads/feat", relative);

        var bare = GitWorktree.Bare(Path.Combine(feat, "sub"));

        Assert.NotNull(bare);
        Assert.Equal("main", bare.DefaultBranch);
        Assert.Equal(main, bare.DefaultWorktree);
        Assert.Null(GitWorktree.MainCheckout(feat));
        Assert.Equal(Path.Combine(main, "app"), GitWorktree.Counterpart(Path.Combine(feat, "app")));
        Assert.Null(GitWorktree.Counterpart(main));
    }

    [Theory]
    [InlineData("ref: refs/heads/.invalid", "ref: refs/heads/.invalid")]
    [InlineData("0123456789abcdef0123456789abcdef01234567", "ref: refs/heads/main")]
    [InlineData("ref: refs/heads/trunk", "ref: refs/heads/main")]
    public void Without_a_nameable_default_worktree_only_siblings_with_a_tracker_are_listed(string head, string worktreeHead)
    {
        var common = BareRepo("proj", head: head);
        var a = BareWorktree(common, Path.Combine(_base, "proj", "a"), worktreeHead);
        var b = BareWorktree(common, Path.Combine(_base, "proj", "b"), worktreeHead);
        BareWorktree(common, Path.Combine(_base, "proj", "c"), worktreeHead);
        Repo("proj", "a");

        Assert.Null(GitWorktree.Bare(b)!.DefaultWorktree);
        Assert.Null(GitWorktree.Counterpart(b));
        Assert.Equal([a], GitWorktree.SiblingKnitRepos(b));
        Assert.Empty(GitWorktree.SiblingKnitRepos(a));
    }

    [Fact]
    public void A_worktree_of_a_non_bare_repo_is_not_bare()
    {
        var main = Dir("main");
        var git = Dir("main", ".git");
        File.WriteAllText(Path.Combine(git, "config"), "[core]\n\tbare = false\n");
        var admin = Dir("main", ".git", "worktrees", "wt");
        File.WriteAllText(Path.Combine(admin, "commondir"), "../..\n");
        var wt = Dir("wt");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {admin}\n");

        Assert.Null(GitWorktree.Bare(wt));
        Assert.Equal(main, GitWorktree.MainCheckout(wt));
    }

    [Theory]
    [InlineData(".git", "box")]
    [InlineData("proj.git", "proj")]
    public void A_bare_repos_worktree_is_named_after_the_repo(string repoDir, string expected)
    {
        var common = BareRepo("box", repoDir);
        var wt = BareWorktree(common, Path.Combine(_base, "box", "main"), "ref: refs/heads/main");

        Assert.Equal(expected, GitHead.DefaultRepoName(wt, wt));
    }

    // config.json declares what a checkout must fill in locally.

    [Fact]
    public void A_declared_knit_root_is_met_by_a_path_or_by_dot_and_an_unknown_setting_is_left_out()
    {
        var checkout = Dir("clone");
        Directory.CreateDirectory(RepoDiscovery.KnitDir(checkout));
        File.WriteAllText(Path.Combine(RepoDiscovery.KnitDir(checkout), "config.json"),
            """{ "requires": { "knit_root": "on the share", "colour": "blue" } }""");

        var unset = Requirements.Check(RepoDiscovery.ResolveDetailed(checkout));
        SetKnitRoot(checkout, ".");
        var local = RepoDiscovery.ResolveDetailed(checkout);

        Assert.Equal([new Requirement("knit_root", "on the share", false)], unset);
        Assert.True(Requirements.Check(local).Single().Met);
        Assert.True(Requirements.KnitRootOverriddenLocally(local));
    }
}
