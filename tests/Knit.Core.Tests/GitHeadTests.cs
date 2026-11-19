using Knit.Core.Discovery;

namespace Knit.Core.Tests;

/// <summary>
/// The header's branch label, read from HEAD without shelling out to git —
/// and only ever from the checkout that holds the knit repo.
/// </summary>
public class GitHeadTests : IDisposable
{
    private const string Sha = "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "knit-githead-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private string Dir(params string[] parts)
    {
        var p = Path.Combine([_base, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    private string Checkout(string name, string head)
    {
        var root = Dir(name);
        File.WriteAllText(Path.Combine(Dir(name, ".git"), "HEAD"), head);
        return root;
    }

    private string Worktree(string main, string name, string head)
    {
        var wt = Dir(name);
        var gitDir = Dir(Path.GetFileName(main), ".git", "worktrees", name);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), head);
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {gitDir}\n");
        return wt;
    }

    [Fact]
    public void A_branch_ref_reads_as_the_branch()
    {
        var repo = Checkout("app", "ref: refs/heads/feat/header\n");

        Assert.Equal(new GitHeadState("feat/header", null, false), GitHead.Read(repo));
    }

    [Fact]
    public void A_detached_head_reads_as_a_short_sha()
    {
        var repo = Checkout("app", Sha + "\n");

        Assert.Equal(new GitHeadState(null, "1a2b3c4", false), GitHead.Read(repo));
    }

    [Fact]
    public void A_rebase_in_progress_is_flagged_on_the_detached_head()
    {
        var repo = Checkout("app", Sha);
        Dir("app", ".git", "rebase-merge");

        Assert.Equal(new GitHeadState(null, "1a2b3c4", true), GitHead.Read(repo));
    }

    [Fact]
    public void The_reftable_placeholder_is_not_a_branch()
    {
        var repo = Checkout("app", "ref: refs/heads/.invalid");

        Assert.Null(GitHead.Read(repo));
    }

    [Fact]
    public void A_worktree_reads_its_own_head_through_the_git_file()
    {
        var main = Checkout("main", "ref: refs/heads/main");
        var wt = Worktree(main, "wt", "ref: refs/heads/feat");

        Assert.Equal("feat", GitHead.Read(wt)!.Branch);
    }

    [Fact]
    public void The_checkout_is_discoverys_git_root_when_it_holds_the_knit_root()
    {
        var repo = Checkout("app", "ref: refs/heads/main");

        Assert.Equal(repo, GitHead.CheckoutFor(Dir("app", "tracker"), repo));
    }

    [Fact]
    public void A_worktree_redirected_to_its_main_checkout_reads_the_main_checkouts_head()
    {
        var main = Checkout("main", "ref: refs/heads/main");
        var wt = Worktree(main, "wt", "ref: refs/heads/feat");

        Assert.Equal(main, GitHead.CheckoutFor(main, wt));
    }

    [Fact]
    public void A_knit_root_outside_the_git_root_has_no_checkout()
    {
        var repo = Checkout("app", "ref: refs/heads/main");

        Assert.Null(GitHead.CheckoutFor(Dir("elsewhere"), repo));
        Assert.Null(GitHead.CheckoutFor(Dir("elsewhere"), null));
    }

    [Fact]
    public void The_default_name_is_the_folder_and_a_worktree_takes_its_main_checkouts()
    {
        var main = Checkout("main", "ref: refs/heads/main");
        var wt = Worktree(main, "wt", "ref: refs/heads/feat");

        Assert.Equal("main", GitHead.DefaultRepoName(main, main));
        Assert.Equal("main", GitHead.DefaultRepoName(wt, wt));
        Assert.Equal("tracker", GitHead.DefaultRepoName(Dir("wt", "tracker"), wt));
        Assert.Equal("plain", GitHead.DefaultRepoName(Dir("plain"), null));
    }
}
