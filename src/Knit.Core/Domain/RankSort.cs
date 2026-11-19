using Knit.Core.Ids;

namespace Knit.Core.Domain;

/// <summary>A rank one item gets so its sibling group follows a sort.</summary>
public sealed record RankMove(Item Item, string Rank);

/// <summary>
/// Bakes a sibling order into rank, one group at a time — the write behind
/// <c>knit reorder --by</c> and its web twin. Minimal: the longest run already
/// in target order keeps its ranks; only the rest is re-ranked.
/// </summary>
public static class RankSort
{
    /// <param name="current">One sibling group in rank order, rankless last.</param>
    /// <param name="target">The order the group should end up in.</param>
    /// <returns>The items whose rank changes, in target order.</returns>
    public static IReadOnlyList<RankMove> Plan(IReadOnlyList<Item> current, IComparer<Item> target)
    {
        if (current.Count < 2) return [];
        var ordered = current.OrderBy(i => i, target).ToList();
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++) position[ordered[i].Id] = i;

        var kept = LongestRun(current, position);
        if (kept.Count == current.Count) return [];

        return Between(ordered, kept) ?? Respace(ordered);
    }

    /// <summary>
    /// The ids of the longest subsequence of <paramref name="current"/> that is
    /// already in target order — those stay put. Rankless items never anchor:
    /// they have no rank to keep.
    /// </summary>
    private static HashSet<string> LongestRun(IReadOnlyList<Item> current, Dictionary<string, int> position)
    {
        var candidates = current.Where(i => Rank.IsValid(i.Rank)).ToList();
        // tails[k] = index into candidates of the smallest tail of a run of length k+1.
        var tails = new List<int>();
        var prev = new int[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (position[candidates[tails[mid]].Id] < position[candidates[i].Id]) lo = mid + 1;
                else hi = mid;
            }
            prev[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i);
            else tails[lo] = i;
        }

        var kept = new HashSet<string>(StringComparer.Ordinal);
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = prev[i]) kept.Add(candidates[i].Id);
        return kept;
    }

    /// <summary>
    /// Each moved run between its kept neighbours; null when a gap has no room —
    /// a closed gap left by an older Knit version, or two kept items sharing one rank.
    /// </summary>
    private static List<RankMove>? Between(List<Item> ordered, HashSet<string> kept)
    {
        var moves = new List<RankMove>();
        var lo = "";
        for (var i = 0; i < ordered.Count; i++)
        {
            if (kept.Contains(ordered[i].Id)) { lo = ordered[i].Rank!; continue; }
            var next = ordered.Skip(i).FirstOrDefault(x => kept.Contains(x.Id));
            var hi = next?.Rank ?? "";
            if (hi.Length > 0 && string.CompareOrdinal(lo, hi) >= 0) return null;
            if (Rank.IsGapClosed(lo, hi)) return null;
            var rank = Rank.Between(lo, hi);
            moves.Add(new RankMove(ordered[i], rank));
            lo = rank;
        }
        return moves;
    }

    // No room between the kept ranks: fall back to the rebalance spacing for
    // the whole group.
    private static List<RankMove> Respace(List<Item> ordered)
    {
        var ranks = Rank.Spread(ordered.Count);
        return ordered
            .Select((item, i) => new RankMove(item, ranks[i]))
            .Where(m => !string.Equals(m.Item.Rank, m.Rank, StringComparison.Ordinal))
            .ToList();
    }
}
