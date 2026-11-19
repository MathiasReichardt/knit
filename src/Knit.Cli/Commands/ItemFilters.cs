using Knit.Core.Domain;
using Knit.Core.Slates;

namespace Knit.Cli.Commands;

/// <summary>
/// Include/exclude predicate over <see cref="Item.Type"/>,
/// <see cref="Item.State"/> and <see cref="Item.Due"/>. Within a dimension
/// (types or states), includes are OR-ed and excludes are subtracted; across
/// dimensions the result is AND-ed. Due predicates (overdue / before / after)
/// AND with the rest. An empty filter matches every item.
///
/// <para><c>Overdue</c> is a *status* and is state-aware — a closed item is
/// never overdue. <c>DueBefore</c> / <c>DueAfter</c> are raw date windows and
/// deliberately are not: closed items keep their due dates, and asking "what
/// was due before X" is a legitimate retrospective query. Compose with a state
/// filter (<c>--open</c>) to narrow a window to live work.</para>
/// </summary>
internal sealed record ItemFilter(
    HashSet<ItemType>? IncludeTypes,
    HashSet<ItemType>? ExcludeTypes,
    HashSet<ItemState>? IncludeStates,
    HashSet<ItemState>? ExcludeStates,
    bool Overdue = false,
    DateOnly? DueBefore = null,
    DateOnly? DueAfter = null,
    DateOnly Today = default,
    int DueSoonDays = 3,
    Priority? MinPriority = null,
    // Tag filters: includes are AND-ed (item must carry every tag), excludes
    // subtract (item carrying any is dropped). Both case-insensitive.
    IReadOnlyList<string>? IncludeTags = null,
    IReadOnlyList<string>? ExcludeTags = null,
    // Slate filter matches on the item's *effective* slate (read-time
    // inheritance — see SlateInheritance); callers attach EffectiveSlates
    // once items are loaded. NoSlate matches items with no effective slate.
    string? Slate = null,
    bool NoSlate = false,
    IReadOnlyDictionary<string, string?>? EffectiveSlates = null,
    // Resolved milestone identity — the caller resolves the CLI ref
    // (<slug> or <project>/<slug>) via MilestoneResolver first. Matches the
    // *effective* milestone (MilestoneInheritance), attached with the slates.
    (string Project, string Slug)? Milestone = null,
    // Matches the orthogonal `blocked` flag only — a Blocked-by link row
    // without the flag doesn't count, same rule as `ready`.
    bool BlockedOnly = false,
    // Project health signal; OR-ed within the set. An item with no signal
    // never matches — unset is not an assessment.
    HashSet<Health>? IncludeHealths = null,
    IReadOnlyDictionary<string, Knit.Data.MilestoneInheritance.Effective>? EffectiveMilestones = null)
{
    public bool IsEmpty =>
        (IncludeTypes is null || IncludeTypes.Count == 0) &&
        (ExcludeTypes is null || ExcludeTypes.Count == 0) &&
        (IncludeStates is null || IncludeStates.Count == 0) &&
        (ExcludeStates is null || ExcludeStates.Count == 0) &&
        (IncludeTags is null || IncludeTags.Count == 0) &&
        (ExcludeTags is null || ExcludeTags.Count == 0) &&
        (IncludeHealths is null || IncludeHealths.Count == 0) &&
        Slate is null && !NoSlate && Milestone is null && !BlockedOnly &&
        !Overdue && DueBefore is null && DueAfter is null && MinPriority is null;

    public bool NeedsSlates => Slate is not null || NoSlate;

    /// <summary>
    /// Attaches the read-time inheritance maps the slate / milestone filters
    /// match on, computed over the full item set so an ancestor outside the
    /// rendered scope still contributes.
    /// </summary>
    public ItemFilter WithInheritance(IReadOnlyCollection<Item> allItems) => this with
    {
        EffectiveSlates = NeedsSlates ? Knit.Data.SlateInheritance.Compute(allItems) : EffectiveSlates,
        EffectiveMilestones = Milestone is not null ? Knit.Data.MilestoneInheritance.Compute(allItems) : EffectiveMilestones,
    };

    public bool Matches(Item i)
    {
        if (IncludeTypes is { Count: > 0 } && !IncludeTypes.Contains(i.Type)) return false;
        if (ExcludeTypes is { Count: > 0 } && ExcludeTypes.Contains(i.Type)) return false;
        if (IncludeStates is { Count: > 0 } && !IncludeStates.Contains(i.State)) return false;
        if (ExcludeStates is { Count: > 0 } && ExcludeStates.Contains(i.State)) return false;
        if (IncludeTags is { Count: > 0 } && !IncludeTags.All(t => i.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (ExcludeTags is { Count: > 0 } && ExcludeTags.Any(t => i.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (Overdue && DueDates.Status(i.Due, i.State, Today, DueSoonDays) != DueStatus.Overdue) return false;
        if (DueBefore is { } lo && !(i.Due is { } d1 && d1 < lo)) return false;
        if (DueAfter is { } hi && !(i.Due is { } d2 && d2 > hi)) return false;
        if (MinPriority is { } min && (int)i.Priority < (int)min) return false;
        if (BlockedOnly && !i.IsBlocked) return false;
        if (IncludeHealths is { Count: > 0 } && (i.Health is not { } h || !IncludeHealths.Contains(h))) return false;
        if (Milestone is { } ms &&
            !(EffectiveMilestones is not null && EffectiveMilestones.TryGetValue(i.Id, out var em) && em.Is(ms.Project, ms.Slug)))
            return false;
        if (NeedsSlates)
        {
            // Fall back to the explicit value when no map was attached
            // (inheritance then simply doesn't apply).
            var effective = EffectiveSlates is not null
                ? EffectiveSlates.GetValueOrDefault(i.Id)
                : i.Slate;
            if (Slate is not null && !string.Equals(effective, Slate, StringComparison.Ordinal)) return false;
            if (NoSlate && !string.IsNullOrEmpty(effective)) return false;
        }
        return true;
    }
}

internal static class ItemFilters
{
    public static System.CommandLine.Option<string?> SlateOption() => new("--slate")
    {
        Description = "Only items in this slate. Inherited: matches items whose nearest ancestor with a slate has it, unless the item sets its own.",
    };

    public static System.CommandLine.Option<bool> NoSlateOption() => new("--no-slate")
    {
        Description = "Only items with no slate (own or inherited).",
    };

    /// <summary>The slate-only filter of <c>next</c> / <c>ready</c> / <c>pick</c>, validated like <c>list</c>'s.</summary>
    public static (ItemFilter? filter, string? error, Output.ErrorCode code) ParseSlate(string root, string? slate, bool noSlate, IEnumerable<Item> items)
    {
        var (filter, error) = Parse(null, null, null, null, open: false, slate: slate, noSlate: noSlate);
        if (filter is null) return (null, error, Output.ErrorCode.BadArgument);
        return UnknownSlate(root, filter.Slate, items) is { } unknown
            ? (null, unknown, Output.ErrorCode.NotFound)
            : (filter, null, default);
    }

    /// <summary>
    /// A <c>--slate</c> naming no slate — not in the registry, on no item —
    /// would match nothing and read as "nothing there". Null when the slate
    /// is known or none was asked for.
    /// </summary>
    public static string? UnknownSlate(string root, string? slate, IEnumerable<Item> items)
    {
        if (slate is null) return null;
        if (SlateRegistry.Load(root).Any(r => r.Name.Equals(slate, StringComparison.OrdinalIgnoreCase))) return null;
        if (items.Any(i => string.Equals(i.Slate, slate, StringComparison.Ordinal))) return null;
        return $"no slate '{slate}' — see `knit slate list`";
    }

    public static (ItemFilter? filter, string? error) Parse(
        IReadOnlyList<string>? types,
        IReadOnlyList<string>? excludeTypes,
        IReadOnlyList<string>? states,
        IReadOnlyList<string>? excludeStates,
        bool open,
        bool overdue = false,
        string? dueBefore = null,
        string? dueAfter = null,
        int dueSoonDays = 3,
        string? minPriority = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? noTags = null,
        string? slate = null,
        bool noSlate = false,
        (string Project, string Slug)? milestone = null,
        bool blocked = false,
        IReadOnlyList<string>? healths = null)
    {
        if (open && ((states is { Count: > 0 }) || (excludeStates is { Count: > 0 })))
            return (null, "--open cannot be combined with --state / --exclude-state");
        if (!string.IsNullOrEmpty(slate) && noSlate)
            return (null, "--slate cannot be combined with --no-slate");

        var (inT, e1) = ParseTypes(types);
        if (e1 is not null) return (null, e1);
        var (exT, e2) = ParseTypes(excludeTypes);
        if (e2 is not null) return (null, e2);
        var (inS, e3) = ParseStates(states);
        if (e3 is not null) return (null, e3);
        var (exS, e4) = ParseStates(excludeStates);
        if (e4 is not null) return (null, e4);

        DateOnly? before = null, after = null;
        if (!string.IsNullOrEmpty(dueBefore))
        {
            if (!DueDates.TryParse(dueBefore, out var b)) return (null, $"invalid --due-before date '{dueBefore}' — expected YYYY-MM-DD");
            before = b;
        }
        if (!string.IsNullOrEmpty(dueAfter))
        {
            if (!DueDates.TryParse(dueAfter, out var a)) return (null, $"invalid --due-after date '{dueAfter}' — expected YYYY-MM-DD");
            after = a;
        }

        Priority? minPrio = null;
        if (!string.IsNullOrEmpty(minPriority))
        {
            if (!Priorities.TryParse(minPriority, out var mp))
                return (null, $"unknown priority '{minPriority}' — expected someday|normal|important|critical");
            minPrio = mp;
        }

        HashSet<Health>? inHealth = null;
        if (healths is { Count: > 0 })
        {
            inHealth = [];
            foreach (var raw in healths)
            {
                if (!Healths.TryParse(raw, out var hv) || hv is not { } h)
                    return (null, $"unknown health '{raw}' — expected on-track|at-risk|off-track");
                inHealth.Add(h);
            }
        }

        if (open)
        {
            exS ??= [];
            exS.Add(ItemState.Done);
            exS.Add(ItemState.Dismissed);
        }

        var inTags = Knit.Core.Kb.Tags.Normalize(tags);
        var exTags = Knit.Core.Kb.Tags.Normalize(noTags);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var slateSlug = string.IsNullOrEmpty(slate) ? null : Knit.Core.Ids.Slug.From(slate);
        return (new ItemFilter(inT, exT, inS, exS, overdue, before, after, today, dueSoonDays, minPrio, inTags, exTags, slateSlug, noSlate, Milestone: milestone, BlockedOnly: blocked, IncludeHealths: inHealth), null);
    }

    private static (HashSet<ItemType>?, string?) ParseTypes(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0) return (null, null);
        var set = new HashSet<ItemType>();
        foreach (var s in raw)
        {
            if (!EnumStrings.TryParseItemType(s, out var t)) return (null, $"unknown type: {s}");
            set.Add(t);
        }
        return (set, null);
    }

    private static (HashSet<ItemState>?, string?) ParseStates(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0) return (null, null);
        var set = new HashSet<ItemState>();
        foreach (var s in raw)
        {
            if (!EnumStrings.TryParseItemState(s, out var st)) return (null, $"unknown state: {s}");
            set.Add(st);
        }
        return (set, null);
    }
}
