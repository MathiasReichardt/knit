using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// One row of a selected item's remaining work: a direct child, or an
/// open blocker of the selection. <see cref="OpenBelow"/> / <see cref="UndatedBelow"/>
/// count the open work in the row's own subtree (excluding the row itself) —
/// the count is what carries the depth, so the list never needs to indent.
/// </summary>
public sealed record RemainingRow(
    Item Item,
    int OpenBelow,
    int UndatedBelow,
    bool Blocker);

/// <summary>
/// The answer to "what has to happen for this date to hold": the rows plus the
/// total open count beneath the selection, which the section heading carries so
/// completed work is counted without being listed.
/// </summary>
public sealed record RemainingWork(IReadOnlyList<RemainingRow> Rows, int OpenBelow);

/// <summary>
/// Remaining work under a selected timeline item. Deliberately
/// independent of the timeline's depth, window and include-done controls: the
/// selection panel answers a structural question, and a view setting that
/// changed the answer is the defect this replaces — an undated feature was
/// listed under its epic at <c>Epic</c> depth and mentioned nowhere at
/// <c>Feature</c> depth.
/// </summary>
public static class TimelineRemaining
{
    public static RemainingWork Compute(LinkGraph graph, Item item)
    {
        var rows = new List<RemainingRow>();
        var blockers = new HashSet<string>(graph.BlockersOf(item.Id), StringComparer.Ordinal);

        foreach (var childId in graph.ChildrenOf(item.Id))
        {
            var child = graph.Get(childId);
            if (child is null) continue;
            var (open, undated) = CountBelow(graph, child);
            // A closed child with open descendants stays listed (rendered dim):
            // dropping it would hide a finished feature's unfinished tasks.
            if (!IsOpen(child) && open == 0) continue;
            rows.Add(new RemainingRow(child, open, undated, blockers.Contains(childId)));
        }

        var listed = new HashSet<string>(rows.Select(r => r.Item.Id), StringComparer.Ordinal);
        foreach (var blockerId in blockers)
        {
            if (listed.Contains(blockerId)) continue;
            var blocker = graph.Get(blockerId);
            if (blocker is null || !IsOpen(blocker)) continue;
            var (open, undated) = CountBelow(graph, blocker);
            rows.Add(new RemainingRow(blocker, open, undated, Blocker: true));
        }

        rows.Sort((a, b) => Compare(a, b, item.Due));

        var totalOpen = 0;
        foreach (var s in graph.Subtree(item.Id))
            if (!string.Equals(s.Id, item.Id, StringComparison.Ordinal) && IsOpen(s))
                totalOpen++;

        return new RemainingWork(rows, totalOpen);
    }

    private static bool IsOpen(Item i) => i.State != ItemState.Done && i.State != ItemState.Dismissed;

    private static (int Open, int Undated) CountBelow(LinkGraph graph, Item row)
    {
        int open = 0, undated = 0;
        foreach (var s in graph.Subtree(row.Id))
        {
            if (string.Equals(s.Id, row.Id, StringComparison.Ordinal) || !IsOpen(s)) continue;
            open++;
            if (s.Due is null) undated++;
        }
        return (open, undated);
    }

    // Ordered by what threatens the date: undated first (they cannot be planned
    // against at all), then work due after the selection's own date, then the
    // rest. Urgency ordering *is* the content here, so it is not a preference.
    private static int Compare(RemainingRow a, RemainingRow b, DateOnly? anchor)
    {
        var byThreat = Threat(a.Item, anchor).CompareTo(Threat(b.Item, anchor));
        if (byThreat != 0) return byThreat;
        if (a.Item.Due is { } ad && b.Item.Due is { } bd)
        {
            var byDue = ad.CompareTo(bd);
            if (byDue != 0) return byDue;
        }
        var byTitle = string.CompareOrdinal(a.Item.Title, b.Item.Title);
        return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Item.Id, b.Item.Id);
    }

    private static int Threat(Item i, DateOnly? anchor) =>
        i.Due is not { } due ? 0
        : anchor is { } a && due > a ? 1
        : 2;
}
