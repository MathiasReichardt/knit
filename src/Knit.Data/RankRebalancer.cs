using Knit.Core.Domain;
using Knit.Core.Ids;

namespace Knit.Data;

/// <summary>
/// Respaces the fractional ranks of one sibling group — the remedy for
/// a group whose first row has a rank nothing can sort before.
///
/// <para>Ranks are per-sibling-group everywhere in Knit, so this is
/// deliberately scoped to one parent's children: the blocked condition is
/// always local to one group, and a repo-wide rewrite would touch every item
/// file for no reason. Shared by <c>knit rebalance</c> and
/// <c>POST /api/doctor/rebalance</c> so the two cannot disagree about which
/// groups need repair.</para>
/// </summary>
public static class RankRebalancer
{
    /// <summary>
    /// One sibling group in its current display order: rankless children sort
    /// last, matching where a move would place them.
    /// </summary>
    /// <param name="ParentId">Null for the root group.</param>
    /// <param name="ClosedGap">
    /// The first slot in this group that no rank can express, or null when
    /// every slot is reachable. <c>Before</c> is null for the slot above the
    /// first child. Only older ranks can close a gap.
    /// </param>
    public sealed record Group(
        string? ParentId, IReadOnlyList<Item> Children, (string? Before, string After)? ClosedGap)
    {
        public bool Blocked => ClosedGap is not null;
    }

    /// <summary>Every sibling group in the repo, keyed by parent.</summary>
    public static IReadOnlyList<Group> Groups(ItemStore store) => Build(store.AllItems());

    /// <summary>Only the groups that cannot accept an insert at the top.</summary>
    public static IReadOnlyList<Group> BlockedGroups(ItemStore store) =>
        Build(store.AllItems()).Where(g => g.Blocked).ToList();

    /// <summary>
    /// The group holding <paramref name="parentId"/>'s children (null = root),
    /// or null when that parent has none.
    /// </summary>
    public static Group? GroupOf(ItemStore store, string? parentId)
    {
        var key = Key(parentId);
        return Build(store.AllItems()).FirstOrDefault(g => Key(g.ParentId) == key);
    }

    private static IReadOnlyList<Group> Build(IEnumerable<Item> items) =>
        items
            .GroupBy(i => Key(i.Parent))
            .Select(g =>
            {
                var children = g
                    // A rankless item has no place in the order yet; it goes
                    // to the end, which is also where a move would put it.
                    .OrderBy(i => Rank.IsValid(i.Rank) ? 0 : 1)
                    .ThenBy(i => i.Rank ?? "", StringComparer.Ordinal)
                    .ThenBy(i => i.Id, StringComparer.Ordinal)
                    .ToList();
                var parentId = string.IsNullOrEmpty(g.Key) ? null : g.Key;
                return new Group(parentId, children, FindClosedGap(children));
            })
            .OrderBy(g => Key(g.ParentId), StringComparer.Ordinal)
            .ToList();

    private static string Key(string? parentId) => parentId ?? "";

    /// <summary>
    /// The first slot in the group that no rank can express — scanning the
    /// boundary above the first child as well as every gap between
    /// neighbours. Rankless children sort last and have no gap of their own
    /// yet, so they are skipped.
    /// </summary>
    private static (string? Before, string After)? FindClosedGap(IReadOnlyList<Item> children)
    {
        var ranked = children.Where(c => Rank.IsValid(c.Rank)).Select(c => c.Rank!).ToList();
        if (ranked.Count == 0) return null;
        if (Rank.IsGapClosed("", ranked[0])) return (null, ranked[0]);
        for (var i = 0; i + 1 < ranked.Count; i++)
            if (Rank.IsGapClosed(ranked[i], ranked[i + 1])) return (ranked[i], ranked[i + 1]);
        return null;
    }

    /// <summary>
    /// The ranks <paramref name="group"/> would get, paired with each child —
    /// the preview behind <c>--dry-run</c>. Entries whose rank is unchanged are
    /// included; <see cref="Rebalance"/> is what skips them.
    /// </summary>
    public static IReadOnlyList<(Item Item, string Rank)> Plan(Group group)
    {
        var ranks = Rank.Spread(group.Children.Count);
        return group.Children.Select((c, i) => (c, ranks[i])).ToList();
    }

    /// <summary>
    /// Write the respaced ranks, touching only the files whose rank actually
    /// changes. Returns the number of items written.
    /// </summary>
    public static int Rebalance(ItemStore store, Group group)
    {
        var written = 0;
        foreach (var (item, rank) in Plan(group))
        {
            if (string.Equals(item.Rank, rank, StringComparison.Ordinal)) continue;
            store.Write(item with { Rank = rank });
            written++;
        }
        return written;
    }
}
