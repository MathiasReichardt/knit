namespace Knit.Cli.Tests;

/// <summary>
/// The CLI side of root discovery: one shared not-found message
/// that says where it looked, a named override when one is set but doesn't
/// resolve, and a stderr line whenever the root is not the obvious one for the
/// working directory (the reported bug was a write landing in <c>~/.knit</c>
/// with nothing said about it).
/// </summary>
[Collection(nameof(CliCollection))]
public class RootDiscoveryCliTests
{
    [Fact]
    public void The_not_found_error_says_where_it_searched()
    {
        using var root = new TempKnitRoot();
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);

        var r = root.Run("list");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("no knit repo found", r.Stderr);
        Assert.Contains("searched from", r.Stderr);
        Assert.Contains("knit init", r.Stderr);
        Assert.Equal("", r.Stdout);
    }

    [Fact]
    public void A_set_but_unresolvable_override_is_named_instead()
    {
        using var root = new TempKnitRoot();
        Environment.SetEnvironmentVariable("KNIT_ROOT", Path.Combine(root.Path, "does-not-exist"));

        var r = root.Run("list");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("KNIT_ROOT points at", r.Stderr);
        Assert.Contains("was not attempted", r.Stderr);
        Assert.DoesNotContain("no knit repo found", r.Stderr);
    }

    [Fact]
    public void A_root_above_the_working_directory_is_announced()
    {
        using var root = new TempKnitRoot();
        root.Run("init", "--handle", "t", "--display", "T", "--non-interactive");
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);

        var r = RunIn(root, "src", "add", "task", "From a subdirectory");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("using knit root:", r.Stderr);
        Assert.Contains(root.Path, r.Stderr);
    }

    [Fact]
    public void A_subdirectory_of_the_git_repo_holding_the_root_stays_silent()
    {
        using var root = new TempKnitRoot();
        root.Run("init", "--handle", "t", "--display", "T", "--non-interactive");
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));

        var r = RunIn(root, "src", "add", "task", "From a subdirectory");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("", r.Stderr);
    }

    [Fact]
    public void A_knit_directory_that_is_not_a_repo_is_walked_past_and_reported()
    {
        using var root = new TempKnitRoot();
        root.Run("init", "--handle", "t", "--display", "T", "--non-interactive");
        Environment.SetEnvironmentVariable("KNIT_ROOT", null);
        var impostor = Path.Combine(root.Path, "tools", ".knit");
        Directory.CreateDirectory(impostor);
        File.WriteAllText(Path.Combine(impostor, "knit.exe"), "binary");

        var r = RunIn(root, Path.Combine("tools", "sub"), "list");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("skipped", r.Stderr);
        Assert.Contains("not a knit repo", r.Stderr);
        Assert.Contains("using knit root:", r.Stderr);
    }

    [Fact]
    public void Verify_reports_a_knit_directory_inside_the_repo_that_is_not_a_repo()
    {
        using var root = new TempKnitRoot();
        root.Run("init", "--handle", "t", "--display", "T", "--non-interactive");
        Directory.CreateDirectory(Path.Combine(root.Path, "tools", ".knit"));

        var r = root.Run("verify");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("stray / hidden files", r.Stdout);
        Assert.Contains(".knit", r.Stdout);
    }

    /// <summary>Run from a subdirectory of the repo — cwd is process-global, so
    /// it is restored before the fixture disposes.</summary>
    private static CliResult RunIn(TempKnitRoot root, string relative, params string[] args)
    {
        var sub = Path.Combine(root.Path, relative);
        Directory.CreateDirectory(sub);
        Directory.SetCurrentDirectory(sub);
        try { return root.Run(args); }
        finally { Directory.SetCurrentDirectory(root.Path); }
    }
}
