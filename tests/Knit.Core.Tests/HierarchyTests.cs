using Knit.Core.Domain;

namespace Knit.Core.Tests;

public class HierarchyTests
{
    [Theory]
    // Project is root-only — no sub-projects, never a child.
    [InlineData(ItemType.Project, null, true)]
    [InlineData(ItemType.Project, ItemType.Project, false)]
    [InlineData(ItemType.Project, ItemType.Epic, false)]
    // Everything below project altitude may attach directly under one.
    [InlineData(ItemType.Epic, ItemType.Project, true)]
    [InlineData(ItemType.Feature, ItemType.Project, true)]
    [InlineData(ItemType.Bug, ItemType.Project, true)]
    [InlineData(ItemType.Task, ItemType.Project, true)]
    // Pre-project rules unchanged.
    [InlineData(ItemType.Epic, null, true)]
    [InlineData(ItemType.Feature, ItemType.Epic, true)]
    [InlineData(ItemType.Feature, ItemType.Feature, false)]
    [InlineData(ItemType.Task, ItemType.Task, true)]
    [InlineData(ItemType.Task, null, true)]
    // No loose todos: project/todo parents only, root rejected.
    [InlineData(ItemType.Todo, ItemType.Project, true)]
    [InlineData(ItemType.Todo, ItemType.Todo, true)]
    [InlineData(ItemType.Todo, null, false)]
    [InlineData(ItemType.Todo, ItemType.Epic, false)]
    [InlineData(ItemType.Todo, ItemType.Task, false)]
    // Todos are non-code leaves for code work — nothing else nests under them.
    [InlineData(ItemType.Task, ItemType.Todo, false)]
    [InlineData(ItemType.Epic, ItemType.Todo, false)]
    public void ParentAllowed_enforces_altitude_rules(ItemType child, ItemType? parent, bool expected)
        => Assert.Equal(expected, Hierarchy.ParentAllowed(child, parent));

    /// <summary>
    /// A refusal that only names the pair leaves the caller guessing at the
    /// rule — and the root case used to print the placeholder "(none)".
    /// </summary>
    [Fact]
    public void ExplainRefusal_names_the_rule_not_just_the_pair()
    {
        var msg = Hierarchy.ExplainRefusal(ItemType.Todo, null);
        Assert.Contains("cannot sit at the repo root", msg);
        Assert.Contains("project", msg);
        Assert.Contains("todo", msg);
        Assert.DoesNotContain("(none)", msg);

        var nested = Hierarchy.ExplainRefusal(ItemType.Feature, ItemType.Feature);
        Assert.Contains("cannot be a child of a feature", nested);
        Assert.Contains("epic", nested);
        Assert.Contains("the repo root", nested);
    }

    [Fact]
    public void ExplainRefusal_lists_exactly_the_parents_the_rule_allows()
    {
        var placements =
            from child in Hierarchy.AllTypes
            from parent in Hierarchy.AllTypes.Cast<ItemType?>().Append(null)
            where !Hierarchy.ParentAllowed(child, parent)
            select (child, parent);

        foreach (var (child, parent) in placements)
        {
            var msg = Hierarchy.ExplainRefusal(child, parent);
            foreach (var allowed in Hierarchy.AllowedParents(child))
                Assert.Contains(allowed.ToWire(), msg);
            Assert.DoesNotContain("(none)", msg);
        }
    }
}
