using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// Scope + write for <see cref="RankSort"/>: which sibling groups a
/// <c>reorder --by</c> touches, shared by the CLI and the web API.
/// </summary>
public static class RankSortApplier
{
    /// <summary>The planned moves of one sibling group of <c>Count</c> items.</summary>
    public sealed record GroupPlan(int Count, IReadOnlyList<RankMove> Moves);

    public sealed record Result(IReadOnlyList<GroupPlan> Groups)
    {
        public int Total => Groups.Sum(g => g.Count);
        public int Moved => Groups.Sum(g => g.Moves.Count);
        public int GroupsMoved => Groups.Count(g => g.Moves.Count > 0);
        public IEnumerable<RankMove> Moves => Groups.SelectMany(g => g.Moves);
    }

    /// <summary>
    /// Plan the moves without writing. <paramref name="under"/> null means the
    /// whole repo; otherwise every group below it, or only its own children
    /// when <paramref name="shallow"/>.
    /// </summary>
    public static Result Plan(ItemStore store, string? under, bool shallow, IComparer<Item> target)
    {
        var groups = RankRebalancer.Groups(store);
        if (under is not null)
        {
            HashSet<string> parents = shallow ? [under] : Subtree(groups, under);
            groups = groups.Where(g => g.ParentId is not null && parents.Contains(g.ParentId)).ToList();
        }
        return new Result(groups
            .Select(g => new GroupPlan(g.Children.Count, RankSort.Plan(g.Children, target)))
            .ToList());
    }

    public static void Write(ItemStore store, Result plan)
    {
        foreach (var move in plan.Moves)
            store.Write(move.Item with { Rank = move.Rank });
    }

    private static HashSet<string> Subtree(IReadOnlyList<RankRebalancer.Group> groups, string root)
    {
        var byParent = groups.Where(g => g.ParentId is not null).ToDictionary(g => g.ParentId!, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { root };
        var queue = new Queue<string>([root]);
        while (queue.TryDequeue(out var id))
        {
            if (!byParent.TryGetValue(id, out var g)) continue;
            foreach (var child in g.Children)
                if (seen.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return seen;
    }
}
