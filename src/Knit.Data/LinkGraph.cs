using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Data;

/// <summary>
/// In-memory link-graph queries built from the loaded item set.
/// Used by `knit ready`, `knit tree`, and link-related views.
/// </summary>
public sealed class LinkGraph
{
    private readonly Dictionary<string, Item> _byId;
    private readonly Dictionary<string, List<string>> _blockedBy;
    private readonly Dictionary<string, List<string>> _blocks;
    private readonly Dictionary<string, List<string>> _children;

    public LinkGraph(IEnumerable<Item> items)
    {
        var list = items.ToList();
        _byId = list.ToDictionary(i => i.Id);
        _blockedBy = new Dictionary<string, List<string>>();
        _blocks = new Dictionary<string, List<string>>();
        _children = new Dictionary<string, List<string>>();

        // Blocker edges come from both row kinds — a `Blocks` row on the
        // blocker and a `Blocked by` row on the blocked item (live
        // impediments). The same pair authored both ways is one edge.
        var pairs = new HashSet<(string Blocker, string Blocked)>();

        foreach (var item in list)
        {
            if (!string.IsNullOrEmpty(item.Parent))
            {
                if (!_children.TryGetValue(item.Parent, out var kids))
                {
                    kids = new List<string>();
                    _children[item.Parent] = kids;
                }
                kids.Add(item.Id);
            }

            foreach (var targetId in item.BlockingTargetIds)
                AddEdge(pairs, item.Id, targetId);
            foreach (var blockerId in item.BlockedByTargetIds)
                AddEdge(pairs, blockerId, item.Id);
        }
    }

    private void AddEdge(HashSet<(string, string)> pairs, string blockerId, string blockedId)
    {
        if (!pairs.Add((blockerId, blockedId))) return;
        if (!_blockedBy.TryGetValue(blockedId, out var blockers))
        {
            blockers = new List<string>();
            _blockedBy[blockedId] = blockers;
        }
        blockers.Add(blockerId);
        if (!_blocks.TryGetValue(blockerId, out var blocked))
        {
            blocked = new List<string>();
            _blocks[blockerId] = blocked;
        }
        blocked.Add(blockedId);
    }

    public IReadOnlyCollection<Item> Items => _byId.Values;

    public Item? Get(string id) => _byId.TryGetValue(id, out var v) ? v : null;

    public IReadOnlyList<string> ChildrenOf(string id) =>
        _children.TryGetValue(id, out var list) ? list : [];

    public IReadOnlyList<string> BlockersOf(string id) =>
        _blockedBy.TryGetValue(id, out var list) ? list : [];

    /// <summary>
    /// Items the given item blocks — the forward view of the same merged,
    /// per-pair-deduped edge set as <see cref="BlockersOf"/>.
    /// </summary>
    public IReadOnlyList<string> BlockedTargetsOf(string id) =>
        _blocks.TryGetValue(id, out var list) ? list : [];

    /// <summary>
    /// Open items with a <c>Blocked by</c> row naming <paramref name="blockerId"/>.
    /// Only those rows count: a <c>Blocks</c> row is a planned dependency and
    /// never blocks. Closing a blocker resolves nothing,
    /// so once it is closed these are the rows someone has to resolve.
    /// </summary>
    public IReadOnlyList<BlockedDependent> BlockedDependentsOf(string blockerId) =>
        BlockedTargetsOf(blockerId)
            .Select(Get)
            .Where(i => i is not null && IsOpen(i) && i.BlockedByTargetIds.Contains(blockerId))
            .Select(i => new BlockedDependent(i!, Remaining(i!, blockerId)))
            .ToList();

    /// <summary>
    /// Every <c>Blocked by</c> row on an open item whose blocker is closed —
    /// the repo-wide view of <see cref="BlockedDependentsOf"/>.
    /// </summary>
    public IEnumerable<(BlockedDependent Dependent, Item Blocker)> StaleBlocks() =>
        _byId.Values
            .Where(b => !IsOpen(b))
            .SelectMany(b => BlockedDependentsOf(b.Id).Select(d => (d, b)));

    /// <summary>
    /// The nearest open ancestor carrying a block, which holds this item and
    /// its whole subtree: a block on a feature holds its tasks. Inherited, not
    /// copied — unblocking the ancestor releases them. <c>null</c> ⇔ nothing
    /// above holds it.
    /// </summary>
    public Item? HeldBy(string id) => Get(id) is { } item ? HeldBy(item, Get) : null;

