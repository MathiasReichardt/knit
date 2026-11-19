using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// At-risk propagation over the blocker DAG.
/// A blocker's <b>implied deadline</b> is the min over its downstream dues
/// only — its own <c>due</c> is excluded — min-propagated transitively. A
/// blocker is <b>at-risk</b> when it is open and either undated or due
/// strictly after that implied deadline (same-day is in time). No dated
/// downstream ⇒ no implied deadline ⇒ never at-risk. Done/dismissed items
/// exit the graph entirely: never at-risk, never endangered, and they
/// neither contribute nor relay deadlines. Cycle-guarded (a re-entrant walk
/// contributes nothing). Always compute over the FULL graph — filters
/// (slate/tag/assignee/…) restrict what is rendered, not the propagation.
/// <para>
/// A dated <b>milestone target</b> is an implied deadline on the
/// linked item <i>itself</i> — the roadmap analogue of a downstream due, so
/// an undated item linked to a dated milestone is at-risk, and the target
/// min-propagates to the item's blockers like any other downstream deadline.
/// Pass the item id → target map from <c>MilestoneResolver.MilestoneDeadlines</c>.
/// </para>
/// </summary>
public static class DeadlineRisk
{
    /// <summary>
    /// Per-item risk read. <see cref="AtRiskBlockers"/> is the transitive
    /// set of open at-risk blocker ids — non-empty means the item's own
    /// deadline is endangered, and the ids let a filtered renderer point at
    /// an out-of-filter culprit (ghost node / blocked-by-hidden hint).
    /// </summary>
    public sealed record ItemRisk(
        DateOnly? ImpliedDeadline,
        bool AtRisk,
        IReadOnlyList<string> AtRiskBlockers)
    {
        public bool Endangered => AtRiskBlockers.Count > 0;
    }

    public static IReadOnlyDictionary<string, ItemRisk> Compute(
        LinkGraph graph,
        IReadOnlyDictionary<string, DateOnly>? milestoneDeadlines = null)
    {
        var implied = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);

        DateOnly? Implied(Item b)
        {
            if (implied.TryGetValue(b.Id, out var cached)) return cached;
            implied[b.Id] = null; // cycle guard
            // The item's own dated-milestone target is an implied deadline.
            DateOnly? min = milestoneDeadlines is not null
                && milestoneDeadlines.TryGetValue(b.Id, out var mt) ? mt : null;
            foreach (var targetId in graph.BlockedTargetsOf(b.Id))
            {
                var x = graph.Get(targetId);
                if (x is null || !IsOpen(x)) continue;
                min = Min(min, Min(x.Due, Implied(x)));
            }
            implied[b.Id] = min;
            return min;
        }

        var atRisk = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in graph.Items)
        {
            var deadline = Implied(item);
            atRisk[item.Id] = IsOpen(item) && deadline is { } d
                && (item.Due is null || item.Due.Value > d);
        }

        var blockersMemo = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        IReadOnlyList<string> AtRiskBlockers(Item x)
        {
            if (blockersMemo.TryGetValue(x.Id, out var cached)) return cached;
            blockersMemo[x.Id] = []; // cycle guard
            if (!IsOpen(x)) return [];
            var ids = new List<string>();
            foreach (var blockerId in graph.BlockersOf(x.Id))
            {
                var b = graph.Get(blockerId);
                if (b is null || !IsOpen(b)) continue;
                if (atRisk[b.Id] && !ids.Contains(blockerId)) ids.Add(blockerId);
                foreach (var t in AtRiskBlockers(b))
                    if (!ids.Contains(t)) ids.Add(t);
            }
            blockersMemo[x.Id] = ids;
            return ids;
        }

        var result = new Dictionary<string, ItemRisk>(StringComparer.Ordinal);
        foreach (var item in graph.Items)
            result[item.Id] = new ItemRisk(implied[item.Id], atRisk[item.Id], AtRiskBlockers(item));
        return result;
    }

    private static bool IsOpen(Item i) =>
        i.State != ItemState.Done && i.State != ItemState.Dismissed;

    private static DateOnly? Min(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
