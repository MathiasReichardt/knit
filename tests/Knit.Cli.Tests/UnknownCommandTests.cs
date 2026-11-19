namespace Knit.Cli.Tests;

/// <summary>
/// A typo'd verb is an error at every nesting level — including with
/// <c>--help</c>, where System.CommandLine would otherwise print help and
/// exit 0.
/// </summary>
[Collection(nameof(CliCollection))]
public class UnknownCommandTests
{
    private static TempKnitRoot Repo()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        return root;
    }

    [Theory]
    [InlineData("aasaf")]
    [InlineData("kb aasaf")]
    [InlineData("project milestone aasaf")]
    public void An_unknown_verb_is_reported(string typed)
    {
        using var root = Repo();
        var args = typed.Split(' ');

        foreach (var invocation in new[] { args, [.. args, "--help"] })
        {
            var r = root.Run(invocation);

            Assert.Equal(2, r.ExitCode);
            Assert.Contains("unknown command", r.Stderr);
            Assert.Contains("aasaf", r.Stderr);
            Assert.Empty(r.Stdout.Trim());
        }
    }

    [Fact]
    public void The_hint_names_the_level_that_was_reached()
    {
        using var root = Repo();

        var r = root.Run("kb", "aasaf", "--help");

        Assert.Contains("kb aasaf", r.Stderr);
        Assert.Contains("knit kb --help", r.Stderr);
    }

    [Fact]
    public void A_leaf_command_reports_an_argument_it_cannot_place()
    {
        using var root = Repo();

        var r = root.Run("add", "task", "wire it up", "stray");

        Assert.Equal(2, r.ExitCode);
        Assert.Contains("unexpected argument: stray", r.Stderr);
    }

    [Fact]
    public void A_real_command_still_gets_its_help()
    {
        using var root = Repo();

        var r = root.Run("kb", "--help");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("kb", r.Stdout);
    }

    [Fact]
    public void The_error_is_json_when_asked()
    {
        using var root = Repo();

        var r = root.Run("aasaf", "--json");

        Assert.Equal(2, r.ExitCode);
        Assert.Contains("\"error_code\": \"bad_argument\"", r.Stderr);
        Assert.Contains("aasaf", r.Stderr);
    }
}
