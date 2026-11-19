using Knit.Core.Discovery;
using Knit.WebApi;

namespace Knit.Cli.Tests;

/// <summary>
/// <see cref="WebApiOptions.ForRoot"/> — the root half of the options every
/// host builds from discovery. Built on a bare repo at <c>proj/.git</c> with
/// worktrees <c>main</c> (default branch, holds the tracker) and <c>feat</c>.
/// In the CLI collection: the walk reads <c>KNIT_ROOT</c>, which is process-global.
/// </summary>
[Collection(nameof(CliCollection))]
public class WebApiOptionsTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"knit-options-{Guid.NewGuid():N}");
    private readonly string? _previousEnv = Environment.GetEnvironmentVariable(RepoDiscovery.EnvOverride);
    private readonly string _main;
    private readonly string _feat;

    public WebApiOptionsTests()
    {
        Environment.SetEnvironmentVariable(RepoDiscovery.EnvOverride, null);
        var common = Path.Combine(_base, "proj", ".git");
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(common, "config"), "[core]\n\tbare = true\n");
        File.WriteAllText(Path.Combine(common, "HEAD"), "ref: refs/heads/main\n");
        _main = Worktree(common, "main");
        _feat = Worktree(common, "feat");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoDiscovery.EnvOverride, _previousEnv);
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    private string Worktree(string common, string branch)
    {
        var wt = Path.Combine(_base, "proj", branch);
        var admin = Path.Combine(common, "worktrees", branch);
        Directory.CreateDirectory(admin);
        Directory.CreateDirectory(Path.Combine(wt, ".knit"));
        File.WriteAllText(Path.Combine(wt, ".knit", "config.json"), "{}");
        File.WriteAllText(Path.Combine(admin, "commondir"), "../..\n");
        File.WriteAllText(Path.Combine(admin, "HEAD"), $"ref: refs/heads/{branch}\n");
        File.WriteAllText(Path.Combine(admin, "gitdir"), Path.Combine(wt, ".git") + "\n");
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {admin}\n");
        return wt;
    }

    [Fact]
    public void A_walked_or_named_undecided_worktree_is_unshared_with_the_default_branchs_worktree()
    {
        var walked = WebApiOptions.ForRoot(RepoDiscovery.ResolveDetailed(_feat));
        var named = WebApiOptions.ForRoot(RepoDiscovery.ResolveDetailed(_base, _feat));

        Assert.Equal(new GitWorktree.UnsharedWorktree(_main, Bare: true), walked.Unshared);
        Assert.Equal(new GitWorktree.UnsharedWorktree(_main, Bare: true), named.Unshared);
        Assert.Equal(_feat, walked.GitRoot);
    }

    [Fact]
    public void A_redirected_worktree_shows_the_target_and_where_it_came_from()
    {
        File.WriteAllText(Path.Combine(_feat, ".knit", "config.local.json"), $$"""{ "knit_root": {{System.Text.Json.JsonSerializer.Serialize(_main)}} }""");

        var o = WebApiOptions.ForRoot(RepoDiscovery.ResolveDetailed(_feat));

        Assert.Equal(_main, o.RootPath);
        Assert.Equal(_feat, o.RedirectedFrom);
        Assert.Null(o.Unshared);
    }

    [Fact]
    public void The_default_branchs_worktree_is_not_unshared() =>
        Assert.Null(WebApiOptions.ForRoot(RepoDiscovery.ResolveDetailed(_main)).Unshared);
}
