using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Core.Tests;

public class LinkGraphBlockTests
{
    // Link rows only resolve to item ids when the target folder name starts
    // with a valid 18-char NanoID — pad short tags up.
    private static string Id(string tag) => tag.ToUpperInvariant().PadRight(18, '0');

    private static LinkRow Row(LinkRel rel, string tag) => new(rel, tag, $"../{Id(tag)}-{tag}.md", null);

    private static Item Mk(
        string tag,
        ItemState state = ItemState.Todo,
        string? reason = null,
        string? parentTag = null,
        params LinkRow[] rows)
        => new()
        {
            Id = Id(tag),
            Title = tag,
            Type = ItemType.Task,
            State = state,
            BlockedReason = reason,
            Parent = parentTag is null ? null : Id(parentTag),
            Created = DateTimeOffset.UnixEpoch,
            LinkRows = rows,
        };

    [Fact]
    public void Dependents_are_open_items_with_a_blocked_by_row()
    {
        var graph = new LinkGraph([
            Mk("blocker", ItemState.Done),
            Mk("sole", rows: Row(LinkRel.BlockedBy, "blocker")),
            Mk("other", reason: "vendor", rows: [Row(LinkRel.BlockedBy, "blocker"), Row(LinkRel.BlockedBy, "next")]),
            Mk("next"),
            Mk("closed", ItemState.Dismissed, rows: Row(LinkRel.BlockedBy, "blocker")),
        ]);

        var deps = graph.BlockedDependentsOf(Id("blocker")).ToDictionary(d => d.Item.Title, d => d.Remaining);

        Assert.Equal(["other", "sole"], deps.Keys.Order());
        Assert.Null(deps["sole"]);
        Assert.Equal("vendor; blocked by next", deps["other"]);
    }

    [Fact]
    public void A_blocks_row_is_not_a_dependent()
    {
        var graph = new LinkGraph([
            Mk("blocker", ItemState.Done, rows: Row(LinkRel.Blocks, "planned")),
            Mk("planned"),
        ]);

        Assert.Empty(graph.BlockedDependentsOf(Id("blocker")));
        Assert.Empty(graph.StaleBlocks());
    }

    [Fact]
    public void Stale_blocks_only_name_closed_blockers()
    {
        var graph = new LinkGraph([
            Mk("done", ItemState.Done),
            Mk("open"),
            Mk("dep", rows: [Row(LinkRel.BlockedBy, "done"), Row(LinkRel.BlockedBy, "open")]),
        ]);

        var (dependent, blocker) = Assert.Single(graph.StaleBlocks());
        Assert.Equal(Id("dep"), dependent.Item.Id);
        Assert.Equal(Id("done"), blocker.Id);
        Assert.Equal("blocked by open", dependent.Remaining);
    }

    [Fact]
    public void The_nearest_open_blocked_ancestor_holds_the_subtree()
    {
        var graph = new LinkGraph([
            Mk("epic", reason: "waiting"),
            Mk("feature", parentTag: "epic", reason: "also waiting"),
            Mk("task", parentTag: "feature"),
        ]);

        Assert.Equal(Id("feature"), graph.HeldBy(Id("task"))!.Id);
        Assert.Equal(Id("epic"), graph.HeldBy(Id("feature"))!.Id);
        Assert.Null(graph.HeldBy(Id("epic")));
        Assert.Empty(graph.Ready());
    }

    [Fact]
    public void Closed_ancestors_and_closed_items_are_not_held()
    {
        var graph = new LinkGraph([
            Mk("feature", ItemState.Done, reason: "stale"),
            Mk("task", parentTag: "feature"),
            Mk("open", reason: "waiting"),
            Mk("closedChild", ItemState.Done, parentTag: "open"),
        ]);

        Assert.Null(graph.HeldBy(Id("task")));
        Assert.Null(graph.HeldBy(Id("closedChild")));
        Assert.Equal([Id("task")], graph.Ready().Select(i => i.Id));
    }

    [Fact]
    public void A_parent_cycle_ends_the_walk()
    {
        var graph = new LinkGraph([Mk("a", parentTag: "b"), Mk("b", parentTag: "a")]);
        Assert.Null(graph.HeldBy(Id("a")));
    }
}
