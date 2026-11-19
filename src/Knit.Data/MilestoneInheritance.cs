using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Data;

/// <summary>
/// Read-time milestone inheritance, the same rule as <see cref="SlateInheritance"/>:
/// an item's effective milestone is its own <c>Milestone</c> link when it carries
/// one, else the nearest ancestor's. An own link overrides — a dangling one too,
/// so a broken link never silently picks up the parent's. Nothing is written to
/// children.
/// </summary>
public static class MilestoneInheritance
{
    /// <param name="InheritedFrom">The ancestor holding the link; empty when it is the item's own.</param>
    public sealed record Effective(string Project, string Slug, string InheritedFrom)
    {
        public string QualifiedSlug => $"{Project}/{Slug}";

        public bool Is(string project, string slug) =>
            string.Equals(Project, project, StringComparison.Ordinal)
            && string.Equals(Slug, slug, StringComparison.Ordinal);
    }

    /// <summary>
    /// Effective milestone per item id; items with none by either route are
    /// absent. Missing parents and parent cycles degrade to "no inherited milestone".
    /// </summary>
    public static IReadOnlyDictionary<string, Effective> Compute(IReadOnlyCollection<Item> items)
    {
        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var i in items) byId.TryAdd(i.Id, i);

        var memo = new Dictionary<string, Effective?>(StringComparer.Ordinal);
        Effective? Resolve(Item i)
        {
            if (memo.TryGetValue(i.Id, out var cached)) return cached;
            memo[i.Id] = null; // cycle guard: a re-entrant walk sees "none"
            var value = Own(i) is { } own
                ? new Effective(own.Project, own.Slug, "")
                : !string.IsNullOrEmpty(i.Parent) && byId.TryGetValue(i.Parent!, out var parent)
                    ? Resolve(parent) is { } p
                        ? p with { InheritedFrom = p.InheritedFrom.Length == 0 ? parent.Id : p.InheritedFrom }
                        : null
                    : null;
            memo[i.Id] = value;
            return value;
        }

        var result = new Dictionary<string, Effective>(StringComparer.Ordinal);
        foreach (var i in items)
            if (Resolve(i) is { } e) result[i.Id] = e;
        return result;
    }

    private static (string Project, string Slug)? Own(Item item)
    {
        foreach (var row in item.MilestoneRows)
            if (LinkPath.MilestoneFromTarget(row.TargetPath) is { } id)
                return (id.Project, id.Slug);
        return null;
    }
}
