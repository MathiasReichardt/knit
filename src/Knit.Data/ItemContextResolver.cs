using Knit.Core.Domain;
using Knit.Core.Frontmatter;

namespace Knit.Data;

public sealed record ResolvedKbRef(string TargetText, string TargetPath, string? Note, string InheritedFrom);

public sealed record ResolvedAgentSkill(string Skill, string InheritedFrom);

public sealed record BlockedStatus(string Reason, string InheritedFrom);

public sealed record ResolvedDesign(string Slug, string InheritedFrom);

/// <param name="Rel">Branch, Repo or PR — carried so the consumer can label it.</param>
/// <param name="Url">The clickable target, or null for a branch / mount / directory.</param>
public sealed record ResolvedExternalRef(
    LinkRel Rel, string Text, string? Url, string? Note, string InheritedFrom);

public sealed record ResolvedItemContext(
    Item Item,
    IReadOnlyList<Item> ParentChain,
    IReadOnlyList<ResolvedKbRef> KbRefs,
    IReadOnlyList<ResolvedAgentSkill> AgentSkills,
    IReadOnlyList<ResolvedExternalRef> ExternalRefs,
    IReadOnlyList<BlockedStatus> Blocked,
    ResolvedDesign? Design,
    MilestoneInheritance.Effective? Milestone);

/// <summary>
/// Resolves the parent-chain-inherited view of an item: unioned KB-ref
/// rows / <c>agent_skills</c>, deduped; <c>blocked</c> (own, then the
/// blocked ancestor holding it); <c>design</c> and the milestone (own or the
/// nearest ancestor's). Sourced from an
/// in-memory <see cref="LinkGraph"/>.
/// </summary>
public static class ItemContextResolver
{
    public static ResolvedItemContext Resolve(LinkGraph graph, Item item)
    {
        var chain = ParentChain(graph, item);
        var ownAndAncestors = new List<Item> { item };
        ownAndAncestors.AddRange(chain);

        var kbRefs = UnionKbRefs(ownAndAncestors, item.Id);
        var agentSkills = UnionAgentSkills(ownAndAncestors);
        var externals = UnionExternalRefs(ownAndAncestors, item.Id);
        var blocked = EffectiveBlocked(graph, item);
        var design = EffectiveDesign(ownAndAncestors, item.Id);
        var milestone = MilestoneInheritance.Compute(ownAndAncestors).GetValueOrDefault(item.Id);

        return new ResolvedItemContext(item, chain, kbRefs, agentSkills, externals, blocked, design, milestone);
    }

    public static IReadOnlyList<Item> ParentChain(LinkGraph graph, Item item)
    {
        var result = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { item.Id };
        var current = item.Parent;
        while (!string.IsNullOrEmpty(current) && seen.Add(current))
        {
            var p = graph.Get(current);
            if (p is null) break;
            result.Add(p);
            current = p.Parent;
        }
        return result;
    }

    private static IReadOnlyList<ResolvedKbRef> UnionKbRefs(IReadOnlyList<Item> ownAndAncestors, string ownId)
    {
        var seen = new Dictionary<string, ResolvedKbRef>(StringComparer.Ordinal);
        foreach (var item in ownAndAncestors)
        {
            foreach (var row in item.KbRefRows)
            {
                var key = row.TargetPath.Replace('\\', '/');
                if (seen.ContainsKey(key)) continue;
                var inheritedFrom = item.Id == ownId ? "" : item.Id;
                seen[key] = new ResolvedKbRef(row.TargetText, row.TargetPath, row.Notes, inheritedFrom);
            }
        }
        return seen.Values.ToList();
    }

    /// <summary>
    /// Branch / Repo / PR rows, own ones first, then the chain's — the branch
    /// a parent feature is being built on is the branch its tasks belong on
    /// too, so these inherit exactly as KB refs do. Commit rows
    /// are the item's own only.
    /// </summary>
    private static IReadOnlyList<ResolvedExternalRef> UnionExternalRefs(
        IReadOnlyList<Item> ownAndAncestors, string ownId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedExternalRef>();
        foreach (var item in ownAndAncestors)
        {
            foreach (var row in item.ExternalRows)
            {
                if (item.Id != ownId && !ExternalRefs.Inherits(row.Rel)) continue;
                if (!seen.Add($"{row.Rel} {row.TargetText}")) continue;
                result.Add(new ResolvedExternalRef(
                    row.Rel, row.TargetText,
                    row.TargetPath.Length == 0 ? null : row.TargetPath,
                    row.Notes,
                    item.Id == ownId ? "" : item.Id));
            }
        }
        return result;
    }

    private static IReadOnlyList<ResolvedAgentSkill> UnionAgentSkills(IReadOnlyList<Item> ownAndAncestors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedAgentSkill>();
        var ownId = ownAndAncestors[0].Id;
        foreach (var item in ownAndAncestors)
        {
            foreach (var s in item.AgentSkills)
            {
                if (!seen.Add(s)) continue;
                var inheritedFrom = item.Id == ownId ? "" : item.Id;
                result.Add(new ResolvedAgentSkill(s, inheritedFrom));
            }
        }
        return result;
    }

    // The item's own block, then the ancestor holding it — both, so an own
    // block never hides the parent's (same predicate as readiness).
    private static IReadOnlyList<BlockedStatus> EffectiveBlocked(LinkGraph graph, Item item)
    {
        var result = new List<BlockedStatus>();
        if (item.BlockedSummary is { } own) result.Add(new BlockedStatus(own, ""));
        if (LinkGraph.HeldBy(item, graph.Get) is { BlockedSummary: { } held } holder)
            result.Add(new BlockedStatus(held, holder.Id));
        return result;
    }

    // Nearest binding wins: an item's own design overrides an ancestor's,
    // matching the "link on the top item, inheritance carries it down" rule.
    private static ResolvedDesign? EffectiveDesign(IReadOnlyList<Item> ownAndAncestors, string ownId)
    {
        foreach (var item in ownAndAncestors)
        {
            if (string.IsNullOrEmpty(item.Design)) continue;
            return new ResolvedDesign(item.Design, item.Id == ownId ? "" : item.Id);
        }
        return null;
    }
}
