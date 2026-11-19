using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// Item-walk operations behind the slate registry — shared by the CLI
/// <c>knit slate</c> verbs and the <c>/api/slates</c> endpoints so both
/// surfaces rewrite items identically.
/// </summary>
public static class SlateItems
{
    /// <summary>
    /// Rewrite <c>slate:</c> on every item assigned to
    /// <paramref name="oldName"/>. Returns the number of items updated.
    /// </summary>
    public static int Rename(ItemStore store, string oldName, string newName)
    {
        var affected = store.AllItems()
            .Where(i => string.Equals(i.Slate, oldName, StringComparison.Ordinal))
            .ToList();
        foreach (var item in affected)
            store.Write(item with { Slate = newName });
        return affected.Count;
    }

    /// <summary>
    /// Drop <c>slate:</c> from every item assigned to <paramref name="name"/>.
    /// Returns the number of items updated.
    /// </summary>
    public static int Clear(ItemStore store, string name)
    {
        var affected = store.AllItems()
            .Where(i => string.Equals(i.Slate, name, StringComparison.Ordinal))
            .ToList();
        foreach (var item in affected)
            store.Write(item with { Slate = null });
        return affected.Count;
    }

    /// <summary>Items currently assigned to the slate (exact name match).</summary>
    public static int Count(ItemStore store, string name) =>
        store.AllItems().Count(i => string.Equals(i.Slate, name, StringComparison.Ordinal));

    /// <summary>
    /// Distinct <c>slate:</c> values present on items, in no particular
    /// order. Unioned with the registry for dropdown sources so a
    /// hand-typed or stale value never disappears (lenient fallback).
    /// </summary>
    public static IReadOnlyList<string> Distinct(ItemStore store) =>
        store.AllItems()
            .Select(i => i.Slate)
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
