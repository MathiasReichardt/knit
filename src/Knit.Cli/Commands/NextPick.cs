using Knit.Core.Domain;
using Knit.Data;

namespace Knit.Cli.Commands;

/// <summary>
/// The recommendation rule behind `knit next`. `ready` is the inventory of
/// workable items — containers included; `next` recommends the one thing to
/// start, so it keeps only leaf work and applies an opinionated order.
/// </summary>
public static class NextPick
{
    /// <summary>
    /// Work someone can actually start. A <c>project</c> is a planning layer,
    /// never work. Any other type is a container only while it still has open
    /// children — a childless feature is legitimately workable directly.
    /// </summary>
    public static bool IsLeafWork(LinkGraph graph, Item item)
    {
        if (item.Type == ItemType.Project) return false;
        foreach (var childId in graph.ChildrenOf(item.Id))
        {
            var child = graph.Get(childId);
            if (child is null) continue;
            if (child.State is not (ItemState.Done or ItemState.Dismissed)) return false;
        }
        return true;
    }

    /// <summary>
    /// Best first: in-progress before untouched (finish what you started),
    /// then deadline pressure, then priority, then the earlier due date, then
    /// user-chosen rank. Id breaks the last tie so the pick is deterministic.
    /// </summary>
    public static IEnumerable<Item> Order(IEnumerable<Item> items, DateOnly today, int dueSoonDays) =>
        items
            .OrderBy(i => i.State == ItemState.InProgress ? 0 : 1)
            .ThenBy(i => DueUrgency(i, today, dueSoonDays))
            .ThenBy(i => PriorityUrgency(i.Priority))
            .ThenBy(i => i.Due ?? DateOnly.MaxValue)
            // Unranked sorts last: an item nobody ordered shouldn't outrank one
            // someone did.
            .ThenBy(i => i.Rank ?? "￿", StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal);

    /// <summary>
    /// Why this item won, in the same order the sort weighs it. Only
    /// non-default signals are named; a pick with none of them says so rather
    /// than looking arbitrary.
    /// </summary>
    public static string Reason(Item item, DateOnly today, int dueSoonDays)
    {
        var parts = new List<string>();
        if (item.State == ItemState.InProgress) parts.Add("in progress");

        switch (DueDates.Status(item.Due, item.State, today, dueSoonDays))
        {
            case DueStatus.Overdue:
                var late = today.DayNumber - item.Due!.Value.DayNumber;
                parts.Add(late == 1 ? "overdue by 1 day" : $"overdue by {late} days");
                break;
            case DueStatus.DueSoon:
                var left = item.Due!.Value.DayNumber - today.DayNumber;
                parts.Add(left switch
                {
                    0 => "due today",
                    1 => "due tomorrow",
                    _ => $"due in {left} days",
                });
                break;
        }

        if (item.Priority != Priorities.Default) parts.Add($"{item.Priority.ToWire()} priority");
        if (parts.Count == 0) parts.Add("first in rank order");
        return string.Join(" · ", parts);
    }

    private static int DueUrgency(Item item, DateOnly today, int dueSoonDays) =>
        DueDates.Status(item.Due, item.State, today, dueSoonDays) switch
        {
            DueStatus.Overdue => 0,
            DueStatus.DueSoon => 1,
            _ => 2,
        };

    private static int PriorityUrgency(Priority p) => p switch
    {
        Priority.Critical => 0,
        Priority.Important => 1,
        Priority.Normal => 2,
        _ => 3,
    };
}
