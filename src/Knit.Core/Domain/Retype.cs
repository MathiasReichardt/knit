namespace Knit.Core.Domain;

/// <summary>
/// Outcome of <see cref="Retype.Validate"/>. <see cref="ErrorCode"/> is
/// <c>null</c> on success. Error codes are stable wire strings reused
/// by both the CLI and the Web API.
/// </summary>
public sealed record RetypeValidation(
    string? ErrorCode,
    string? Message,
    IReadOnlyList<string> OffendingChildIds)
{
    public static RetypeValidation Ok() => new(null, null, []);

    public static RetypeValidation Fail(string code, string message)
        => new(code, message, []);

    public static RetypeValidation Fail(string code, string message, IReadOnlyList<string> offendingChildIds)
        => new(code, message, offendingChildIds);

    public bool IsOk => ErrorCode is null;
}

/// <summary>
/// Single atomic "change type (and optionally re-parent)" use case.
/// Validates hierarchy on both edges:
/// <list type="bullet">
///   <item>new parent must accept the new type (or new type must be a
///         valid root); see <see cref="Hierarchy.ParentAllowed"/>.</item>
///   <item>every existing child must remain a valid descendant of the
///         new type — otherwise the change would orphan them.</item>
///   <item>the new parent must not lie inside the item's own subtree
///         (cycle protection).</item>
/// </list>
/// Pure: callers are responsible for the on-disk write and for
/// rewriting the <c>Parent</c> link row when <paramref name="newParent"/>
/// differs from <paramref name="current"/>.<see cref="Item.Parent"/>.
/// </summary>
public static class Retype
{
    public const string ErrInvalidType = "invalid_type";
    public const string ErrInvalidParent = "invalid_parent";
    public const string ErrWouldOrphanChildren = "would_orphan_children";
    public const string ErrCycle = "cycle";

    public static RetypeValidation Validate(
        Item current,
        ItemType toType,
        Item? newParent,
        IReadOnlyList<Item> allItems)
    {
        if (newParent is not null && string.Equals(newParent.Id, current.Id, StringComparison.Ordinal))
            return RetypeValidation.Fail(ErrCycle, "an item cannot be its own parent");

        if (newParent is not null && IsInSubtree(newParent.Id, current.Id, allItems))
            return RetypeValidation.Fail(ErrCycle,
                $"new parent {newParent.Id} \"{newParent.Title}\" is inside the subtree of {current.Id} \"{current.Title}\"");

        if (!Hierarchy.ParentAllowed(toType, newParent?.Type))
            return RetypeValidation.Fail(ErrInvalidParent,
                Hierarchy.ExplainRefusal(toType, newParent?.Type));

        var children = allItems
            .Where(i => !string.IsNullOrEmpty(i.Parent)
                        && string.Equals(i.Parent, current.Id, StringComparison.Ordinal))
            .ToList();

        var offending = children
            .Where(c => !Hierarchy.ParentAllowed(c.Type, toType))
            .Select(c => c.Id)
            .ToList();

        if (offending.Count > 0)
            return RetypeValidation.Fail(ErrWouldOrphanChildren,
                $"{offending.Count} existing child(ren) would not be valid under a {toType.ToWire()} parent",
                offending);

        return RetypeValidation.Ok();
    }

    private static bool IsInSubtree(string candidateAncestorId, string rootId, IReadOnlyList<Item> allItems)
    {
        var byId = allItems.ToDictionary(i => i.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(candidateAncestorId, out var node)) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (node is not null && !string.IsNullOrEmpty(node.Parent))
        {
            if (!seen.Add(node.Id)) return false;
            if (string.Equals(node.Parent, rootId, StringComparison.Ordinal)) return true;
            byId.TryGetValue(node.Parent!, out node);
        }
        return false;
    }
}
