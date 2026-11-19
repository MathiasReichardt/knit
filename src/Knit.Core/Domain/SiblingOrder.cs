namespace Knit.Core.Domain;

/// <summary>How the children of one parent are ordered in a tree view.</summary>
public enum TreeSort
{
    Rank,
    State,
}

/// <summary>
/// The one sibling comparer for <c>tree</c>, <c>list</c> and the web API. A view
/// order, never a write: rank stays the plan, state only groups it.
/// </summary>
public static class SiblingOrder
{
    public static string ToWire(this TreeSort sort) => sort switch
    {
        TreeSort.Rank => "rank",
        TreeSort.State => "state",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "unknown tree sort"),
    };

    public static bool TryParse(string? raw, out TreeSort sort)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "rank": sort = TreeSort.Rank; return true;
            case "state": sort = TreeSort.State; return true;
            default: sort = TreeSort.Rank; return false;
        }
    }

    /// <summary>
    /// The sibling order for one view: the requested one (a <c>--sort</c> flag,
    /// a <c>treeSort</c> query parameter) when given, else the <c>tree_sort</c>
    /// config value. A bad config value is an error, not a silent fall back to rank.
    /// </summary>
    public static (TreeSort sort, string? error) Resolve(string? requested, string configValue)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return TryParse(requested, out var r)
                ? (r, null)
                : (TreeSort.Rank, $"unknown tree sort: {requested} — expected rank|state");
        return TryParse(configValue, out var c)
            ? (c, null)
            : (TreeSort.Rank, $"config tree_sort is '{configValue}' — expected rank|state; fix it with `knit config set tree_sort rank`");
    }

    /// <summary>
    /// Finished work on top, then open work closest-to-done first; dismissed
    /// last — dropped work is neither progress nor plan.
    /// </summary>
    private static int StateBand(ItemState state) => state switch
    {
        ItemState.Done => 0,
        ItemState.Review => 1,
        ItemState.InProgress => 2,
        ItemState.Todo => 3,
        ItemState.Draft => 4,
        ItemState.Dismissed => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown item state"),
    };

    private static int CompareRank(Item a, Item b) =>
        StringComparer.Ordinal.Compare(a.Rank ?? string.Empty, b.Rank ?? string.Empty);

    /// <summary>
    /// Band order, rank ascending within a band. <paramref name="descending"/>
    /// flips the open bands to the lifecycle (draft → … → done) but keeps
    /// dismissed last and rank ascending — the planned order inside a band
    /// never reverses.
    /// </summary>
    public static int CompareState(Item a, Item b, bool descending = false)
    {
        var c = descending
            ? ((int)a.State).CompareTo((int)b.State)
            : StateBand(a.State).CompareTo(StateBand(b.State));
        return c != 0 ? c : CompareRank(a, b);
    }

    public static IComparer<Item> Comparer(TreeSort sort) => sort switch
    {
        TreeSort.State => ByState,
        _ => ByRank,
    };

    private static readonly IComparer<Item> ByRank = Comparer<Item>.Create(CompareRank);
    private static readonly IComparer<Item> ByState = Comparer<Item>.Create((a, b) => CompareState(a, b));
}
