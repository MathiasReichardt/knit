using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;

namespace Knit.Core.Tests;

public class DeadlineRiskTests
{
    // Blocks rows only resolve to item ids when the target folder name
    // starts with a valid 18-char NanoID — pad short tags up.
    private static string Id(string tag) => tag.ToUpperInvariant().PadRight(18, '0');

    private static DateOnly D(int day) => new(2026, 8, day);

    private static Item Mk(
        string tag,
        ItemType type = ItemType.Task,
        ItemState state = ItemState.Todo,
        DateOnly? due = null,
        string? parentTag = null,
        params string[] blocksTags)
        => new()
        {
            Id = Id(tag),
            Title = tag,
            Type = type,
            State = state,
            Due = due,
            Parent = parentTag is null ? null : Id(parentTag),
            Created = DateTimeOffset.UnixEpoch,
            LinkRows = blocksTags
                .Select(t => new LinkRow(LinkRel.Blocks, t, $"../{Id(t)}-{t}.md", null))
                .ToList(),
        };

    private static IReadOnlyDictionary<string, DeadlineRisk.ItemRisk> Risk(params Item[] items) =>
        DeadlineRisk.Compute(new LinkGraph(items));

    private static IReadOnlyDictionary<string, DeadlineRisk.ItemRisk> RiskWithMilestones(
        (string Tag, DateOnly Target)[] milestones, params Item[] items) =>
        DeadlineRisk.Compute(
            new LinkGraph(items),
            milestones.ToDictionary(m => Id(m.Tag), m => m.Target, StringComparer.Ordinal));

