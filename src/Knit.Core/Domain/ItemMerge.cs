using Knit.Core.Frontmatter;

namespace Knit.Core.Domain;

/// <summary>
/// Re-applies one writer's change onto an item another writer changed since
/// it was read. Three tiers, because "re-apply" means different things
/// per field:
/// <list type="bullet">
///   <item><b>Merges</b> — comments, link rows, tags, feature, agent skills.
///     List- or set-shaped, so both writers' additions and removals survive.</item>
///   <item><b>Re-races</b> — every scalar, and the one-per-item Parent and
///     Milestone rows. No <i>other</i> field is lost, but when both writers
///     changed the same one, this writer's value wins.</item>
///   <item><b>Refuses</b> — the body, when both changed it. Prose cannot be
///     re-applied blindly.</item>
/// </list>
/// </summary>
public static class ItemMerge
{
    /// <returns>The merged item, or <c>null</c> when both changed the body.</returns>
    public static Item? ThreeWay(Item basis, Item mine, Item theirs)
    {
        string body;
        if (mine.Body == basis.Body) body = theirs.Body;
        else if (theirs.Body == basis.Body || theirs.Body == mine.Body) body = mine.Body;
        else return null;

        var rows = Set(basis.LinkRows.Where(Multi), mine.LinkRows.Where(Multi), theirs.LinkRows.Where(Multi),
            EqualityComparer<LinkRow>.Default);
        var single = Pick(Single(basis), Single(mine), Single(theirs), RowsEqual);

        return theirs with
        {
            Title = Pick(basis.Title, mine.Title, theirs.Title),
            Type = Pick(basis.Type, mine.Type, theirs.Type),
            State = Pick(basis.State, mine.State, theirs.State),
            Assignee = Pick(basis.Assignee, mine.Assignee, theirs.Assignee),
            Parent = Pick(basis.Parent, mine.Parent, theirs.Parent),
            LinkRows = [.. single.Where(r => r.Rel == LinkRel.Parent), .. rows, .. single.Where(r => r.Rel != LinkRel.Parent)],
            Rank = Pick(basis.Rank, mine.Rank, theirs.Rank),
            Effort = Pick(basis.Effort, mine.Effort, theirs.Effort),
            Priority = Pick(basis.Priority, mine.Priority, theirs.Priority),
            Health = Pick(basis.Health, mine.Health, theirs.Health),
            Due = Pick(basis.Due, mine.Due, theirs.Due),
            Created = Pick(basis.Created, mine.Created, theirs.Created),
            Body = body,
            Comments = Comments(basis.Comments, mine.Comments, theirs.Comments),
            Summary = Pick(basis.Summary, mine.Summary, theirs.Summary),
            Feature = Set(basis.Feature, mine.Feature, theirs.Feature, StringComparer.Ordinal),
            AgentSkills = Set(basis.AgentSkills, mine.AgentSkills, theirs.AgentSkills, StringComparer.Ordinal),
            Tags = Set(basis.Tags, mine.Tags, theirs.Tags, StringComparer.OrdinalIgnoreCase),
            Design = Pick(basis.Design, mine.Design, theirs.Design),
            Project = Pick(basis.Project, mine.Project, theirs.Project),
            Slate = Pick(basis.Slate, mine.Slate, theirs.Slate),
            BlockedReason = Pick(basis.BlockedReason, mine.BlockedReason, theirs.BlockedReason),
            ExtraFrontmatter = ReferenceEquals(mine.ExtraFrontmatter, basis.ExtraFrontmatter)
                ? theirs.ExtraFrontmatter
                : mine.ExtraFrontmatter
        };
    }

    private static T Pick<T>(T basis, T mine, T theirs) =>
        EqualityComparer<T>.Default.Equals(mine, basis) ? theirs : mine;

    private static T Pick<T>(T basis, T mine, T theirs, Func<T, T, bool> equal) =>
        equal(mine, basis) ? theirs : mine;

    private static bool Multi(LinkRow r) => r.Rel is not (LinkRel.Parent or LinkRel.Milestone);

    private static List<LinkRow> Single(Item item) => [.. item.LinkRows.Where(r => !Multi(r))];

    private static bool RowsEqual(List<LinkRow> a, List<LinkRow> b) => a.SequenceEqual(b);

    /// <summary>Theirs, minus what mine removed, plus what mine added — in their order, then mine.</summary>
    private static List<T> Set<T>(IEnumerable<T> basis, IEnumerable<T> mine, IEnumerable<T> theirs, IEqualityComparer<T> eq)
    {
        var basisSet = basis.ToHashSet(eq);
        var mineList = mine.ToList();
        var mineSet = mineList.ToHashSet(eq);
        var result = theirs.Where(t => mineSet.Contains(t) || !basisSet.Contains(t)).ToList();
        var resultSet = result.ToHashSet(eq);
        result.AddRange(mineList.Where(m => !basisSet.Contains(m) && resultSet.Add(m)));
        return result;
    }

    /// <summary>Keyed by id, so an edited comment replaces rather than duplicates.</summary>
    private static List<Comment> Comments(IReadOnlyList<Comment> basis, IReadOnlyList<Comment> mine, IReadOnlyList<Comment> theirs)
    {
        var basisById = basis.DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var mineById = mine.DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        var result = new List<Comment>();
        foreach (var t in theirs)
        {
            if (mineById.TryGetValue(t.Id, out var m))
                result.Add(basisById.TryGetValue(t.Id, out var b) && m == b ? t : m);
            else if (!basisById.ContainsKey(t.Id))
                result.Add(t);
        }
        var seen = result.Select(c => c.Id).ToHashSet();
        result.AddRange(mine.Where(m => !basisById.ContainsKey(m.Id) && seen.Add(m.Id)));
        return result;
    }
}
