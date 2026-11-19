namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class ShowPathOnlyTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Show_path_only_prints_repo_relative_path()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "find me").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("show", id, "--path-only");
        Assert.Equal(0, r.ExitCode);
        var line = r.Stdout.TrimEnd();
        Assert.StartsWith(".knit/items/", line);
        Assert.EndsWith(".md", line);
        Assert.DoesNotContain('\x1b', line);
    }

    [Fact]
    public void Show_path_only_accepts_prefix()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "one").ExitCode);
        var id = ItemFixtures.FirstItemId(root);
        var prefix = id[..8];

        var r = root.Run("show", prefix, "--path-only");
        Assert.Equal(0, r.ExitCode);
        Assert.EndsWith(".md" + Environment.NewLine, r.Stdout);
    }

    [Fact]
    public void Show_path_only_unknown_id_errors_with_stable_code()
    {
        using var root = new TempKnitRoot();
        Init(root);

        var r = root.Run("show", "deadbeef", "--path-only");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("not_found", r.Stderr);
    }

    [Fact]
    public void Show_path_only_json_wraps_path()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "wrap me").ExitCode);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("show", id, "--path-only", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"path\":", r.Stdout);
        Assert.Contains(".md", r.Stdout);
    }
}
