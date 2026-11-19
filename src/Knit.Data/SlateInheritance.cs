using Knit.Core.Domain;

namespace Knit.Data;

/// <summary>
/// Read-time slate inheritance: an item's effective slate is
/// its own <c>slate:</c> when set, else the nearest ancestor's explicit
/// slate. An explicit child value overrides — filtering the parent's
/// slate re-roots the child's subtree under the child's slate. Nothing is
/// ever written to children (mirrors <c>agent_skills</c> inheritance).
/// </summary>
public static class SlateInheritance
{
    /// <summary>
    /// Effective slate per item id. Missing parents and parent cycles
    /// degrade to "no inherited slate".
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Compute(IReadOnlyCollection<Item> items)
    {
        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var i in items) byId.TryAdd(i.Id, i);

        var memo = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? Effective(Item i)
        {
            if (memo.TryGetValue(i.Id, out var cached)) return cached;
            memo[i.Id] = null; // cycle guard: a re-entrant walk sees "none"
            var value = !string.IsNullOrEmpty(i.Slate)
                ? i.Slate
                : !string.IsNullOrEmpty(i.Parent) && byId.TryGetValue(i.Parent!, out var parent)
                    ? Effective(parent)
                    : null;
            memo[i.Id] = value;
            return value;
        }

        foreach (var i in items) Effective(i);
        return memo;
    }
}
