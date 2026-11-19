namespace Knit.Cli.Tests;

/// <summary>
/// `knit init` is the first command a new user runs, so it must never
/// stack-trace. Without a terminal to prompt on, Spectre's Ask/Confirm throw
/// rather than fall back — so the missing-prompt case has to be caught up front
/// and reported like any other argument error.
/// </summary>
[Collection(nameof(CliCollection))]
public class InitPromptGuardTests
{
    [Fact]
    public void Init_without_a_handle_and_without_a_tty_errors_cleanly()
    {
        Assert.True(Console.IsInputRedirected,
            "test host is expected to run with redirected stdin; without that this test can't exercise the guard");

        using var root = new TempKnitRoot();
        var r = root.Run("init");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("--handle", r.Stderr);
        Assert.DoesNotContain("Exception", r.Stderr);
        Assert.DoesNotContain("   at ", r.Stderr);
        Assert.Equal("", r.Stdout);
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit")),
            "a refused init must not leave a half-scaffolded repo behind");
    }

    [Fact]
    public void Init_without_a_tty_still_scaffolds_when_both_names_are_given()
    {
        using var root = new TempKnitRoot();

        // No --non-interactive: the pointer prompt must be skipped rather than
        // throwing once the required values are already known.
        var r = root.Run("init", "--handle", "ci", "--display", "CI");

        Assert.Equal(0, r.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(root.Path, ".knit")));
        Assert.False(File.Exists(Path.Combine(root.Path, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(root.Path, "AGENTS.md")));
    }
}
