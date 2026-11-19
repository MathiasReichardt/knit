namespace Knit.Cli.Tests;

/// <summary>
/// Sources may grow into folders, and <c>kb split</c> repairs the
/// references its own move invalidated.
/// </summary>
[Collection(nameof(CliCollection))]
public class KbSourceFolderTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    private static string KbPath(TempKnitRoot root, params string[] parts) =>
        Path.Combine(new[] { root.Path, ".knit", "kb" }.Concat(parts).ToArray());

    private static void AddSource(TempKnitRoot root, string id) =>
        Assert.Equal(0, root.Run("kb", "add", "source", id, "--url", "https://example.com/" + id).ExitCode);

    [Fact]
    public void A_source_can_be_split_into_folder_form()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddSource(root, "vendor");

        Assert.Equal(0, root.Run("kb", "split", "source:vendor").ExitCode);

        Assert.False(File.Exists(KbPath(root, "sources", "vendor.md")));
        Assert.True(File.Exists(KbPath(root, "sources", "vendor", "readme.md")));
        // The canonical ref is unchanged — the resolver falls back to the folder.
        var show = root.Run("kb", "show", "source:vendor");
        Assert.Equal(0, show.ExitCode);
    }

    [Fact]
    public void A_split_source_hosts_nested_children()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddSource(root, "vendor");
        Assert.Equal(0, root.Run("kb", "split", "source:vendor").ExitCode);

        AddSource(root, "vendor/api-spec");

        Assert.True(File.Exists(KbPath(root, "sources", "vendor", "api-spec.md")));
        Assert.Equal(0, root.Run("kb", "show", "source:vendor/api-spec").ExitCode);
        // The id duplicates the filename, so it is the leaf, not the path.
        var body = File.ReadAllText(KbPath(root, "sources", "vendor", "api-spec.md"));
        Assert.Contains("id: api-spec", body);
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void A_nested_source_needs_its_parent_in_folder_form()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddSource(root, "vendor");

        var r = root.Run("kb", "add", "source", "vendor/api-spec", "--url", "https://example.com/x");

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("must be in folder form", r.Stderr);
    }

    [Fact]
    public void Flat_buckets_still_refuse_to_split()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "idea", "batch imports").ExitCode);

        var r = root.Run("kb", "split", "idea:batch-imports");

        Assert.NotEqual(0, r.ExitCode);
        // The wire list is wrapped to console width, so match its parts.
        Assert.Contains("grow-to-folder", r.Stderr);
        Assert.Contains("source", r.Stderr);
    }

    [Fact]
    public void Split_repoints_kb_ref_rows_in_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var itemPath = ItemFixtures.FirstItemFile(root);
        File.WriteAllText(itemPath, File.ReadAllText(itemPath) +
            "\n## Links\n\n" +
            "| Rel    | Target                                          | Notes |\n" +
            "|--------|-------------------------------------------------|-------|\n" +
            "| KB ref | [doc:architecture](../kb/docs/architecture.md)  |       |\n");

        var r = root.Run("kb", "split", "doc:architecture", "--json");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_rewritten\"", r.Stdout);
        var content = File.ReadAllText(itemPath);
        Assert.Contains("../kb/docs/architecture/readme.md", content);
        // The ref text is untouched — only the path half moved.
        Assert.Contains("doc:architecture]", content);
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Split_repoints_prose_links_in_other_kb_entries()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "overview").ExitCode);
        var overview = KbPath(root, "docs", "overview.md");
        File.WriteAllText(overview, File.ReadAllText(overview) +
            "\nSee [the architecture](architecture.md#storage) for the storage model.\n");

        var r = root.Run("kb", "split", "doc:architecture", "--json");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"files_relinked\"", r.Stdout);
        // The fragment survives the move; only the path half is rewritten.
        Assert.Contains("(architecture/readme.md#storage)", File.ReadAllText(overview));
        Assert.Equal(0, root.Run("verify").ExitCode);
    }

    [Fact]
    public void Split_leaves_unrelated_links_alone()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "overview").ExitCode);
        var overview = KbPath(root, "docs", "overview.md");
        File.WriteAllText(overview, File.ReadAllText(overview) +
            "\nSee [upstream](https://example.com/architecture.md) and [self](overview.md).\n");

        Assert.Equal(0, root.Run("kb", "split", "doc:architecture").ExitCode);

        var content = File.ReadAllText(overview);
        Assert.Contains("(https://example.com/architecture.md)", content);
        Assert.Contains("(overview.md)", content);
    }

    [Fact]
    public void Splitting_twice_is_a_reported_no_op()
    {
        using var root = new TempKnitRoot();
        Init(root);
        AddSource(root, "vendor");
        Assert.Equal(0, root.Run("kb", "split", "source:vendor").ExitCode);

        var r = root.Run("kb", "split", "source:vendor");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("already split", r.Stdout);
    }
}
