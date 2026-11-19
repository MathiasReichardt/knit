using System.Text.Json;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Cli.Tests;

[Collection(nameof(CliCollection))]
public class SlateTests
{
    private static void Init(TempKnitRoot root) =>
        Assert.Equal(0, root.Run("init", "--handle", "alice", "--display", "Alice", "--non-interactive").ExitCode);

    private static string SlatesFile(TempKnitRoot root) =>
        Path.Combine(root.Path, ".knit", "slates.md");

    private static ItemStore Store(TempKnitRoot root) =>
        new(root.Path, new SimpleFrontmatterSerializer());

    [Fact]
    public void Add_creates_registry_lazily_and_rejects_duplicates()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.False(File.Exists(SlatesFile(root)));

        Assert.Equal(0, root.Run("slate", "add", "2026 W27", "--status", "active", "--note", "current").ExitCode);
        Assert.True(File.Exists(SlatesFile(root)));
        Assert.Contains("2026-w27", File.ReadAllText(SlatesFile(root)));

        var dup = root.Run("slate", "add", "2026-w27");
        Assert.NotEqual(0, dup.ExitCode);
        Assert.Contains("already exists", dup.Stderr);
    }

    [Fact]
    public void List_orders_active_first_and_hides_archived_without_all()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "later", "--status", "planned").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "now", "--status", "active").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "old", "--status", "archived").ExitCode);

        var list = root.Run("slate", "list", "--json");
        Assert.Equal(0, list.ExitCode);
        Assert.DoesNotContain("\"old\"", list.Stdout);
        Assert.True(list.Stdout.IndexOf("\"now\"", StringComparison.Ordinal)
            < list.Stdout.IndexOf("\"later\"", StringComparison.Ordinal));

        var all = root.Run("slate", "list", "--all", "--json");
        Assert.Contains("\"old\"", all.Stdout);
    }

    [Fact]
    public void Move_reorders_rows_and_set_updates_status_and_note()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "a").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "b").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "c").ExitCode);

        Assert.Equal(0, root.Run("slate", "move", "c", "--before", "a").ExitCode);
        var text = File.ReadAllText(SlatesFile(root));
        Assert.True(text.IndexOf("| c", StringComparison.Ordinal) < text.IndexOf("| a", StringComparison.Ordinal));

        Assert.Equal(0, root.Run("slate", "set", "b", "status=active", "note=next up").ExitCode);
        text = File.ReadAllText(SlatesFile(root));
        Assert.Contains("active", text);
        Assert.Contains("next up", text);

        Assert.Equal(0, root.Run("slate", "archive", "a").ExitCode);
        Assert.Contains("archived", File.ReadAllText(SlatesFile(root)));

        var badStatus = root.Run("slate", "set", "b", "status=bogus");
        Assert.NotEqual(0, badStatus.ExitCode);
    }

    [Fact]
    public void Rename_walks_items_and_reports_count()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "first").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "second").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "unassigned").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "old-name").ExitCode);

        var store = Store(root);
        foreach (var item in store.AllItems().Where(i => i.Title != "unassigned").ToList())
            store.Write(item with { Slate = "old-name" });

        var r = root.Run("slate", "rename", "old-name", "new-name", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_updated\":2", r.Stdout);
        Assert.Contains("new-name", File.ReadAllText(SlatesFile(root)));
        Assert.DoesNotContain("old-name", File.ReadAllText(SlatesFile(root)));

        var slates = Store(root).AllItems().Select(i => i.Slate).ToList();
        Assert.Equal(2, slates.Count(s => s == "new-name"));
        Assert.DoesNotContain("old-name", slates);
    }

    [Fact]
    public void Rename_of_item_only_slate_works_without_registry_row()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "loose").ExitCode);

        var store = Store(root);
        store.Write(store.AllItems().Single() with { Slate = "hand-typed" });

        var r = root.Run("slate", "rename", "hand-typed", "tidy");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("tidy", Store(root).AllItems().Single().Slate);

        var missing = root.Run("slate", "rename", "nowhere", "elsewhere");
        Assert.NotEqual(0, missing.ExitCode);
    }

    [Fact]
    public void Rm_clears_the_slate_from_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "keeper").ExitCode);
        Assert.Equal(0, root.Run("slate", "add", "doomed").ExitCode);

        var store = Store(root);
        store.Write(store.AllItems().Single() with { Slate = "doomed" });

        var r = root.Run("slate", "rm", "doomed", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_cleared\":1", r.Stdout);
        Assert.DoesNotContain("doomed", File.ReadAllText(SlatesFile(root)));
        Assert.Null(Store(root).AllItems().Single().Slate);
        Assert.DoesNotContain("slate:", File.ReadAllText(ItemFixtures.FirstItemFile(root)));

        Assert.NotEqual(0, root.Run("slate", "rm", "doomed").ExitCode);
    }

    [Fact]
    public void Verify_warns_on_a_slate_missing_from_the_registry()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("slate", "add", "known").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "a").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "b").ExitCode);
        Assert.Equal(0, root.Run("add", "task", "c").ExitCode);
        var store = Store(root);
        var items = store.AllItems().OrderBy(i => i.Title).ToList();
        store.Write(items[0] with { Slate = "typo" });
        store.Write(items[1] with { Slate = "typo" });
        store.Write(items[2] with { Slate = "known" });

        // Warning class: exit 0 without --strict, 4 with it.
        var r = root.Run("verify", "--json");
        Assert.Equal(0, r.ExitCode);
        using var doc = JsonDocument.Parse(r.Stdout);
        var finding = Assert.Single(doc.RootElement.GetProperty("unregistered_slates").EnumerateArray().ToList());
        Assert.Equal("typo", finding.GetProperty("slate").GetString());
        Assert.Equal(2, finding.GetProperty("paths").GetArrayLength());
        Assert.Equal(4, root.Run("verify", "--strict").ExitCode);

        var human = root.Run("verify");
        Assert.Contains("slates not in the registry (1)", human.Stdout);
        Assert.Contains("knit slate add", human.Stdout);

        Assert.Equal(0, root.Run("slate", "add", "typo").ExitCode);
        Assert.Equal(0, root.Run("verify", "--strict").ExitCode);
    }

    [Fact]
    public void Rm_clears_a_slate_that_is_only_on_items()
    {
        using var root = new TempKnitRoot();
        Init(root);
        Assert.Equal(0, root.Run("add", "task", "stray").ExitCode);
        var store = Store(root);
        store.Write(store.AllItems().Single() with { Slate = "hand-typed" });

        var r = root.Run("slate", "rm", "hand-typed", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("\"items_cleared\":1", r.Stdout);
        Assert.Null(Store(root).AllItems().Single().Slate);
    }
}
