using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class FrontmatterTests
{
    private static readonly ItemFile File = new(new SimpleFrontmatterSerializer());

    [Fact]
    public void Read_ParsesCanonicalItem()
    {
        const string content = """
            ---
            id: 01968a73-3aef-7c2c-9efc-aa1a2c79c5e7
            title: Add dark mode
            type: feature
            state: in-progress
            assignee: alice
            rank: n
            created: 2026-05-01T10:00:00Z
            ---

            Body text here.

            ## Acceptance Criteria
            - [ ] do it

            ## Comments

            #### `01968a74-1111-7000-9000-aaaaaaaaaaaa` · alice · 2026-05-05 14:23 UTC
            First comment.
            """;

        var item = File.Read(content);

        Assert.Equal("01968a73-3aef-7c2c-9efc-aa1a2c79c5e7", item.Id);
        Assert.Equal("Add dark mode", item.Title);
        Assert.Equal(ItemType.Feature, item.Type);
        Assert.Equal(ItemState.InProgress, item.State);
        Assert.Equal("alice", item.Assignee);
        Assert.Equal("n", item.Rank);
        Assert.Single(item.Comments);
        Assert.Equal("alice", item.Comments[0].Author);
        Assert.Equal("First comment.", item.Comments[0].Body);
    }

    [Fact]
    public void Read_LenientOnMissingFields()
    {
        const string content = """
            ---
            title: Sketchy item
            ---
            """;
        var item = File.Read(content);
        Assert.Equal("Sketchy item", item.Title);
        Assert.Equal(ItemType.Task, item.Type);
        Assert.Equal(ItemState.Todo, item.State);
    }

    [Fact]
    public void Read_LenientOnMalformedYaml()
    {
        const string content = """
            ---
            title: "broken: : :
            ---
            body
            """;
        var item = File.Read(content);
        Assert.NotNull(item);
    }

    [Fact]
    public void RoundTrip_PreservesLinkRows()
    {
        var original = new Item
        {
            Id = Guid.CreateVersion7().ToString("D"),
            Title = "Test item",
            Type = ItemType.Bug,
            State = ItemState.InProgress,
            Assignee = "bob",
            Rank = "n",
            Created = DateTimeOffset.Parse("2026-05-01T10:00:00Z").ToUniversalTime(),
            Body = "Body here\n",
            LinkRows =
            [
                new LinkRow(LinkRel.Blocks, "OAuth migration", "../01968a73-3aef.md", null),
            ],
        };

        var serialized = File.Write(original);
        var roundTripped = File.Read(serialized);

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Title, roundTripped.Title);
        Assert.Equal(original.Type, roundTripped.Type);
        Assert.Equal(original.State, roundTripped.State);
        Assert.Equal(original.Assignee, roundTripped.Assignee);
        Assert.Equal(original.Rank, roundTripped.Rank);
        Assert.Single(roundTripped.LinkRows);
        Assert.Equal(LinkRel.Blocks, roundTripped.LinkRows[0].Rel);
        Assert.Equal("../01968a73-3aef.md", roundTripped.LinkRows[0].TargetPath);
    }

    [Fact]
    public void Write_renders_links_table_in_body()
    {
        var item = new Item
        {
            Id = "11111111-1111-7111-8111-111111111111",
            Title = "child",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            LinkRows =
            [
                new LinkRow(LinkRel.Parent, "umbrella", "../22222222-2222-7222-8222-222222222222-x.md", null),
                new LinkRow(LinkRel.Blocks, "OAuth", "../33333333-3333-7333-8333-333333333333-y.md", null),
                new LinkRow(LinkRel.KbRef, "doc:security", "../../kb/docs/security.md", "auth assumptions"),
            ],
        };
        var s = File.Write(item);
        Assert.Contains("## Links", s);
        Assert.Contains("| Parent ", s);
        Assert.Contains("| Blocks ", s);
        Assert.Contains("| KB ref ", s);
        Assert.Contains("../../kb/docs/security.md", s);
        // Legacy frontmatter keys must NOT appear.
        Assert.DoesNotContain("\nlinks:", s);
        Assert.DoesNotContain("\nblockers:", s);
        Assert.DoesNotContain("\nkb_refs:", s);
    }

    [Fact]
    public void Read_derives_parent_id_from_parent_row()
    {
        var content =
            "---\n" +
            "id: AAAAAAAAAA11111111\n" +
            "title: child\n" +
            "type: task\n" +
            "state: todo\n" +
            "---\n\n" +
            "## Links\n\n" +
            "| Rel    | Target                          | Notes |\n" +
            "|--------|---------------------------------|-------|\n" +
            "| Parent | [umbrella](../BBBBBBBBBB22222222-x.md) |       |\n";
        var item = File.Read(content);
        Assert.Equal("BBBBBBBBBB22222222", item.Parent);
    }

    [Fact]
    public void Read_legacy_frontmatter_arrays_silently_dropped()
    {
        // Legacy frontmatter is tolerated on read (no throw) but produces
        // no rows; next write will emit the new shape.
        var content =
            "---\n" +
            "id: aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa\n" +
            "title: legacy\n" +
            "type: task\n" +
            "state: todo\n" +
            "parent: bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb\n" +
            "blockers:\n" +
            "  - item:cccccccc-cccc-7ccc-8ccc-cccccccccccc\n" +
            "---\n";
        var item = File.Read(content);
        Assert.Empty(item.LinkRows);
        Assert.Equal("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb", item.Parent);
    }

    [Fact]
    public void Write_emits_body_h1_before_links_table()
    {
        // A leading `# h1` in the body must precede the
        // `## Links` table on disk, not the other way round.
        var item = new Item
        {
            Id = "11111111-1111-7111-8111-111111111111",
            Title = "Tinies",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "# Tinies -- pocket-sized scouts\n\nDescription paragraph.\n",
            LinkRows =
            [
                new LinkRow(LinkRel.Parent, "epic", "../22222222-2222-7222-8222-222222222222-x.md", null),
            ],
        };
        var s = File.Write(item);

        var h1Idx = s.IndexOf("# Tinies -- pocket-sized scouts", StringComparison.Ordinal);
        var linksIdx = s.IndexOf("## Links", StringComparison.Ordinal);
        var descIdx = s.IndexOf("Description paragraph.", StringComparison.Ordinal);
        Assert.True(h1Idx > 0);
        Assert.True(linksIdx > 0);
        Assert.True(descIdx > 0);
        Assert.True(h1Idx < linksIdx, "h1 must come before ## Links");
        Assert.True(linksIdx < descIdx, "## Links must come before remaining body");
    }

    [Fact]
    public void Write_without_body_h1_keeps_links_at_top()
    {
        // No leading h1 -> ordering unchanged from the old behavior:
        // ## Links table sits at the top of the body region.
        var item = new Item
        {
            Id = "11111111-1111-7111-8111-111111111111",
            Title = "no h1",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "Description paragraph.\n",
            LinkRows =
            [
                new LinkRow(LinkRel.Parent, "epic", "../22222222-2222-7222-8222-222222222222-x.md", null),
            ],
        };
        var s = File.Write(item);

        // The body region is everything after the closing `---\n\n`.
        var fmEnd = s.IndexOf("---\n\n", 4, StringComparison.Ordinal);
        Assert.True(fmEnd > 0);
        var bodyRegion = s[(fmEnd + 5)..];
        Assert.StartsWith("## Links", bodyRegion);
    }

    [Fact]
    public void RoundTrip_preserves_body_h1_position()
    {
        // Read -> Write -> Read must produce the same in-memory item
        // when the body starts with an h1 heading.
        var original = new Item
        {
            Id = "11111111-1111-7111-8111-111111111111",
            Title = "Tinies",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UnixEpoch,
            Body = "# Tinies -- pocket-sized scouts\n\nDescription paragraph.\n",
            LinkRows =
            [
                new LinkRow(LinkRel.Parent, "epic", "../22222222-2222-7222-8222-222222222222-x.md", null),
            ],
        };
        var written = File.Write(original);
        var roundTripped = File.Read(written);

        Assert.Single(roundTripped.LinkRows);
        Assert.Equal(LinkRel.Parent, roundTripped.LinkRows[0].Rel);
        Assert.Contains("# Tinies -- pocket-sized scouts", roundTripped.Body);
        Assert.Contains("Description paragraph.", roundTripped.Body);
        Assert.DoesNotContain("## Links", roundTripped.Body);
    }

    [Fact]
    public void Read_tolerates_legacy_links_before_h1_and_rewrites_on_write()
    {
        // Older files have ## Links above the body h1. Read must still
        // parse correctly; Write rewrites them into the new order.
        var legacy =
            "---\n" +
            "id: 11111111-1111-7111-8111-111111111111\n" +
            "title: Tinies\n" +
            "type: task\n" +
            "state: todo\n" +
            "---\n\n" +
            "## Links\n\n" +
            "| Rel    | Target                                                         | Notes |\n" +
            "|--------|----------------------------------------------------------------|-------|\n" +
            "| Parent | [epic](../22222222-2222-7222-8222-222222222222-x.md)     |       |\n\n" +
            "# Tinies -- pocket-sized scouts\n\n" +
            "Description.\n";

        var item = File.Read(legacy);
        Assert.Single(item.LinkRows);
        Assert.Contains("# Tinies -- pocket-sized scouts", item.Body);

        var rewritten = File.Write(item);
        var h1Idx = rewritten.IndexOf("# Tinies -- pocket-sized scouts", StringComparison.Ordinal);
        var linksIdx = rewritten.IndexOf("## Links", StringComparison.Ordinal);
        Assert.True(h1Idx > 0 && linksIdx > 0 && h1Idx < linksIdx);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownFields()
    {
        const string content = """
            ---
            id: 01968a73-3aef-7c2c-9efc-aa1a2c79c5e7
            title: Item with extras
            type: task
            state: todo
            custom_field: weird value
            ---
            body
            """;
        var item = File.Read(content);
        Assert.True(item.ExtraFrontmatter.ContainsKey("custom_field"));
        Assert.Equal("weird value", item.ExtraFrontmatter["custom_field"]?.ToString());

        var written = File.Write(item);
        Assert.Contains("custom_field", written);
    }

    [Fact]
    public void Null_and_empty_list_write_the_same_bare_key_and_survive_a_rewrite()
    {
        var yaml = new SimpleFrontmatterSerializer();
        var fields = new Dictionary<string, object?> { ["supersedes"] = new List<object?>(), ["superseded_by"] = null };

        var first = yaml.Write(fields);
        Assert.Equal("supersedes:\nsuperseded_by:\n", first);
        Assert.Equal(first, yaml.Write(yaml.Parse(first)));
    }
}
