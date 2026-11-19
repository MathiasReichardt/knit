namespace Knit.Cli.Tests;

/// <summary>
/// One version surface — <c>knit version</c> and
/// <c>knit --version</c> are two spellings of the same line.
/// </summary>
[Collection(nameof(CliCollection))]
public class VersionSurfaceTests
{
    [Fact]
    public void Both_spellings_print_the_same_line()
    {
        using var root = new TempKnitRoot();

        var verb = root.Run("version");
        var flag = root.Run("--version");

        Assert.Equal(0, verb.ExitCode);
        Assert.Equal(0, flag.ExitCode);
        Assert.Equal(verb.Stdout, flag.Stdout);
        Assert.NotEmpty(verb.Stdout.Trim());
    }

    [Fact]
    public void The_version_carries_no_build_metadata()
    {
        using var root = new TempKnitRoot();

        // An SDK default (SourceRevisionId) can reintroduce `+<hash>` in the
        // informational version without anything in the props changing.
        foreach (var spelling in new[] { "version", "--version" })
            Assert.DoesNotContain("+", root.Run(spelling).Stdout);
    }
}
