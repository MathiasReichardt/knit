namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class InitScaffoldTests
{
    [Fact]
    public void Init_does_not_create_kb_folder()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit", "kb")),
            ".knit/kb/ should not be created by init");
    }

    [Fact]
    public void Reinit_on_existing_knit_is_a_noop_with_hint()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

        var r = root.Run("init", "--handle", "a", "--display", "A", "--non-interactive");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("already initialized", r.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New to Knit?", r.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void First_kb_add_creates_only_needed_bucket_folder()
    {
        using var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);

        Assert.True(Directory.Exists(Path.Combine(root.Path, ".knit", "kb", "docs")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit", "kb", "adrs")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, ".knit", "kb", "logs")));
    }
}
