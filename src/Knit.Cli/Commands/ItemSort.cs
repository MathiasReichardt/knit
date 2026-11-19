using Knit.Core.Domain;
using Knit.Data;

namespace Knit.Cli.Commands;

internal enum SortKey { Tree, Rank, Type, State, Assignee, Created, Due, Priority }
internal enum SortDir { Asc, Desc }

internal static class ItemSort
{
    public static (SortKey key, SortDir dir, string? error) Parse(string? raw, SortKey defaultKey)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (defaultKey, SortDir.Asc, null);
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        var keyRaw = parts[0].ToLowerInvariant();
        var dir = SortDir.Asc;
        if (parts.Length == 2)
        {
            var dirRaw = parts[1].ToLowerInvariant();
            if (dirRaw == "desc") dir = SortDir.Desc;
            else if (dirRaw == "asc") dir = SortDir.Asc;
            else return (defaultKey, SortDir.Asc, $"unknown sort direction: {parts[1]}");
        }
        SortKey k;
        switch (keyRaw)
        {
            case "tree": k = SortKey.Tree; break;
            case "rank": k = SortKey.Rank; break;
            case "type": k = SortKey.Type; break;
            case "state": k = SortKey.State; break;
            case "assignee": k = SortKey.Assignee; break;
            case "created": k = SortKey.Created; break;
            case "due": k = SortKey.Due; break;
            case "priority": k = SortKey.Priority; break;
            default: return (defaultKey, SortDir.Asc, $"unknown sort key: {parts[0]}");
        }
        return (k, dir, null);
    }

    public static IReadOnlyList<Item> Apply(IEnumerable<Item> items, SortKey key, SortDir dir, LinkGraph graph, TreeSort treeSort)
    {
        var rankCmp = StringComparer.Ordinal;
        var list = items.ToList();
        switch (key)
        {
            case SortKey.Tree:
                var order = TreeOrder.Index(graph, treeSort);
                list.Sort((a, b) =>
                {
                    var ai = order.TryGetValue(a.Id, out var x) ? x : int.MaxValue;
                    var bi = order.TryGetValue(b.Id, out var y) ? y : int.MaxValue;
                    return ai.CompareTo(bi);
                });
                break;
            case SortKey.Rank:
                list.Sort((a, b) => rankCmp.Compare(a.Rank, b.Rank));
                break;
            case SortKey.Type:
                list.Sort((a, b) =>
                {
                    var c = ((int)a.Type).CompareTo((int)b.Type);
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                break;
            case SortKey.State:
                // Dismissed last and rank ascending in both directions, so the
                // trailing Reverse() must skip this key.
                return list.OrderBy(i => i, Comparer<Item>.Create((a, b) =>
                    SiblingOrder.CompareState(a, b, descending: dir == SortDir.Desc))).ToList();
            case SortKey.Assignee:
                list.Sort((a, b) =>
                {
                    var c = StringComparer.Ordinal.Compare(a.Assignee ?? "", b.Assignee ?? "");
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                break;
            case SortKey.Created:
                list.Sort((a, b) =>
                {
                    var c = a.Created.CompareTo(b.Created);
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                break;
            case SortKey.Due:
                // Nulls (undated) always sort last, in both directions —
                // dated work is the actionable set (design D3). Direction is
                // applied inline so the trailing Reverse() must skip this key.
                list.Sort((a, b) =>
                {
                    if (a.Due is null && b.Due is null) return rankCmp.Compare(a.Rank, b.Rank);
                    if (a.Due is null) return 1;
                    if (b.Due is null) return -1;
                    var c = a.Due.Value.CompareTo(b.Due.Value);
                    if (dir == SortDir.Desc) c = -c;
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                return list;
            case SortKey.Priority:
                // Critical-first by default (Asc); rank tiebreak. Direction is
                // applied inline so the trailing Reverse() must skip this key.
                list.Sort((a, b) =>
                {
                    var c = ((int)b.Priority).CompareTo((int)a.Priority);
                    if (dir == SortDir.Desc) c = -c;
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                return list;
        }
        if (dir == SortDir.Desc) list.Reverse();
        return list;
    }
}
