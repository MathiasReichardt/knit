using System.Text.Json;

namespace Knit.Cli.Tests;

/// <summary>
/// `knit link --kb-ref <ref>` writes a KB ref row in the
/// item's `## Links` table; `knit unlink <id> <bucket>:<slug>` removes
/// it; idempotent re-add updates the Notes column in place.
/// </summary>
[Collection(nameof(CliCollection))]
public class KbRefLinkTests
{
    private static (TempKnitRoot root, string itemId) BuildItemAndKb()
    {
        var root = new TempKnitRoot();
        Assert.Equal(0, root.Run("init", "--handle", "ci", "--display", "CI", "--non-interactive").ExitCode);
        Assert.Equal(0, root.Run("kb", "add", "doc", "architecture", "--summary", "arch overview").ExitCode);
        var add = root.Run("add", "task", "task one", "--json");
        Assert.Equal(0, add.ExitCode);
        var id = JsonDocument.Parse(add.Stdout).RootElement.GetProperty("id").GetString()!;
        return (root, id);
    }

    [Fact]
    public void Link_kbref_adds_row_and_round_trips()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            var r = root.Run("link", id, "--kb-ref", "doc:architecture", "--note", "guides the rewrite", "--json");
            Assert.Equal(0, r.ExitCode);

            var path = ItemFixtures.FirstItemFile(root);
            var content = File.ReadAllText(path);
            Assert.Contains("## Links", content);
            Assert.Contains("KB ref", content);
            Assert.Contains("doc:architecture", content);
            Assert.Contains("guides the rewrite", content);

            // Re-read via knit context to confirm the parser picks it up.
            var ctx = root.Run("context", id, "--json");
            Assert.Equal(0, ctx.ExitCode);
            Assert.Contains("doc:architecture", ctx.Stdout);
        }
    }

    [Fact]
    public void Link_kbref_is_idempotent_and_updates_note_in_place()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            Assert.Equal(0, root.Run("link", id, "--kb-ref", "doc:architecture", "--note", "first").ExitCode);
            Assert.Equal(0, root.Run("link", id, "--kb-ref", "doc:architecture", "--note", "second").ExitCode);

            var content = File.ReadAllText(ItemFixtures.FirstItemFile(root));
            // Exactly one KB-ref row to doc:architecture (count occurrences of the target slug in target cells).
            var rowCount = content.Split('\n').Count(l => l.Contains("KB ref") && l.Contains("architecture.md"));
            Assert.Equal(1, rowCount);
            // Note was updated, not the first one preserved.
            Assert.Contains("second", content);
            Assert.DoesNotContain("first", content);
        }
    }

    [Fact]
    public void Link_kbref_rejects_unresolved()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            var r = root.Run("link", id, "--kb-ref", "doc:nonexistent", "--json");
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("unresolved", r.Stderr);
            Assert.Contains("knit kb add", r.Stderr);
        }
    }

    [Fact]
    public void Link_kbref_rejects_item_bucket()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            var r = root.Run("link", id, "--kb-ref", $"item:{id}", "--json");
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("KB bucket", r.Stderr);
        }
    }

    [Fact]
    public void Link_rejects_kbref_combined_with_other_kind()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            var r = root.Run("link", id, "--kb-ref", "doc:architecture", "--related", id, "--json");
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("exactly one link kind", r.Stderr);
        }
    }

    [Fact]
    public void Unlink_removes_kbref_row()
    {
        var (root, id) = BuildItemAndKb();
        using (root)
        {
            Assert.Equal(0, root.Run("link", id, "--kb-ref", "doc:architecture", "--note", "n").ExitCode);
            var pre = File.ReadAllText(ItemFixtures.FirstItemFile(root));
            Assert.Contains("doc:architecture", pre);

            var u = root.Run("unlink", id, "doc:architecture", "--json");
            Assert.Equal(0, u.ExitCode);

            var post = File.ReadAllText(ItemFixtures.FirstItemFile(root));
            Assert.DoesNotContain("doc:architecture", post);
        }
    }
}
