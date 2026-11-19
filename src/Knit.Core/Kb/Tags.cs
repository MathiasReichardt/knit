namespace Knit.Core.Kb;

/// <summary>
/// The single chokepoint for tag list hygiene. Both write paths — the SPA
/// PATCH (<c>KbMapping.SetListOrDrop</c>) and the CLI <c>kb set tags</c> — route
/// through here so the dedup/trim rule can't drift between them.
/// </summary>
public static class Tags
{
    /// <summary>
    /// Trim each tag, drop empties, and dedup case-insensitively while
    /// preserving the first-seen casing and order (<c>API</c> then <c>api</c>
    /// → <c>[API]</c>).
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? tags)
    {
        if (tags is null) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in tags)
        {
            var t = raw?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (seen.Add(t)) result.Add(t);
        }
        return result;
    }
}
