using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Core.Tests;

public class ItemFrontmatterExtTests
{
    private static readonly ItemFile File = new(new SimpleFrontmatterSerializer());

    [Fact]
    public void Summary_roundtrips()
    {
        var item = MakeItem() with { Summary = "one-line summary" };
        var s = File.Write(item);
        Assert.Contains("summary: one-line summary", s);
        var back = File.Read(s);
        Assert.Equal("one-line summary", back.Summary);
    }

    [Fact]
    public void Summary_missing_is_null()
    {
        var item = File.Read("---\nid: x\ntitle: t\ntype: task\nstate: todo\n---\n");
        Assert.Null(item.Summary);
    }

    [Fact]
    public void KbRefs_rows_roundtrip_in_links_table()
    {
        var item = MakeItem() with
        {
            LinkRows =
            [
                new LinkRow(LinkRel.KbRef, "feature:user-mgmt", "../../kb/features/user-mgmt.md", null),
                new LinkRow(LinkRel.KbRef, "adr:0007", "../../kb/adrs/0007.md", "settled the storage choice"),
            ],
        };
        var s = File.Write(item);
        Assert.Contains("## Links", s);
        Assert.Contains("../../kb/features/user-mgmt.md", s);
        Assert.Contains("settled the storage choice", s);
        var back = File.Read(s);
        Assert.Equal(2, back.LinkRows.Count);
        Assert.All(back.LinkRows, r => Assert.Equal(LinkRel.KbRef, r.Rel));
        Assert.Equal("settled the storage choice", back.LinkRows[1].Notes);
    }

    [Fact]
    public void AgentSkills_roundtrip()
    {
        var item = MakeItem() with { AgentSkills = ["csharp-style", "result-types"] };
        var back = File.Read(File.Write(item));
        Assert.Equal(2, back.AgentSkills.Count);
        Assert.Equal("csharp-style", back.AgentSkills[0]);
    }

    [Fact]
    public void Design_roundtrip()
    {
        var item = MakeItem() with { Design = "user-mgmt" };
        var s = File.Write(item);
        Assert.Contains("design: user-mgmt", s);
        var back = File.Read(s);
        Assert.Equal("user-mgmt", back.Design);
    }

    [Fact]
    public void Blocked_string_roundtrip()
    {
        var item = MakeItem() with { BlockedReason = "waiting for vendor" };
        var s = File.Write(item);
        Assert.Contains("blocked: waiting for vendor", s);
        var back = File.Read(s);
        Assert.Equal("waiting for vendor", back.BlockedReason);
    }

    [Fact]
    public void Blocked_false_or_missing_is_null()
    {
        var item = File.Read("---\nid: x\ntitle: t\ntype: task\nstate: todo\nblocked: false\n---\n");
        Assert.Null(item.BlockedReason);
    }

    [Fact]
    public void Blocked_omitted_when_null_on_write()
    {
        var item = MakeItem() with { BlockedReason = null };
        var s = File.Write(item);
        Assert.DoesNotContain("blocked:", s);
    }

    [Fact]
    public void Legacy_state_blocked_throws()
    {
        var ex = Assert.Throws<KnitParseException>(() =>
            File.Read("---\nid: x\ntitle: t\ntype: task\nstate: blocked\n---\n"));
        Assert.Contains("orthogonal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dismissed_state_roundtrip()
    {
        var item = MakeItem() with { State = ItemState.Dismissed };
        var s = File.Write(item);
        Assert.Contains("state: dismissed", s);
        var back = File.Read(s);
        Assert.Equal(ItemState.Dismissed, back.State);
    }

    [Fact]
    public void Feature_list_roundtrip()
    {
        var item = MakeItem() with { Feature = ["user-mgmt", "storage"] };
        var s = File.Write(item);
        Assert.Contains("feature:", s);
        var back = File.Read(s);
        Assert.Equal(2, back.Feature.Count);
    }

    [Fact]
    public void Tags_roundtrip()
    {
        var item = MakeItem() with { Tags = ["api", "security"] };
        var s = File.Write(item);
        Assert.Contains("tags:", s);
        var back = File.Read(s);
        Assert.Equal(["api", "security"], back.Tags);
    }

    [Fact]
    public void Tags_omitted_when_empty_on_write()
    {
        var s = File.Write(MakeItem() with { Tags = [] });
        Assert.DoesNotContain("tags:", s);
    }

    [Fact]
    public void Tags_missing_is_empty()
    {
        var item = File.Read("---\nid: x\ntitle: t\ntype: task\nstate: todo\n---\n");
        Assert.Empty(item.Tags);
    }

    private static Item MakeItem() => new()
    {
        Id = "11111111-1111-7111-8111-111111111111",
        Title = "t",
        Type = ItemType.Task,
        State = ItemState.Todo,
        Created = DateTimeOffset.UnixEpoch,
    };
}
