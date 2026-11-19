using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Projects;
using Knit.Data;
using Knit.WebApi.Services;

namespace Knit.WebApi.Queries;

internal static class ItemQueryRunner
{
    public sealed class Result
    {
        public IReadOnlyList<Item> Items { get; init; } = [];
        public HashSet<string> MatchIds { get; init; } = new(StringComparer.Ordinal);
        public string? Error { get; init; }
        public bool IsNotFound { get; init; }

        public static Result NotFound(string err) => new() { Error = err, IsNotFound = true };
        public static Result BadRequest(string err) => new() { Error = err };
        public static Result Ok(IReadOnlyList<Item> items, HashSet<string> matchIds) =>
            new() { Items = items, MatchIds = matchIds };
    }

    /// <summary>
    /// Settle <see cref="ItemQuery.TreeSort"/>: the <c>treeSort</c> parameter,
    /// else the repo's <c>tree_sort</c>. Null on success, the 400 message otherwise.
    /// </summary>
    public static string? ResolveTreeSort(RepoServices repo, ItemQuery q)
    {
        var cfg = ConfigLoader.Load(repo.KnitDir);
        var (sort, error) = SiblingOrder.Resolve(q.TreeSortParam, cfg.TreeSort);
        q.TreeSort = sort;
        return error;
    }

    public static Result Run(RepoServices repo, LinkGraph graph, ItemQuery q)
    {
        // Resolve --under scope (and validate it).
        Item? underItem = null;
        if (!string.IsNullOrEmpty(q.Under))
        {
            underItem = TryResolveById(graph, q.Under);
            if (underItem is null) return Result.NotFound($"no item matches '{q.Under}'");
        }

        IEnumerable<Item> universe = underItem is not null
            ? graph.Subtree(underItem.Id)
            : graph.Items;

        // Resolve "mine" handle.
        string? myHandle = null;
        if (q.Mine)
        {
            var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(repo.Root));
            myHandle = cfg.Handle;
        }

        // Build the ready set (over the full graph — readiness is global).
        HashSet<string>? readyIds = q.Ready
            ? new HashSet<string>(graph.Ready().Select(i => i.Id), StringComparer.Ordinal)
            : null;

        // Subtree-hide: when a parent is excluded by state (e.g. --open or
        // explicit excludeState=done,dismissed) hide its descendants too.
        // Belongs in the query layer, not the leaf predicate.
        HashSet<string>? hiddenSubtree = null;
        var hiddenStates = new HashSet<ItemState>();
        if (q.Open) { hiddenStates.Add(ItemState.Done); hiddenStates.Add(ItemState.Dismissed); }
        if (q.ExcludeStates is { Count: > 0 })
            foreach (var s in q.ExcludeStates) hiddenStates.Add(s);
        if (hiddenStates.Count > 0)
        {
            var hiddenRoots = graph.Items.Where(i => hiddenStates.Contains(i.State)).Select(i => i.Id);
            hiddenSubtree = graph.DescendantsOf(hiddenRoots);
        }

        // Resolve the milestone ref up front so ambiguity / not-found
        // errors surface instead of silently matching nothing.
        (string Project, string Slug)? milestone = null;
        if (!string.IsNullOrEmpty(q.Milestone))
        {
            var (mt, mErr) = MilestoneResolver.Resolve(repo.Store, repo.Root, null, q.Milestone);
            if (mt is null) return Result.BadRequest(mErr!);
            milestone = (mt.ProjectSlug, mt.Slug);
        }

