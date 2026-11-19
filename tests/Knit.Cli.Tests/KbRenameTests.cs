namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class KbRenameTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "a", "--display", "A", "--non-interactive").ExitCode);

    [Fact]
    public void Rename_doc_moves_file_and_rewrites_indexes()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("kb", "rename", "doc:architecture", "arch-overview").ExitCode);

        Assert.False(File.Exists(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".knit", "kb", "docs", "arch-overview.md")));
        var bucketIndex = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "index.md"));
        Assert.Contains("arch-overview.md", bucketIndex);
    }

    [Fact]
    public void Rename_rewrites_kb_refs_in_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        // Add an item with a kb_refs entry pointing to the feature
        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        // Inject a KB ref row into the item's file. Flat items live at
        // .knit/items/<id-slug>.md, so the relative path to kb is "../kb/...".
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path);
        var injected = content +
            "\n## Links\n\n" +
            "| Rel    | Target                                       | Notes |\n" +
            "|--------|----------------------------------------------|-------|\n" +
            "| KB ref | [feature:user-mgmt](../kb/features/user-mgmt.md) |       |\n";
        File.WriteAllText(path, injected);

        var r = root.Run("kb", "rename", "feature:user-mgmt", "user-management", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_rewritten\"", r.Stdout);
        var newContent = File.ReadAllText(path);
        Assert.Contains("feature:user-management", newContent);
        Assert.Contains("../kb/features/user-management.md", newContent);
        Assert.DoesNotContain("feature:user-mgmt.md", newContent);
    }

    [Fact]
    public void Rename_to_nested_rewrites_subdoc_refs_in_items()
    {
        // A folder-form entry with a sub-doc is moved under a new
        // parent. The whole folder travels, so an item ref pointing at the
        // sub-doc must follow it, not just the parent's readme.
        using var root = new TempKnitRoot();
        Init(root);
        // Target parent "bar" must already be folder-form.
        Assert.Equal(0, root.Run("kb", "add", "feature", "bar").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:bar").ExitCode);
        // The entry being renamed, grown to a folder with one sub-doc.
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo/wiring").ExitCode);

        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path) +
            "\n## Links\n\n" +
            "| Rel    | Target                                             | Notes |\n" +
            "|--------|----------------------------------------------------|-------|\n" +
            "| KB ref | [feature:foo](../kb/features/foo/readme.md)        |       |\n" +
            "| KB ref | [feature:foo/wiring](../kb/features/foo/wiring.md) |       |\n";
        File.WriteAllText(path, content);

        Assert.Equal(0, root.Run("kb", "rename", "feature:foo", "bar/foo").ExitCode);

        var newContent = File.ReadAllText(path);
        Assert.Contains("feature:bar/foo](../kb/features/bar/foo/readme.md)", newContent);
        Assert.Contains("feature:bar/foo/wiring](../kb/features/bar/foo/wiring.md)", newContent);
        Assert.DoesNotContain("features/foo/wiring.md", newContent);
        Assert.DoesNotContain("feature:foo/wiring]", newContent);
    }

    [Fact]
    public void Rename_top_level_rewrites_parent_and_subdoc_refs()
    {
        // Flat top-level rename of a folder-form entry with a sub-doc
        // rewrites both the parent ref and the sub-doc ref.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo/wiring").ExitCode);

        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var path = ItemFixtures.FirstItemFile(root);
        var content = File.ReadAllText(path) +
            "\n## Links\n\n" +
            "| Rel    | Target                                             | Notes |\n" +
            "|--------|----------------------------------------------------|-------|\n" +
            "| KB ref | [feature:foo](../kb/features/foo/readme.md)        |       |\n" +
            "| KB ref | [feature:foo/wiring](../kb/features/foo/wiring.md) |       |\n";
        File.WriteAllText(path, content);

        Assert.Equal(0, root.Run("kb", "rename", "feature:foo", "baz").ExitCode);

        var newContent = File.ReadAllText(path);
        Assert.Contains("feature:baz](../kb/features/baz/readme.md)", newContent);
        Assert.Contains("feature:baz/wiring](../kb/features/baz/wiring.md)", newContent);
        Assert.DoesNotContain("features/foo/", newContent);
    }

    [Fact]
    public void Rename_rewrites_prose_refs_and_links()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "old-name").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "doc:old-name").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "old-name/sub").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "old-namesake").ExitCode);
        Assert.Equal(0, root.Run("design", "new", "web", "--title", "Web").ExitCode);

        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var itemPath = ItemFixtures.FirstItemFile(root);
        File.AppendAllText(itemPath,
            "\nSee `doc:old-name` and `doc:old-name/sub`, not `doc:old-namesake` or `doc:old-name-x`.\n" +
            "Read [the doc](../kb/docs/old-name/readme.md#intro) and [sub](../kb/docs/old-name/sub.md).\n" +
            "\n```\n`doc:old-name` [fenced](../kb/docs/old-name/readme.md)\n```\n");
        var designPath = Path.Combine(root.Path, ".knit", "designs", "web-design.md");
        File.AppendAllText(designPath, "\nBuilds on `doc:old-name/sub`.\n");

        var r = root.Run("kb", "rename", "doc:old-name", "new-name", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"files_relinked\"", r.Stdout);
        Assert.Contains("web-design.md", r.Stdout);

        var item = File.ReadAllText(itemPath);
        Assert.Contains("See `doc:new-name` and `doc:new-name/sub`", item);
        Assert.Contains("not `doc:old-namesake` or `doc:old-name-x`", item);
        Assert.Contains("[the doc](../kb/docs/new-name/readme.md#intro)", item);
        Assert.Contains("[sub](../kb/docs/new-name/sub.md)", item);
        Assert.Contains("`doc:old-name` [fenced](../kb/docs/old-name/readme.md)", item);
        Assert.Contains("Builds on `doc:new-name/sub`.", File.ReadAllText(designPath));
    }

    [Fact]
    public void Rename_reports_prose_rewrites_in_human_output()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "login flow").ExitCode);
        var itemPath = ItemFixtures.FirstItemFile(root);
        File.AppendAllText(itemPath, "\nSee `doc:architecture` and [arch](../kb/docs/architecture.md).\n");

        var r = root.Run("kb", "rename", "doc:architecture", "arch");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("1 file(s) with prose refs", r.Stdout);
        Assert.Contains("See `doc:arch` and [arch](../kb/docs/arch.md).", File.ReadAllText(itemPath));
        Assert.Equal(0, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Supersede_links_adrs_both_ways()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Old decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "New decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "supersede", "0002-new-decision", "0001-old-decision").ExitCode);

        var old = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "adrs", "0001-old-decision.md"));
        Assert.Contains("status: superseded", old);
        Assert.Contains("superseded_by: adr:0002-new-decision", old);

        var fresh = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "adrs", "0002-new-decision.md"));
        Assert.Contains("supersedes:", fresh);
        Assert.Contains("- adr:0001-old-decision", fresh);
    }

    [Fact]
    public void Supersede_with_short_slugs_does_not_create_duplicates()
    {
        // Regression: `kb supersede 0002 0001` used to write
        // fresh adrs/0001.md / adrs/0002.md next to the full-slug files,
        // because KbStore.Write resolved through the short-slug Ref.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Old decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "New decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "supersede", "0002", "0001").ExitCode);

        var adrDir = Path.Combine(root.Path, ".knit", "kb", "adrs");
        var files = Directory.GetFiles(adrDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("index.md", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains("0001-old-decision.md", files);
        Assert.Contains("0002-new-decision.md", files);
        Assert.DoesNotContain("0001.md", files);
        Assert.DoesNotContain("0002.md", files);
    }

    [Fact]
    public void Add_adr_and_supersede_pass_verify_clean()
    {
        // End-to-end: a fresh ADR pair plus a supersede must not
        // produce any title drift or duplicate files.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Old decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "New decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "supersede", "0002", "0001").ExitCode);

        var verify = root.Run("verify");
        Assert.Equal(0, verify.ExitCode);
    }

    [Fact]
    public void Supersede_rejects_non_adr_refs()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "doc", "a").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "b").ExitCode);
        var r = root.Run("kb", "supersede", "doc:a", "doc:b");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void Rename_adr_rewrites_superseding_entry_frontmatter()
    {
        // B.supersedes points at A; renaming A must rewrite B's
        // frontmatter ref (and the symmetric superseded_by on A's file).
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Old decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "adr", "New decision").ExitCode);
        Assert.Equal(0, root.Run("kb", "supersede", "0002-new-decision", "0001-old-decision").ExitCode);

        var r = root.Run("kb", "rename", "adr:0001-old-decision", "0001-renamed", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"kb_entries_affected\"", r.Stdout);
        Assert.Contains("adr:0002-new-decision", r.Stdout);

        var fresh = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "adrs", "0002-new-decision.md"));
        Assert.Contains("- adr:0001-renamed", fresh);
        Assert.DoesNotContain("adr:0001-old-decision", fresh);

        var renamed = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "adrs", "0001-renamed.md"));
        Assert.Contains("superseded_by: adr:0002-new-decision", renamed);
    }

    [Fact]
    public void Rename_feature_rewrites_bare_feature_slug_in_referrers()
    {
        // The `feature:` frontmatter field holds a bare slug, not a
        // full ref — renaming the feature must rewrite referrers' slugs.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--feature", "user-mgmt").ExitCode);

        Assert.Equal(0, root.Run("kb", "rename", "feature:user-mgmt", "user-management").ExitCode);

        var doc = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md"));
        Assert.Contains("feature: user-management", doc);
        Assert.DoesNotContain("feature: user-mgmt", doc);
    }

    [Fact]
    public void Rename_feature_folder_rewrites_subdoc_feature_slug_in_referrers()
    {
        // Folder-form rename cascades — a referrer pointing at a
        // sub-doc (feature:foo/wiring) follows the parent slug change.
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:foo").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "feature", "foo/wiring").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--feature", "foo/wiring").ExitCode);

        Assert.Equal(0, root.Run("kb", "rename", "feature:foo", "bar").ExitCode);

        var doc = File.ReadAllText(Path.Combine(root.Path, ".knit", "kb", "docs", "architecture.md"));
        Assert.Contains("feature: bar/wiring", doc);
        Assert.DoesNotContain("feature: foo/wiring", doc);
    }

    [Fact]
    public void Move_renames_item_entry_id_stays()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "First name").ExitCode);
        var oldFile = ItemFixtures.FirstItemFile(root);
        var id = ItemFixtures.FirstItemId(root);

        var r = root.Run("move", id, "renamed-task");
        Assert.Equal(0, r.ExitCode);
        Assert.False(File.Exists(oldFile));
        var newFile = ItemFixtures.FirstItemFile(root);
        var entry = ItemFixtures.EntryName(newFile);
        Assert.EndsWith("-renamed-task", entry);
    }

    [Fact]
    public void Split_promotes_file_to_folder()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);

        var fileForm = Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt.md");
        Assert.True(File.Exists(fileForm));

        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);

        Assert.False(File.Exists(fileForm));
        var readme = Path.Combine(root.Path, ".knit", "kb", "features", "user-mgmt", "readme.md");
        Assert.True(File.Exists(readme));

        // ref still resolves
        Assert.Equal(0, root.Run("kb", "show", "feature:user-mgmt", "--path-only").ExitCode);
    }

    [Fact]
    public void Split_rejects_non_growable_buckets()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "adr", "Pick").ExitCode);
        var r = root.Run("kb", "split", "adr:0001-pick");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public void Split_already_folder_is_noop()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("kb", "add", "feature", "user-mgmt").ExitCode);
        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);
        // Second call should be no-op (still 0)
        Assert.Equal(0, root.Run("kb", "split", "feature:user-mgmt").ExitCode);
    }
}
