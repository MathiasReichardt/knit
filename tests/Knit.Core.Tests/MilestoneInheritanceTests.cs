using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Core.Tests;

public class MilestoneInheritanceTests
{
    private static Item Mk(string id, string? parent = null, string? milestone = null) => new()
    {
        Id = id,
        Title = id,
        Type = ItemType.Task,
        State = ItemState.Todo,
        Parent = parent,
        Created = DateTimeOffset.UnixEpoch,
        LinkRows = milestone is null
            ? []
            : [new LinkRow(LinkRel.Milestone, milestone, $"../../projects/{milestone.Split('/')[0]}/milestones/{milestone.Split('/')[1]}.md", null)],
    };

    [Fact]
    public void Nearest_ancestor_link_is_inherited_with_its_source()
    {
        var map = MilestoneInheritance.Compute([
            Mk("epic", milestone: "relaunch/beta"),
            Mk("feature", parent: "epic"),
            Mk("task", parent: "feature"),
        ]);

        Assert.Equal("relaunch/beta", map["task"].QualifiedSlug);
        Assert.Equal("epic", map["task"].InheritedFrom);
        Assert.Equal("epic", map["feature"].InheritedFrom);
        Assert.Equal("", map["epic"].InheritedFrom);
    }

    [Fact]
    public void Own_link_overrides_and_re_roots_the_subtree()
    {
        var map = MilestoneInheritance.Compute([
            Mk("epic", milestone: "relaunch/beta"),
            Mk("feature", parent: "epic", milestone: "relaunch/ga"),
            Mk("task", parent: "feature"),
        ]);

        Assert.Equal("relaunch/ga", map["feature"].QualifiedSlug);
        Assert.Equal("relaunch/ga", map["task"].QualifiedSlug);
        Assert.Equal("feature", map["task"].InheritedFrom);
    }

    [Fact]
    public void Unlinked_chain_missing_parent_and_cycle_yield_none()
    {
        var map = MilestoneInheritance.Compute([
            Mk("loose"),
            Mk("orphan", parent: "gone"),
            Mk("a", parent: "b"),
            Mk("b", parent: "a"),
        ]);

        Assert.Empty(map);
    }
}
