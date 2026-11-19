using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// Coarsest item type shown on the timeline. Hidden descendants roll up
/// into the nearest visible ancestor.
/// </summary>
public enum TimelineDepth
{
    Project,
    Epic,    // projects + epics
    Feature, // + features + bugs
    All,     // + tasks + todos
}

/// <summary>
/// One visible timeline entry: the item, its risk read, the rolled-up due
/// span across itself plus its hidden descendants, and whether any of those
/// hidden descendants is at-risk or endangered (the coarse view must stay
/// honest about hidden risk — drilling down reveals which child).
/// </summary>
public sealed record TimelineNode(
    Item Item,
    DeadlineRisk.ItemRisk Risk,
    DateOnly? SpanStart,
    DateOnly? SpanEnd,
    bool SubtreeAtRisk,
    IReadOnlyList<string> RolledUpIds);

/// <summary>
/// Depth-by-type rollup for the deadline timeline. Visibility: an item shows when its type is within the depth. A
/// hidden item rolls into its nearest visible ancestor — not the shallowest
/// — so no due is counted twice and raising the depth redistributes, never
/// duplicates. When the chain has no visible ancestor, the item rolls into
/// its **chain root** (topmost resolvable ancestor) instead — a coarse view
/// shows the coarsest available bar per tree, not scattered leaves.
/// Only a **parentless** item (its own root) surfaces at its own level, so
/// nothing vanishes (its dates ride its root's span otherwise).
///
/// A drawn container brackets its subtree: its span reaches back to the
/// earliest date under it, so it reads as a summary over its children rather
/// than a lone marker beside them. It only *ends* at a descendant's
/// date when it has no due of its own — otherwise the container's own due
/// stays its deadline, which is what the marker and the overdue read use.
/// </summary>
public static class TimelineRollup
{
    public static IReadOnlyList<TimelineNode> Compute(
        LinkGraph graph,
        IReadOnlyDictionary<string, DeadlineRisk.ItemRisk> risk,
        TimelineDepth depth = TimelineDepth.All,
        bool includeDone = false)
    {
        bool Included(Item i) =>
            includeDone || (i.State != ItemState.Done && i.State != ItemState.Dismissed);

        bool VisibleByType(Item i) => Included(i) && WithinDepth(i.Type, depth);

        // The anchor an out-of-depth item rolls into: the nearest visible
        // ancestor if one exists, else the topmost *included* ancestor on a
        // cleanly-terminating chain (the chain root). Returns null — item
        // drawn at its own level — when it is parentless, when the chain roots
        // at an excluded (done/dismissed) item, or when the chain cycles.
        // Every non-null anchor is itself a drawn node, so nothing vanishes.
        Item? Anchor(Item i)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { i.Id };
            var current = i;
            Item? root = null;
            while (!string.IsNullOrEmpty(current.Parent))
            {
                if (!seen.Add(current.Parent)) return null; // cycle: no real root
                var parent = graph.Get(current.Parent);
                if (parent is null) break; // broken link: `root` (if any) is the top
                if (VisibleByType(parent)) return parent; // nearest visible ancestor wins
                if (Included(parent)) root = parent; // highest included ancestor so far
                current = parent;
            }
            return root;
        }

        var visible = new List<Item>();
        var rolledUp = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        foreach (var item in graph.Items)
        {
            if (!Included(item)) continue;
            var anchor = VisibleByType(item) ? null : Anchor(item);
            if (anchor is null)
            {
                visible.Add(item);
                continue;
            }
            if (!rolledUp.TryGetValue(anchor.Id, out var list))
                rolledUp[anchor.Id] = list = [];
            list.Add(item);
        }

        var nodes = new List<TimelineNode>(visible.Count);
        foreach (var item in visible)
        {
            var rolled = rolledUp.TryGetValue(item.Id, out var list) ? list : [];
            rolled.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            DateOnly? start = item.Due, end = item.Due;
            var subtreeAtRisk = false;
            foreach (var r in rolled)
            {
                if (r.Due is { } due)
                {
                    if (start is null || due < start) start = due;
                    if (end is null || due > end) end = due;
                }
                var rRisk = risk.GetValueOrDefault(r.Id);
                subtreeAtRisk |= rRisk is not null && (rRisk.AtRisk || rRisk.Endangered);
            }

            // Summary span: a container brackets its children even when
            // they are drawn at their own level rather than folded into it (an
            // epic at Feature depth, or anything at depth All). Without this a
            // dated epic collapsed to a bare point marker while an *undated*
            // one got the bracket — giving the epic a due date visually
            // detached it from its own children.
            //
            // The end is only borrowed when the item has no due of its own:
            // the marker sits at `SpanEnd` and its due status is read from it,
            // so a child dated later must not become the container's deadline.
            // A folded descendant does move it (above) — there the container is
            // the only thing drawn for that child, so it has to carry its date.
            foreach (var s in graph.Subtree(item.Id))
            {
                if (!Included(s) || s.Due is not { } due) continue;
                if (start is null || due < start) start = due;
                if (item.Due is null && (end is null || due > end)) end = due;
            }
            nodes.Add(new TimelineNode(
                item,
                risk.GetValueOrDefault(item.Id) ?? new DeadlineRisk.ItemRisk(null, false, []),
                start, end, subtreeAtRisk,
                rolled.Select(r => r.Id).ToList()));
        }

        nodes.Sort((a, b) =>
        {
            var byDue = CompareNullsLast(a.SpanStart, b.SpanStart);
            if (byDue != 0) return byDue;
            var byTitle = string.CompareOrdinal(a.Item.Title, b.Item.Title);
            return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Item.Id, b.Item.Id);
        });
        return nodes;
    }

    /// <summary>Whether an item's type is coarse enough to be drawn at this
    /// depth (rank ≤ depth). A node in the result that fails this is a
    /// "disconnected" chain root — too fine for the selected rank; the
    /// hide-disconnected render filter drops these. No-op at
    /// <see cref="TimelineDepth.All"/> (every type is within depth there).</summary>
    public static bool WithinDepth(ItemType type, TimelineDepth depth) =>
        TypeRank(type) <= (int)depth;

    /// <summary>Ancestor titles coarsest-first joined by " ▸ " — the parent
    /// breadcrumb shown on a timeline bar so containment reads without a click
    /// Null for a root (no ancestors). Shared by both back ends.</summary>
    public static string? ParentPath(LinkGraph graph, Item item)
    {
        var chain = ItemContextResolver.ParentChain(graph, item); // nearest-first
        if (chain.Count == 0) return null;
        var titles = new string[chain.Count];
        for (var i = 0; i < chain.Count; i++) titles[chain.Count - 1 - i] = chain[i].Title;
        return string.Join(" ▸ ", titles);
    }

    private static int TypeRank(ItemType type) => type switch
    {
        ItemType.Project => 0,
        ItemType.Epic => 1,
        ItemType.Feature or ItemType.Bug => 2,
        _ => 3, // task, todo
    };

    private static int CompareNullsLast(DateOnly? a, DateOnly? b) =>
        a is null ? (b is null ? 0 : 1) : b is null ? -1 : a.Value.CompareTo(b.Value);
}