    /// <inheritdoc cref="HeldBy(string)"/>
    public static Item? HeldBy(Item item, Func<string, Item?> get)
    {
        if (!IsOpen(item)) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal) { item.Id };
        var id = item.Parent;
        while (!string.IsNullOrEmpty(id) && seen.Add(id) && get(id) is { } parent)
        {
            if (IsOpen(parent) && parent.IsBlocked) return parent;
            id = parent.Parent;
        }
        return null;
    }

    public static bool IsOpen(Item item) =>
        item.State is not (ItemState.Done or ItemState.Dismissed);

    private static string? Remaining(Item item, string blockerId) =>
        (item with
        {
            LinkRows = item.LinkRows
                .Where(r => r.Rel != LinkRel.BlockedBy || LinkPath.ItemIdFromTarget(r.TargetPath) != blockerId)
                .ToList(),
        }).BlockedSummary;

    /// <summary>
    /// Whether the item carries a non-empty <c>blocked</c> flag. Its own
    /// flag and a blocked ancestor (<see cref="HeldBy(string)"/>) gate readiness; link rows
    /// (both kinds) feed the dependency graph but never re-block an item
    /// on their own.
    /// </summary>
    public bool IsBlocked(string id) =>
        _byId.TryGetValue(id, out var item) && item.IsBlocked;

    /// <summary>
    /// Items currently actionable: open state (not done, not dismissed),
    /// not blocked, and not held by a blocked ancestor (<see cref="HeldBy(string)"/>).
    /// A <c>Blocks</c> row never gates.
    ///
    /// A <c>draft</c> is planned work that isn't specified or approved yet,
    /// so it is never actionable — this is the one place `next`, `ready` and
    /// `pick` all read from. It still counts everywhere else (rollups,
    /// overdue, at-risk): not having specified the work doesn't move the date.
    /// A <c>review</c> item is implemented and waits on its reviewer, not on
    /// the implementer, so it isn't actionable either.
    /// </summary>
    public IEnumerable<Item> Ready(string? assignee = null)
    {
        foreach (var item in _byId.Values)
        {
            if (item.State is ItemState.Draft or ItemState.Review or ItemState.Done or ItemState.Dismissed) continue;
            if (item.IsBlocked || HeldBy(item, Get) is not null) continue;
            if (assignee is not null && !string.Equals(item.Assignee, assignee, StringComparison.Ordinal))
                continue;
            yield return item;
        }
    }

    /// <summary>
    /// The given item plus all transitive descendants via parent links.
    /// Returns empty if the id is unknown. Cycle-safe.
    /// </summary>
    public IEnumerable<Item> Subtree(string rootId)
    {
        if (!_byId.ContainsKey(rootId)) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id)) continue;
            if (_byId.TryGetValue(id, out var item)) yield return item;
            foreach (var c in ChildrenOf(id)) stack.Push(c);
        }
    }

    public IEnumerable<Item> Roots()
    {
        foreach (var item in _byId.Values)
        {
            if (string.IsNullOrEmpty(item.Parent) || !_byId.ContainsKey(item.Parent))
                yield return item;
        }
    }

    /// <summary>
    /// Set of every item id whose ancestor chain contains any of the
    /// <paramref name="hiddenRoots"/>. Used by the open/closed filter:
    /// hiding a parent (dismissed or done) also hides its whole subtree.
    /// The hidden roots
    /// themselves are NOT included — the caller filters them by other
    /// means (state predicate).
    /// </summary>
    public HashSet<string> DescendantsOf(IEnumerable<string> hiddenRoots)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rootId in hiddenRoots)
        {
            foreach (var descendant in Subtree(rootId))
            {
                if (descendant.Id == rootId) continue;
                result.Add(descendant.Id);
            }
        }
        return result;
    }
}

/// <summary>
/// An open item blocked by a given item. <see cref="Remaining"/> is what
/// still blocks it without that blocker (<see cref="Item.BlockedSummary"/>
/// shape); <c>null</c> ⇔ that blocker is its only impediment.
/// </summary>
public sealed record BlockedDependent(Item Item, string? Remaining);
