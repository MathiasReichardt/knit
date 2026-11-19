using Knit.Core.Domain;
using Knit.Core.Projects;

namespace Knit.Data;

/// <summary>
/// Counts for one assignee inside a scope. Unassigned work rides along as a
/// row with an empty <see cref="Assignee"/> — a PM reading a load table needs
/// the unclaimed pile in the same column as the claimed ones.
/// </summary>
public sealed record AssigneeLoad(
    string Assignee, int Total, int Done, int InProgress, int Todo, int Blocked, int Overdue);

/// <summary>
/// The rollup behind <c>knit status</c> and the SPA summary strip. One shape,
/// computed once, so the two surfaces can't disagree about what "62% done"
/// means.
/// </summary>
public sealed record StatusReport(
    string Scope,
    int Total,
    int Draft,
    int Done,
    int InProgress,
    int Review,
    int Todo,
    int Dismissed,
    int Blocked,
    int Overdue,
    int Unassigned,
    int Percent,
    Health? Health,
    IReadOnlyList<AssigneeLoad> ByAssignee,
    IReadOnlyList<Item> BlockedItems,
    IReadOnlyList<Item> OverdueItems);

public static class StatusRollup
{
    /// <summary>
    /// Narrows the graph to a reporting scope. Every argument is optional and
    /// AND-ed; all null means the whole repo.
    ///
    /// <para>Deliberately *scope* filters only — no state/type/assignee
    /// narrowing. A completion percentage computed over a set that already
    /// excluded done items is a tautology (always 0%), so the report refuses
    /// to be handed one.</para>
    ///
    /// <para><paramref name="underId"/> must already be resolved to a full id;
    /// the caller owns prefix resolution because CLI and HTTP word that
    /// failure differently.</para>
    /// </summary>
    public static (IReadOnlyList<Item> Items, string? Error) Scope(
        ItemStore store, string root, LinkGraph graph,
        string? project, string? slate, string? milestone, string? underId)
    {
        IEnumerable<Item> items = string.IsNullOrEmpty(underId)
            ? graph.Items
            : graph.Subtree(underId);

        if (!string.IsNullOrEmpty(project))
        {
            if (!ProjectsLayout.ProjectSlugs(root).Contains(project, StringComparer.Ordinal))
                return ([], $"unknown project '{project}' — no folder under .knit/projects/");
            items = items.Where(i =>
                string.Equals(MilestoneResolver.ProjectScopeOf(store, i), project, StringComparison.Ordinal));
        }

        if (!string.IsNullOrEmpty(milestone))
        {
            var (target, err) = MilestoneResolver.Resolve(store, root, null, milestone);
            if (target is null) return ([], err);
            // Effective (inherited) milestone, over the full graph like the slate below.
            var effective = MilestoneInheritance.Compute(graph.Items);
            items = items.Where(i =>
                effective.TryGetValue(i.Id, out var e) && e.Is(target.ProjectSlug, target.Slug));
        }

        if (!string.IsNullOrEmpty(slate))
        {
            // Effective (inherited) slate, computed over the full graph so an
            // ancestor outside an --under scope still contributes.
            var effective = SlateInheritance.Compute(graph.Items);
            var wanted = Knit.Core.Ids.Slug.From(slate);
            items = items.Where(i =>
                string.Equals(effective.GetValueOrDefault(i.Id), wanted, StringComparison.Ordinal));
        }

        return (items.ToList(), null);
    }

    /// <summary>
    /// Rolls a scoped item set up into <see cref="StatusReport"/>.
    ///
    /// <para>Percent is done over <c>total - dismissed</c>: dismissed work was
    /// descoped, and leaving it in the denominator would cap a finished
    /// project below 100% forever.</para>
    /// </summary>
    public static StatusReport Compute(IReadOnlyList<Item> items, DateOnly today, string scope)
    {
        var draft = items.Count(i => i.State == ItemState.Draft);
        var done = items.Count(i => i.State == ItemState.Done);
        var dismissed = items.Count(i => i.State == ItemState.Dismissed);
        var inProgress = items.Count(i => i.State == ItemState.InProgress);
        var review = items.Count(i => i.State == ItemState.Review);
        var todo = items.Count(i => i.State == ItemState.Todo);

        var blocked = items.Where(i => i.IsBlocked).ToList();
        var overdue = items.Where(IsOverdue).ToList();
        var considered = items.Count - dismissed;
        var percent = considered <= 0 ? 0 : (int)Math.Round(done * 100.0 / considered, MidpointRounding.AwayFromZero);

        // Health is a project-level signal; it only means something when the
        // scope resolves to exactly one project. Repo-wide, three projects'
        // signals have no defensible way to merge.
        var projects = items.Where(i => i.Type == ItemType.Project).ToList();
        var health = projects.Count == 1 ? projects[0].Health : null;

        var byAssignee = items
            .GroupBy(i => i.Assignee ?? "", StringComparer.Ordinal)
            .Select(g => new AssigneeLoad(
                g.Key,
                g.Count(),
                g.Count(i => i.State == ItemState.Done),
                g.Count(i => i.State == ItemState.InProgress),
                g.Count(i => i.State == ItemState.Todo),
                g.Count(i => i.IsBlocked),
                g.Count(IsOverdue)))
            .OrderByDescending(a => a.InProgress + a.Todo)
            .ThenBy(a => a.Assignee, StringComparer.Ordinal)
            .ToList();

        return new StatusReport(
            Scope: scope,
            Total: items.Count,
            Draft: draft,
            Done: done,
            InProgress: inProgress,
            Review: review,
            Todo: todo,
            Dismissed: dismissed,
            Blocked: blocked.Count,
            Overdue: overdue.Count,
            Unassigned: items.Count(i => string.IsNullOrEmpty(i.Assignee)),
            Percent: percent,
            Health: health,
            ByAssignee: byAssignee,
            BlockedItems: Attention(blocked),
            OverdueItems: Attention(overdue));

        bool IsOverdue(Item i) => DueDates.Status(i.Due, i.State, today, 0) == DueStatus.Overdue;
    }

    /// <summary>
    /// Label for a scope, matching the flags that produced it. Empty scope
    /// reads "all items" rather than "" so the report always names what it
    /// counted.
    /// </summary>
    public static string Label(string? project, string? slate, string? milestone, string? underShortId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(project)) parts.Add($"project: {project}");
        if (!string.IsNullOrEmpty(milestone)) parts.Add($"milestone: {milestone}");
        if (!string.IsNullOrEmpty(slate)) parts.Add($"slate: {slate}");
        if (!string.IsNullOrEmpty(underShortId)) parts.Add($"under: {underShortId}");
        return parts.Count == 0 ? "all items" : string.Join(" · ", parts);
    }

    private static IReadOnlyList<Item> Attention(IEnumerable<Item> items) => items
        .OrderByDescending(i => (int)i.Priority)
        .ThenBy(i => i.Due ?? DateOnly.MaxValue)
        .ThenBy(i => i.Rank ?? "", StringComparer.Ordinal)
        .ToList();
}
