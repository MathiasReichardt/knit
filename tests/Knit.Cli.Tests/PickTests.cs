namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class PickTests
{
    [Fact]
    public void Pick_refuses_when_stderr_is_redirected()
    {
        // The test fixture redirects both stdout AND stderr (TempKnitRoot.Run)
        // — that's exactly the non-interactive contract pick must reject.
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "anything").ExitCode);

        var r = root.Run("pick");
        Assert.Equal(2, r.ExitCode);
        Assert.Empty(r.Stdout); // no id leaked when refused
        Assert.Contains("interactive terminal", r.Stderr);
    }

    [Fact]
    public void Pick_help_includes_shell_substitution_examples()
    {
        using var root = new TempKnitRoot();
        var help = root.Run("pick", "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("$(knit pick", help.Stdout);
    }
}