    [Fact]
    public void Undated_blocker_of_dated_item_is_at_risk()
    {
        var risk = Risk(
            Mk("blocker", blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.True(risk[Id("blocker")].AtRisk);
        Assert.Equal(D(10), risk[Id("blocker")].ImpliedDeadline);
        Assert.False(risk[Id("target")].AtRisk);
        Assert.Equal([Id("blocker")], risk[Id("target")].AtRiskBlockers);
    }

    [Fact]
    public void Blocker_finishing_in_time_is_not_at_risk()
    {
        var risk = Risk(
            Mk("blocker", due: D(5), blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.False(risk[Id("blocker")].AtRisk);
        Assert.False(risk[Id("target")].Endangered);
    }

    [Fact]
    public void Late_blocker_is_at_risk()
    {
        var risk = Risk(
            Mk("blocker", due: D(15), blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.True(risk[Id("blocker")].AtRisk);
    }

    [Fact]
    public void Same_day_is_in_time()
    {
        var risk = Risk(
            Mk("blocker", due: D(10), blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.False(risk[Id("blocker")].AtRisk);
    }

    [Fact]
    public void No_dated_downstream_means_never_at_risk()
    {
        // Entirely undated chain: min over an empty set = no implied
        // deadline = no flag, even though the blocker itself is undated.
        var risk = Risk(
            Mk("blocker", blocksTags: "target"),
            Mk("target"));
        Assert.False(risk[Id("blocker")].AtRisk);
        Assert.Null(risk[Id("blocker")].ImpliedDeadline);
    }

    [Fact]
    public void Dismissed_blocker_exits_the_graph_like_done()
    {
        var risk = Risk(
            Mk("cancelled", state: ItemState.Dismissed, blocksTags: "target"),
            Mk("done", state: ItemState.Done, blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.False(risk[Id("cancelled")].AtRisk);
        Assert.False(risk[Id("done")].AtRisk);
        Assert.False(risk[Id("target")].Endangered);
    }

    [Fact]
    public void Done_downstream_contributes_no_deadline()
    {
        var risk = Risk(
            Mk("blocker", blocksTags: "shipped"),
            Mk("shipped", state: ItemState.Done, due: D(10)));
        Assert.False(risk[Id("blocker")].AtRisk);
        Assert.Null(risk[Id("blocker")].ImpliedDeadline);
    }

    [Fact]
    public void Implied_deadline_min_propagates_transitively_excluding_own_due()
    {
        // upstream → mid (undated) → target(due 10); upstream due 20 is late.
        // mid's own lack of due doesn't stop propagation, and upstream's own
        // due is excluded from its implied deadline.
        var risk = Risk(
            Mk("upstream", due: D(20), blocksTags: "mid"),
            Mk("mid", blocksTags: "target"),
            Mk("target", due: D(10)));
        Assert.Equal(D(10), risk[Id("mid")].ImpliedDeadline);
        Assert.True(risk[Id("mid")].AtRisk); // undated with a dated downstream
        Assert.Equal(D(10), risk[Id("upstream")].ImpliedDeadline);
        Assert.True(risk[Id("upstream")].AtRisk);
        // Endangered target names both transitive culprits.
        Assert.Equal(
            new[] { Id("mid"), Id("upstream") },
            risk[Id("target")].AtRiskBlockers.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Tightest_downstream_deadline_wins()
    {
        var risk = Risk(
            Mk("blocker", due: D(8), blocksTags: ["loose", "tight"]),
            Mk("loose", due: D(20)),
            Mk("tight", due: D(7)));
        Assert.Equal(D(7), risk[Id("blocker")].ImpliedDeadline);
        Assert.True(risk[Id("blocker")].AtRisk);
    }

    [Fact]
    public void Blocker_cycle_terminates_without_flagging_spuriously()
    {
        var risk = Risk(
            Mk("a", blocksTags: "b"),
            Mk("b", blocksTags: ["a", "target"]),
            Mk("target", due: D(10)));
        Assert.True(risk[Id("b")].AtRisk);
        // a's only downstream is the cycle partner — propagation reaches the
        // dated target through b without hanging.
        Assert.Equal(D(10), risk[Id("a")].ImpliedDeadline);
        Assert.True(risk[Id("a")].AtRisk);
    }

    private static Item MkBlockedBy(string tag, DateOnly? due = null, params string[] blockedByTags)
        => Mk(tag, due: due) with
        {
            LinkRows = blockedByTags
                .Select(t => new LinkRow(LinkRel.BlockedBy, t, $"../{Id(t)}-{t}.md", null))
                .ToList(),
        };

    [Fact]
    public void Blocked_by_row_is_a_live_reverse_edge()
    {
        // The natural blocking flow (`knit block --by`) authors only a
        // Blocked-by row on the blocked item — it must produce the same
        // at-risk propagation as a Blocks row on the blocker.
        var risk = Risk(
            Mk("blocker"),
            MkBlockedBy("target", due: D(10), "blocker"));
        Assert.True(risk[Id("blocker")].AtRisk);
        Assert.Equal(D(10), risk[Id("blocker")].ImpliedDeadline);
        Assert.Equal([Id("blocker")], risk[Id("target")].AtRiskBlockers);
    }

    [Fact]
    public void Blocks_and_blocked_by_for_the_same_pair_dedupe_to_one_edge()
    {
        var graph = new LinkGraph(
        [
            Mk("blocker", blocksTags: "target"),
            MkBlockedBy("target", due: D(10), "blocker"),
        ]);
        Assert.Equal([Id("blocker")], graph.BlockersOf(Id("target")));
        Assert.Equal([Id("target")], graph.BlockedTargetsOf(Id("blocker")));
        var risk = DeadlineRisk.Compute(graph);
        Assert.Equal([Id("blocker")], risk[Id("target")].AtRiskBlockers);
    }

    [Fact]
    public void Filtered_render_still_sees_out_of_filter_blocker()
    {
        // Risk computes on the FULL graph; the endangered item carries the
        // culprit id, so a renderer that filtered the blocker out (slate,
        // tag, …) can still badge the item and hint at the hidden blocker.
        var blocker = Mk("outside", blocksTags: "inside");
        var target = Mk("inside", due: D(10));
        var risk = Risk(blocker, target);
        var filteredRender = new[] { target }; // blocker excluded by a filter
        var rendered = filteredRender.Single();
        Assert.Contains(Id("outside"), risk[rendered.Id].AtRiskBlockers);
    }

    // A dated milestone target is an implied deadline on its linked item.

    [Fact]
    public void Undated_item_linked_to_dated_milestone_is_at_risk()
    {
        // The roadmap analogue of the undated-blocker case: an undated item
        // in a dated milestone raises the same signal.
        var risk = RiskWithMilestones(
            [("task", D(10))],
            Mk("task"));
        Assert.True(risk[Id("task")].AtRisk);
        Assert.Equal(D(10), risk[Id("task")].ImpliedDeadline);
    }

    [Fact]
    public void Item_due_after_its_milestone_target_is_at_risk_before_is_not()
    {
        var late = RiskWithMilestones([("task", D(10))], Mk("task", due: D(15)));
        Assert.True(late[Id("task")].AtRisk);
        var inTime = RiskWithMilestones([("task", D(10))], Mk("task", due: D(5)));
        Assert.False(inTime[Id("task")].AtRisk);
        var sameDay = RiskWithMilestones([("task", D(10))], Mk("task", due: D(10)));
        Assert.False(sameDay[Id("task")].AtRisk); // same-day is in time
    }

    [Fact]
    public void Milestone_target_propagates_to_the_linked_items_blocker()
    {
        // blocker → target(undated, in a milestone due 10). The blocker inherits
        // the milestone target transitively, exactly like a downstream due.
        var risk = RiskWithMilestones(
            [("target", D(10))],
            Mk("blocker", blocksTags: "target"),
            Mk("target"));
        Assert.Equal(D(10), risk[Id("blocker")].ImpliedDeadline);
        Assert.True(risk[Id("blocker")].AtRisk);
        Assert.True(risk[Id("target")].AtRisk);
        Assert.Equal([Id("blocker")], risk[Id("target")].AtRiskBlockers);
    }

    [Fact]
    public void Done_item_linked_to_milestone_is_not_at_risk()
    {
        var risk = RiskWithMilestones(
            [("shipped", D(10))],
            Mk("shipped", state: ItemState.Done));
        Assert.False(risk[Id("shipped")].AtRisk);
    }
}

public class TimelineRollupTests
{
    private static string Id(string tag) => tag.ToUpperInvariant().PadRight(18, '0');
    private static DateOnly D(int day) => new(2026, 8, day);

    private static Item Mk(
        string tag,
        ItemType type = ItemType.Task,
        ItemState state = ItemState.Todo,
        DateOnly? due = null,
        string? parentTag = null,
        params string[] blocksTags)
        => new()
        {
            Id = Id(tag),
            Title = tag,
            Type = type,
            State = state,
            Due = due,
            Parent = parentTag is null ? null : Id(parentTag),
            Created = DateTimeOffset.UnixEpoch,
            LinkRows = blocksTags
                .Select(t => new LinkRow(LinkRel.Blocks, t, $"../{Id(t)}-{t}.md", null))
                .ToList(),
        };

    private static IReadOnlyList<TimelineNode> Roll(
        TimelineDepth depth, bool includeDone = false, params Item[] items)
    {
        var graph = new LinkGraph(items);
        return TimelineRollup.Compute(graph, DeadlineRisk.Compute(graph), depth, includeDone);
    }

    [Fact]
    public void Epic_depth_rolls_descendants_into_due_span()
    {
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("epic", ItemType.Epic, due: D(12)),
                Mk("feat", ItemType.Feature, due: D(20), parentTag: "epic"),
                Mk("task", due: D(5), parentTag: "feat"),
            ]);
        var epic = Assert.Single(nodes);
        Assert.Equal(Id("epic"), epic.Item.Id);
        Assert.Equal(D(5), epic.SpanStart);
        Assert.Equal(D(20), epic.SpanEnd);
        Assert.Equal(2, epic.RolledUpIds.Count);
    }

    [Fact]
    public void Orphan_surfaces_at_its_own_type_level()
    {
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("epic", ItemType.Epic),
                Mk("loose", due: D(5)), // no epic ancestor — must not vanish
            ]);
        Assert.Contains(nodes, n => n.Item.Id == Id("loose"));
    }

    [Fact]
    public void Rolls_to_chain_root_when_no_ancestor_is_within_depth()
    {
        // At a coarse depth an item with no ancestor of that rank rolls
        // into its chain root (the orphan epic) rather than surfacing as a
        // leaf; a parentless item still stays at its own level.
        var nodes = Roll(TimelineDepth.Project,
            items:
            [
                Mk("epic", ItemType.Epic),               // orphan root — no project above it
                Mk("task", due: D(5), parentTag: "epic"),
                Mk("loose", due: D(6)),                  // parentless — stays plain
            ]);
        var epic = Assert.Single(nodes, n => n.Item.Id == Id("epic"));
        Assert.Equal([Id("task")], epic.RolledUpIds);
        Assert.Equal(D(5), epic.SpanStart);
        Assert.DoesNotContain(nodes, n => n.Item.Id == Id("task"));
        Assert.Contains(nodes, n => n.Item.Id == Id("loose"));
    }

    [Fact]
    public void Hidden_item_rolls_to_nearest_visible_ancestor_not_shallowest()
    {
        var nodes = Roll(TimelineDepth.Feature,
            items:
            [
                Mk("epic", ItemType.Epic),
                Mk("feat", ItemType.Feature, parentTag: "epic"),
                Mk("task", due: D(5), parentTag: "feat"),
            ]);
        var feat = nodes.Single(n => n.Item.Id == Id("feat"));
        var epic = nodes.Single(n => n.Item.Id == Id("epic"));
        Assert.Equal([Id("task")], feat.RolledUpIds);
        Assert.Empty(epic.RolledUpIds);
        Assert.Equal(D(5), feat.SpanStart);
        // Summary-span fallback: the epic folds nothing (its feature is
        // drawn at its own level), but still brackets its subtree's dates so it
        // renders as a container bar, not a broken date-less one.
        Assert.Equal(D(5), epic.SpanStart);
        Assert.Equal(D(5), epic.SpanEnd);
    }

    [Fact]
    public void Container_keeps_its_own_due_as_the_span_end()
    {
        // A container with its OWN date keeps it as the end: the marker sits at
        // SpanEnd and the overdue read comes off it, so a later descendant
        // drawn at its own level must not become the epic's deadline.
        var nodes = Roll(TimelineDepth.Feature,
            items:
            [
                Mk("epic", ItemType.Epic, due: D(3)),
                Mk("feat", ItemType.Feature, due: D(8), parentTag: "epic"),
            ]);
        var epic = nodes.Single(n => n.Item.Id == Id("epic"));
        Assert.Empty(epic.RolledUpIds);
        Assert.Equal(D(3), epic.SpanStart);
        Assert.Equal(D(3), epic.SpanEnd);
    }

    [Fact]
    public void Dated_container_still_brackets_an_earlier_child()
    {
        // The bug this fixes: giving the epic a due date used to collapse it to
        // a bare marker while an *undated* epic got the bracket — a due date
        // visually detached a container from its own children.
        var nodes = Roll(TimelineDepth.All,
            items:
            [
                Mk("epic", ItemType.Epic, due: D(20)),
                Mk("feat", ItemType.Feature, due: D(10), parentTag: "epic"),
            ]);
        var epic = nodes.Single(n => n.Item.Id == Id("epic"));
        Assert.Empty(epic.RolledUpIds);
        Assert.Equal(D(10), epic.SpanStart);
        Assert.Equal(D(20), epic.SpanEnd);
    }

    [Fact]
    public void Folded_descendant_still_moves_a_dated_containers_span_end()
    {
        // At Epic depth the feature is folded in, so the epic is the only thing
        // drawn for it and has to carry its date — the end moves here.
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("epic", ItemType.Epic, due: D(20)),
                Mk("feat", ItemType.Feature, due: D(25), parentTag: "epic"),
            ]);
        var epic = nodes.Single(n => n.Item.Id == Id("epic"));
        Assert.Equal([Id("feat")], epic.RolledUpIds);
        Assert.Equal(D(25), epic.SpanEnd);
    }

    [Fact]
    public void Closed_descendant_does_not_widen_the_span()
    {
        var nodes = Roll(TimelineDepth.All,
            items:
            [
                Mk("epic", ItemType.Epic, due: D(20)),
                Mk("feat", ItemType.Feature, ItemState.Done, due: D(5), parentTag: "epic"),
            ]);
        Assert.Equal(D(20), nodes.Single(n => n.Item.Id == Id("epic")).SpanStart);
    }

    [Fact]
    public void Subtree_at_risk_surfaces_hidden_risk()
    {
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("epic", ItemType.Epic),
                Mk("task", parentTag: "epic", blocksTags: "dated"),
                Mk("dated", due: D(10), parentTag: "epic"),
            ]);
        var epic = Assert.Single(nodes, n => n.Item.Id == Id("epic"));
        Assert.True(epic.SubtreeAtRisk); // hidden undated task endangers hidden dated one
    }

    [Fact]
    public void Done_items_are_excluded_unless_included()
    {
        var items = new[]
        {
            Mk("epic", ItemType.Epic, due: D(10)),
            Mk("shipped", state: ItemState.Done, due: D(25), parentTag: "epic"),
        };
        var without = Roll(TimelineDepth.Epic, items: items);
        Assert.Equal(D(10), Assert.Single(without).SpanEnd);
        var with = Roll(TimelineDepth.Epic, includeDone: true, items: items);
        Assert.Equal(D(25), Assert.Single(with).SpanEnd);
    }

    [Fact]
    public void Open_child_of_hidden_done_parent_surfaces_itself()
    {
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("epic", ItemType.Epic, state: ItemState.Done),
                Mk("task", due: D(5), parentTag: "epic"),
            ]);
        var task = Assert.Single(nodes);
        Assert.Equal(Id("task"), task.Item.Id);
    }

    [Fact]
    public void Parent_cycle_terminates()
    {
        var nodes = Roll(TimelineDepth.Epic,
            items:
            [
                Mk("a", parentTag: "b"),
                Mk("b", parentTag: "a"),
            ]);
        Assert.Equal(2, nodes.Count); // neither has a visible ancestor
    }

    [Fact]
    public void Nodes_order_by_span_start_nulls_last()
    {
        var nodes = Roll(TimelineDepth.All,
            items:
            [
                Mk("undated"),
                Mk("late", due: D(20)),
                Mk("early", due: D(5)),
            ]);
        Assert.Equal(
            [Id("early"), Id("late"), Id("undated")],
            nodes.Select(n => n.Item.Id).ToArray());
    }
}

public class TimelineRemainingTests
{
    private static string Id(string tag) => tag.ToUpperInvariant().PadRight(18, '0');
    private static DateOnly D(int day) => new(2026, 8, day);

    private static Item Mk(
        string tag,
        ItemType type = ItemType.Task,
        ItemState state = ItemState.Todo,
        DateOnly? due = null,
        string? parentTag = null,
        params string[] blockedByTags)
        => new()
        {
            Id = Id(tag),
            Title = tag,
            Type = type,
            State = state,
            Due = due,
            Parent = parentTag is null ? null : Id(parentTag),
            Created = DateTimeOffset.UnixEpoch,
            LinkRows = blockedByTags
                .Select(t => new LinkRow(LinkRel.BlockedBy, t, $"../{Id(t)}-{t}.md", null))
                .ToList(),
        };

    private static RemainingWork Work(string ofTag, params Item[] items)
    {
        var graph = new LinkGraph(items);
        return TimelineRemaining.Compute(graph, graph.Get(Id(ofTag))!);
    }

    [Fact]
    public void Lists_direct_children_only_and_counts_the_rest()
    {
        var work = Work("epic",
            Mk("epic", ItemType.Epic, due: D(20)),
            Mk("feat", ItemType.Feature, parentTag: "epic"),
            Mk("t1", parentTag: "feat"),
            Mk("t2", parentTag: "feat"),
            Mk("t3", parentTag: "t2"));

        var row = Assert.Single(work.Rows);
        Assert.Equal(Id("feat"), row.Item.Id);
        Assert.Equal(3, row.OpenBelow);
        Assert.Equal(3, row.UndatedBelow);
        Assert.Equal(4, work.OpenBelow);
    }

    [Fact]
    public void An_undated_child_is_listed_and_counted()
    {
        // The reported hole: an undated feature under a dated epic was listed
        // in the epic's rollup at Epic depth and mentioned nowhere at Feature
        // depth. Remaining is computed off the graph alone — no depth, no
        // window, no include-done — so no view setting can reach it.
        var work = Work("epic",
            Mk("epic", ItemType.Epic, due: D(20)),
            Mk("feat", ItemType.Feature, parentTag: "epic"),
            Mk("t1", parentTag: "feat"),
            Mk("t2", due: D(12), parentTag: "feat"));

        var row = Assert.Single(work.Rows);
        Assert.Null(row.Item.Due);
        Assert.Equal(2, row.OpenBelow);
        Assert.Equal(1, row.UndatedBelow);
    }

    [Fact]
    public void A_closed_child_with_open_descendants_is_kept()
    {
        var work = Work("epic",
            Mk("epic", ItemType.Epic, due: D(20)),
            Mk("shipped", ItemType.Feature, state: ItemState.Done, parentTag: "epic"),
            Mk("leftover", parentTag: "shipped"),
            Mk("finished", ItemType.Feature, state: ItemState.Done, parentTag: "epic"));

        var row = Assert.Single(work.Rows);
        Assert.Equal(Id("shipped"), row.Item.Id);
        Assert.Equal(1, row.OpenBelow);
    }

    [Fact]
    public void Closed_descendants_do_not_count_as_remaining()
    {
        var work = Work("feat",
            Mk("feat", ItemType.Feature, due: D(20)),
            Mk("open", parentTag: "feat"),
            Mk("done", state: ItemState.Done, parentTag: "feat"),
            Mk("dropped", state: ItemState.Dismissed, parentTag: "feat"));

        Assert.Equal(1, work.OpenBelow);
        Assert.Equal([Id("open")], work.Rows.Select(r => r.Item.Id).ToArray());
    }

    [Fact]
    public void Ordered_by_what_threatens_the_date()
    {
        var work = Work("epic",
            Mk("epic", ItemType.Epic, due: D(20)),
            Mk("before", ItemType.Feature, due: D(10), parentTag: "epic"),
            Mk("after", ItemType.Feature, due: D(25), parentTag: "epic"),
            Mk("undated", ItemType.Feature, parentTag: "epic"));

        Assert.Equal(
            [Id("undated"), Id("after"), Id("before")],
            work.Rows.Select(r => r.Item.Id).ToArray());
    }

    [Fact]
    public void Open_blockers_join_the_same_list()
    {
        var work = Work("feat",
            Mk("feat", ItemType.Feature, due: D(20), blockedByTags: "waiting"),
            Mk("waiting", due: D(5)),
            Mk("child", parentTag: "feat"));

        Assert.Equal([Id("child"), Id("waiting")], work.Rows.Select(r => r.Item.Id).ToArray());
        Assert.True(work.Rows.Single(r => r.Item.Id == Id("waiting")).Blocker);
        // A blocker is not under the selection, so it never reaches the count.
        Assert.Equal(1, work.OpenBelow);
    }

    [Fact]
    public void A_child_that_also_blocks_is_listed_once()
    {
        var work = Work("feat",
            Mk("feat", ItemType.Feature, due: D(20), blockedByTags: "child"),
            Mk("child", parentTag: "feat"));

        var row = Assert.Single(work.Rows);
        Assert.True(row.Blocker);
    }

    [Fact]
    public void A_leaf_has_nothing_remaining()
    {
        Assert.Empty(Work("solo", Mk("solo", due: D(20))).Rows);
    }
}