        // Project containment scope: the set of items whose nearest
        // project-type ancestor points at this doc folder. Validated up
        // front (unknown slug 400s, never a silent empty result), then
        // matched by id-membership like the ready set.
        HashSet<string>? projectIds = null;
        if (!string.IsNullOrEmpty(q.Project))
        {
            if (!ProjectsLayout.ProjectSlugs(repo.Root).Contains(q.Project, StringComparer.Ordinal))
                return Result.BadRequest($"unknown project '{q.Project}' — no folder under .knit/projects/");
            projectIds = new HashSet<string>(
                graph.Items
                    .Where(i => string.Equals(
                        MilestoneResolver.ProjectScopeOf(repo.Store, i), q.Project, StringComparison.Ordinal))
                    .Select(i => i.Id),
                StringComparer.Ordinal);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);

        // Effective (inherited) slate per item — computed over the full
        // graph so ancestors outside an --under scope still contribute.
        IReadOnlyDictionary<string, string?>? effectiveSlates =
            q.Slate is not null || q.NoSlate ? SlateInheritance.Compute(graph.Items) : null;
        var effectiveMilestones = milestone is not null ? MilestoneInheritance.Compute(graph.Items) : null;

        var matchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in universe)
        {
            if (hiddenSubtree is not null && hiddenSubtree.Contains(i.Id)) continue;
            if (!Matches(i, q, graph, readyIds, myHandle, today, effectiveSlates, milestone, effectiveMilestones, projectIds)) continue;
            matchIds.Add(i.Id);
        }

        // The visible item set is just the matches (callers that need
        // ancestor context build it themselves via the graph + matchIds).
        var ordered = Sort(matchIds.Select(id => graph.Get(id)!).Where(i => i is not null), q, graph);
        return Result.Ok(ordered, matchIds);
    }

    private static bool Matches(
        Item i, ItemQuery q, LinkGraph graph,
        HashSet<string>? readyIds, string? myHandle, DateOnly today,
        IReadOnlyDictionary<string, string?>? effectiveSlates,
        (string Project, string Slug)? milestone,
        IReadOnlyDictionary<string, MilestoneInheritance.Effective>? effectiveMilestones,
        HashSet<string>? projectIds)
    {
        if (milestone is { } ms &&
            !(effectiveMilestones is not null && effectiveMilestones.TryGetValue(i.Id, out var em) && em.Is(ms.Project, ms.Slug)))
            return false;
        if (projectIds is not null && !projectIds.Contains(i.Id)) return false;
        if (effectiveSlates is not null)
        {
            var effective = effectiveSlates.GetValueOrDefault(i.Id);
            if (q.Slate is not null && !string.Equals(effective, q.Slate, StringComparison.Ordinal)) return false;
            if (q.NoSlate && !string.IsNullOrEmpty(effective)) return false;
        }
        // dueSoonDays is irrelevant to overdue classification (only DueSoon
        // needs the window), so 0 is fine here.
        if (q.Overdue && DueDates.Status(i.Due, i.State, today, 0) != DueStatus.Overdue) return false;
        if (q.DueBefore is { } lo && !(i.Due is { } d1 && d1 < lo)) return false;
        if (q.DueAfter is { } hi && !(i.Due is { } d2 && d2 > hi)) return false;
        if (q.MinPriority is { } min && (int)i.Priority < (int)min) return false;
        if (q.Tags is { Count: > 0 } && !q.Tags.All(t => i.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))) return false;
        if (q.Types is { Count: > 0 } && !q.Types.Contains(i.Type)) return false;
        if (q.ExcludeTypes is { Count: > 0 } && q.ExcludeTypes.Contains(i.Type)) return false;
        if (q.States is { Count: > 0 } && !q.States.Contains(i.State)) return false;
        if (q.ExcludeStates is { Count: > 0 } && q.ExcludeStates.Contains(i.State)) return false;
        if (q.Open && (i.State == ItemState.Done || i.State == ItemState.Dismissed)) return false;
        if (q.Blocked == BlockedFilter.Only && !i.IsBlocked) return false;
        if (q.Blocked == BlockedFilter.Hide && i.IsBlocked) return false;
        if (!string.IsNullOrEmpty(q.Assignee) &&
            !string.Equals(i.Assignee, q.Assignee, StringComparison.Ordinal)) return false;
        if (q.Mine && !string.Equals(i.Assignee, myHandle, StringComparison.Ordinal)) return false;
        if (q.Unassigned && !string.IsNullOrEmpty(i.Assignee)) return false;
        if (!string.IsNullOrEmpty(q.Query) &&
            !i.Title.Contains(q.Query!, StringComparison.OrdinalIgnoreCase)) return false;
        if (readyIds is not null && !readyIds.Contains(i.Id)) return false;
        if (q.Unparented)
        {
            // "Parent set, but unresolvable" — the orphan-triage case.
            if (string.IsNullOrEmpty(i.Parent)) return false;
            if (graph.Get(i.Parent!) is not null) return false;
        }
        return true;
    }

    private static IReadOnlyList<Item> Sort(IEnumerable<Item> items, ItemQuery q, LinkGraph graph)
    {
        IComparer<string?> rankCmp = StringComparer.Ordinal;
        var list = items.ToList();
        if (list.Count == 0) return list;

        switch (q.SortKey)
        {
            case SortKey.Tree:
                var order = TreeOrder.Index(graph, q.TreeSort);
                list.Sort((a, b) =>
                {
                    var ai = order.TryGetValue(a.Id, out var x) ? x : int.MaxValue;
                    var bi = order.TryGetValue(b.Id, out var y) ? y : int.MaxValue;
                    return ai.CompareTo(bi);
                });
                if (q.SortDir == SortDir.Desc) list.Reverse();
                return list;
            case SortKey.Rank:
                return Apply(list, (a, b) => rankCmp.Compare(a.Rank, b.Rank), q.SortDir);
            case SortKey.Type:
                return Apply(list, (a, b) =>
                {
                    var c = ((int)a.Type).CompareTo((int)b.Type);
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                }, q.SortDir);
            case SortKey.State:
                // Not Apply: desc is its own order (dismissed stays last, rank never reverses).
                var desc = q.SortDir == SortDir.Desc;
                return list.OrderBy(i => i, Comparer<Item>.Create((a, b) => SiblingOrder.CompareState(a, b, desc))).ToList();
            case SortKey.Assignee:
                return Apply(list, (a, b) =>
                {
                    var c = StringComparer.Ordinal.Compare(a.Assignee ?? "", b.Assignee ?? "");
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                }, q.SortDir);
            case SortKey.Created:
                return Apply(list, (a, b) =>
                {
                    var c = a.Created.CompareTo(b.Created);
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                }, q.SortDir);
            case SortKey.Due:
                // Nulls (undated) always last, both directions.
                list.Sort((a, b) =>
                {
                    if (a.Due is null && b.Due is null) return rankCmp.Compare(a.Rank, b.Rank);
                    if (a.Due is null) return 1;
                    if (b.Due is null) return -1;
                    var c = a.Due.Value.CompareTo(b.Due.Value);
                    if (q.SortDir == SortDir.Desc) c = -c;
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                return list;
            case SortKey.Priority:
                // Critical-first by default (Asc); rank tiebreak.
                list.Sort((a, b) =>
                {
                    var c = ((int)b.Priority).CompareTo((int)a.Priority);
                    if (q.SortDir == SortDir.Desc) c = -c;
                    return c != 0 ? c : rankCmp.Compare(a.Rank, b.Rank);
                });
                return list;
            default:
                return list;
        }
    }

    private static IReadOnlyList<Item> Apply(List<Item> list, Comparison<Item> cmp, SortDir dir)
    {
        list.Sort(cmp);
        if (dir == SortDir.Desc) list.Reverse();
        return list;
    }

    public static Item? TryResolveById(LinkGraph graph, string idOrPrefix)
    {
        if (graph.Get(idOrPrefix) is { } exact) return exact;
        var matches = graph.Items.Where(i => i.Id.StartsWith(idOrPrefix, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
