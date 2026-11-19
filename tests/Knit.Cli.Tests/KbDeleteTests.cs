namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class KbDeleteTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Delete_flat_strips_kb_refs_in_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path) +
            "\n## Links\n\n" +
            "| Rel    | Target                                           | Notes |\n" +
            "|--------|--------------------------------------------------|-------|\n" +
            "| KB ref | [feature:user-mgmt](../kb/features/user-mgmt.md) |       |\n";
        File.WriteAllText(path, content);

        // --force is required because the test harness redirects stderr (no TTY).
        var r = root.Run("kb", "delete", "feature:user-mgmt", "--force", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_affected\"", r.Stdout);

        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt.md")));
        var newContent = File.ReadAllText(path);
        Assert.DoesNotContain("feature:user-mgmt", newContent);
        Assert.DoesNotContain("../kb/features/user-mgmt.md", newContent);
    }

    [Fact]
    public void Delete_folder_with_siblings_requires_delete_folder_flag()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo/wiring").ExitCode);

        var folder = Path.Combine(root.Path, ".knit", "kb", "features", "foo");

        // --force alone does not bypass the folder-has-siblings guard.
        var blocked = root.Run("kb", "delete", "feature:foo", "--force");
        Assert.NotEqual(0, blocked.ExitCode);
        Assert.True(Directory.Exists(folder), "blocked delete must leave the folder intact");

        // --delete-folder removes the whole folder recursively.
        var ok = root.Run("kb", "delete", "feature:foo", "--force", "--delete-folder");
        Assert.Equal(0, ok.ExitCode);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Delete_adr_clears_superseded_by_in_other_entry()
    {
        // Deleting the superseding ADR clears the dead superseded_by
        // ref on the entry it had superseded.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Old decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "New decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "supersede", "0002-new-decision", "0001-old-decision").ExitCode);

        var r = root.Run("kb", "delete", "adr:0002-new-decision", "--force", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"kb_entries_affected\"", r.Stdout);
        Assert.Contains("adr:0001-old-decision", r.Stdout);

        var old = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "adrs", "0001-old-decision.md"));
        Assert.DoesNotContain("adr:0002-new-decision", old);
    }

    [Fact]
    public void Delete_feature_clears_bare_feature_slug_in_referrers()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--feature", "user-mgmt").ExitCode);

        Assert.Equal(0, root.Run("kb", "delete", "feature:user-mgmt", "--force").ExitCode);

        var doc = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md"));
        Assert.DoesNotContain("feature: user-mgmt", doc);
    }

    [Fact]
    public void Delete_without_force_refuses_when_not_a_tty()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);

        // stderr is redirected by the harness → no interactive confirm possible.
        var r = root.Run("kb", "delete", "doc:architecture");
        Assert.NotEqual(0, r.ExitCode);
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md")),
            "refused delete must leave the file in place");
    }
}
