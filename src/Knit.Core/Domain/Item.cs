using Knit.Core.Frontmatter;

namespace Knit.Core.Domain;

public sealed record Item
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ItemType Type { get; init; }
    public required ItemState State { get; init; }
    public string? Assignee { get; init; }

    /// <summary>
    /// Parent item id, derived from the <c>Parent</c> row in the body
    /// <c>## Links</c> table when its target resolves to an item folder.
    /// Kept as a denormalized field for cheap graph queries.
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>
    /// The single source of truth for typed relations: the rows of the
    /// body <c>## Links</c> table (item↔item, item↔KB).
    /// </summary>
    public IReadOnlyList<LinkRow> LinkRows { get; init; } = [];

    public string? Rank { get; init; }
    public string? Effort { get; init; }

    /// <summary>
    /// Item priority (<see cref="Domain.Priority"/>). Defaults to
    /// <see cref="Priority.Normal"/>; an item with no <c>priority:</c>
    /// frontmatter is exactly <c>Normal</c>, and the field is only written
    /// out when it differs from <c>Normal</c>.
    /// </summary>
    public Priority Priority { get; init; } = Priority.Normal;

    /// <summary>
    /// Project health signal (<see cref="Domain.Health"/>). <c>null</c> = no
    /// signal. Only meaningful on type <c>project</c>; verify flags it on any
    /// other type. A malformed on-disk <c>health:</c> degrades to <c>null</c>.
    /// </summary>
    public Health? Health { get; init; }

    /// <summary>
    /// Optional date-only deadline (<c>YYYY-MM-DD</c>). <c>null</c> = no due.
    /// Timezone-free by design (a tracker rarely needs sub-day precision);
    /// a malformed on-disk <c>due:</c> degrades to <c>null</c> on read.
    /// </summary>
    public DateOnly? Due { get; init; }

    public DateTimeOffset Created { get; init; }
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<Comment> Comments { get; init; } = [];

    // KB / context fields.
    public string? Summary { get; init; }
    public IReadOnlyList<string> Feature { get; init; } = [];
    public IReadOnlyList<string> AgentSkills { get; init; } = [];

    /// <summary>
    /// Free-form cross-cutting labels. Same shape + normalization as KB tags
    /// (see <see cref="Knit.Core.Kb.Tags"/>): trimmed, empties dropped,
    /// case-insensitively deduped. Unset ⇒ empty; only written when non-empty.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Design { get; init; }

    /// <summary>
    /// Doc-folder pointer for project items: the slug of this project's
    /// <c>.knit/projects/&lt;slug&gt;/</c> folder (the <c>design:</c>
    /// pattern). Only meaningful on type <c>project</c>; verify flags the
    /// field on any other type and a slug with no folder behind it.
    /// </summary>
    public string? Project { get; init; }

    /// <summary>
    /// Optional slate assignment (work-selection grouping). Holds the
    /// slugified slate name from the <c>.knit/slates.md</c> registry —
    /// the name itself, not an opaque id, so a registry rename walks
    /// items. Inheritance (nearest-ancestor slate) is computed at read
    /// time and never written to children.
    /// </summary>
    public string? Slate { get; init; }

    /// <summary>
    /// Free-text block reason (frontmatter <c>blocked:</c>) — an impediment
    /// that is not another item. Each <c>Blocked by</c> row is a reason of
    /// its own, its Notes cell saying why; <see cref="IsBlocked"/> combines both.
    /// </summary>
    public string? BlockedReason { get; init; }

    public bool IsBlocked =>
        !string.IsNullOrEmpty(BlockedReason) || LinkRows.Any(r => r.Rel == LinkRel.BlockedBy);

    /// <summary>
    /// Every reason in one line — the free-text reason, then one
    /// <c>blocked by &lt;title&gt; — &lt;notes&gt;</c> per row. <c>null</c> ⇔ not blocked.
    /// </summary>
    public string? BlockedSummary
    {
        get
        {
            var reasons = LinkRows.Where(r => r.Rel == LinkRel.BlockedBy)
                .Select(r => string.IsNullOrEmpty(r.Notes)
                    ? $"blocked by {r.TargetText}"
                    : $"blocked by {r.TargetText} — {r.Notes}")
                .Prepend(BlockedReason)
                .Where(r => !string.IsNullOrEmpty(r))
                .ToList();
            return reasons.Count == 0 ? null : string.Join("; ", reasons);
        }
    }

    /// <summary>
    /// Set the Notes of the <c>Blocked by</c> row for <paramref name="blockerId"/>,
    /// so the reason goes when that link does. <paramref name="previous"/> is the
    /// replaced note when it differed. No row ⇒ unchanged.
    /// </summary>
    public Item WithBlockerNote(string blockerId, string note, out string? previous)
    {
        previous = null;
        var rows = LinkRows.ToList();
        var i = rows.FindIndex(r => r.Rel == LinkRel.BlockedBy && LinkPath.ItemIdFromTarget(r.TargetPath) == blockerId);
        if (i < 0) return this;
        if (!string.IsNullOrEmpty(rows[i].Notes) && rows[i].Notes != note) previous = rows[i].Notes;
        rows[i] = rows[i] with { Notes = note };
        return this with { LinkRows = rows };
    }

    public IReadOnlyDictionary<string, object?> ExtraFrontmatter { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Convenience view: item ids of every <c>Blocks</c> row whose target
    /// is an item folder. Used by graph code that wants id-keyed adjacency.
    /// </summary>
    public IEnumerable<string> BlockingTargetIds =>
        LinkRows.Where(r => r.Rel == LinkRel.Blocks)
            .Select(r => LinkPath.ItemIdFromTarget(r.TargetPath))
            .Where(id => !string.IsNullOrEmpty(id))!
            .Cast<string>();

    /// <summary>
    /// Convenience view: item ids of every <c>Blocked by</c> row whose
    /// target is an item folder. Under the live-impediment semantics
    /// these are reverse blocker edges — the graph
    /// consumes them alongside <see cref="BlockingTargetIds"/>, deduped
    /// per pair.
    /// </summary>
    public IEnumerable<string> BlockedByTargetIds =>
        LinkRows.Where(r => r.Rel == LinkRel.BlockedBy)
            .Select(r => LinkPath.ItemIdFromTarget(r.TargetPath))
            .Where(id => !string.IsNullOrEmpty(id))!
            .Cast<string>();

    /// <summary>
    /// Convenience view: KB ref rows in the <see cref="LinkRows"/>.
    /// </summary>
    public IEnumerable<LinkRow> KbRefRows =>
        LinkRows.Where(r => r.Rel == LinkRel.KbRef);

    /// <summary>
    /// Convenience view: Milestone rows in the <see cref="LinkRows"/>.
    /// One-per-item is a convention (verify warns on multiples), so this
    /// stays a list at the parse level.
    /// </summary>
    public IEnumerable<LinkRow> MilestoneRows =>
        LinkRows.Where(r => r.Rel == LinkRel.Milestone);

    /// <summary>
    /// Convenience view: the <c>Branch</c> / <c>Repo</c> / <c>PR</c> /
    /// <c>Commit</c> rows. Several of each are legitimate — work splits across
    /// branches, a feature gets a follow-up PR — so order is preserved.
    /// </summary>
    public IEnumerable<LinkRow> ExternalRows =>
        LinkRows.Where(r => ExternalRefs.IsExternal(r.Rel));
}
