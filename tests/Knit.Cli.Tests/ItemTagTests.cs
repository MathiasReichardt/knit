namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class ItemTagTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static string Add(TempKnitRoot root, string title)
    {
        Assert.Equal(0, root.Run("add", "task", title).ExitCode);
        var r = root.Run("list", "-q", title, "--json");
        Assert.Equal(0, r.ExitCode);
        // The freshly-added item is the only one matching its unique title.
        var m = System.Text.RegularExpressions.Regex.Match(r.Stdout, "\"id\": \"([^\"]+)\"");
        Assert.True(m.Success);
        return m.Groups[1].Value;
    }

    [Fact]
    public void Set_tags_normalizes_and_roundtrips()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Add(root, "alpha");

        // Dupes (case-insensitive) collapse to first-seen casing; order kept.
        Assert.Equal(0, root.Run("set", id, "tags=API, api ,security,API").ExitCode);

        var show = root.Run("list", "-q", "alpha", "--json");
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("\"tags\"", show.Stdout);
        Assert.Contains("\"API\"", show.Stdout);
        Assert.Contains("\"security\"", show.Stdout);
        Assert.DoesNotContain("\"api\"", show.Stdout);
    }

    [Fact]
    public void Set_tags_empty_clears_and_omits_key()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var id = Add(root, "beta");

        Assert.Equal(0, root.Run("set", id, "tags=x,y").ExitCode);
        Assert.Equal(0, root.Run("set", id, "tags=").ExitCode);

        var file = ItemFixtures.FirstItemFile(root);
        Assert.DoesNotContain("tags:", File.ReadAllText(file));
    }

    [Fact]
    public void List_tag_filter_is_and_semantics()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var both = Add(root, "both");
        var oneOnly = Add(root, "oneonly");
        Assert.Equal(0, root.Run("set", both, "tags=api,security").ExitCode);
        Assert.Equal(0, root.Run("set", oneOnly, "tags=api").ExitCode);

        // AND: only the item carrying every requested tag survives.
        var r = root.Run("list", "--tag", "api", "--tag", "security", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("both", r.Stdout);
        Assert.DoesNotContain("oneonly", r.Stdout);
    }

    [Fact]
    public void List_no_tag_filter_excludes()
    {
        using var root = new TempKnitRoot();
        Init(root);
        var tagged = Add(root, "tagged");
        var clean = Add(root, "clean");
        Assert.Equal(0, root.Run("set", tagged, "tags=wip").ExitCode);

        var r = root.Run("list", "--no-tag", "wip", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("clean", r.Stdout);
        Assert.DoesNotContain("tagged", r.Stdout);
    }
}
