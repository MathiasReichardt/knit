namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class KbIndexerTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Add_adr_regenerates_bucket_index()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Pick storage", "--summary", "tradeoffs").ExitCode);

        var indexPath = Path.Combine(root.Path, ".knit", "kb", "adrs", "index.md");
        Assert.True(File.Exists(indexPath));
        var content = File.ReadAllText(indexPath);
        Assert.Contains("- [0001 — Pick storage]", content);
        Assert.Contains("— tradeoffs", content);
    }

    [Fact]
    public void Add_doc_regenerates_master_index()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--summary", "fit").ExitCode);

        var master = Path.Combine(root.Path, ".knit", "kb", "kb-index.md");
        Assert.True(File.Exists(master));
        var content = File.ReadAllText(master);
        Assert.Contains("## Cross-cutting docs", content);
        Assert.Contains("doc:architecture", content);
    }

    [Fact]
    public void Set_regenerates_indexes()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Pick").ExitCode);
        Assert.Equal(0, root.Run("kb", "set", "adr:0001-pick", "summary", "updated").ExitCode);
        var indexPath = Path.Combine(root.Path, ".knit", "kb", "adrs", "index.md");
        Assert.Contains("updated", File.ReadAllText(indexPath));
    }

    [Fact]
    public void Kb_index_command_regenerates_from_scratch()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "arch").ExitCode);

        var bucketIndex = Path.Combine(root.Path, ".knit", "kb", "docs", "index.md");
        File.Delete(bucketIndex);
        Assert.False(File.Exists(bucketIndex));

        var r = root.Run("kb", "index", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(bucketIndex));
        Assert.Contains("\"files_written\"", r.Stdout);
    }

    [Fact]
    public void Master_index_lists_latest_decisions()
    {
        using var root = new TempKnitRoot();
        Init(root);
        for (var i = 0; i < 3; i++)
            Assert.Equal(0, root.Run("kb", "add", "adr", $"Decision {i}").ExitCode);

        var master = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "kb-index.md"));
        Assert.Contains("## Latest decisions", master);
        // ADR rows keep the `[NNNN — title]` form and carry no `adr:NNNN`
        // code-span (the id already plays the ref role).
        Assert.Contains("[0003 — Decision 2]", master);
        Assert.DoesNotContain("adr:0003", master);
    }

    [Fact]
    public void Feature_sub_docs_render_nested_with_ref_code_spans()
    {
        using var root = new TempKnitRoot();
        Init(root);
        // Two top-level features; one grows a folder with two sub-docs.
        // Sub-docs require the parent in folder form, so split it first.
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt", "--summary", "accounts").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "billing").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt/wiring").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt/api").ExitCode);

        var index = File.ReadAllText(
            Path.Combine(root.Path, ".knit", "kb", "features", "index.md"));

        // Parent line: title-first text + copy-pasteable ref code-span.
        Assert.Contains("- [user-mgmt](user-mgmt/readme.md) `feature:user-mgmt` — accounts", index);
        // Sub-docs nested one level (2-space indent), shown by leaf segment,
        // with their full ref in the code-span. Sibling top-level stays flat.
        Assert.Contains("  - [api](user-mgmt/api.md) `feature:user-mgmt/api`", index);
        Assert.Contains("  - [wiring](user-mgmt/wiring.md) `feature:user-mgmt/wiring`", index);
        Assert.Contains("- [billing](billing.md) `feature:billing`", index);
        Assert.DoesNotContain("  - [billing]", index);

        // Master index mirrors the nesting + format B.
        var master = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "kb-index.md"));
        Assert.Contains("`feature:user-mgmt`", master);
        Assert.Contains("  - [wiring](features/user-mgmt/wiring.md) `feature:user-mgmt/wiring`", master);
    }
}
