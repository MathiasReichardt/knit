using Knit.Core.Domain;
using Knit.Core.Projects;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Queries;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Knit.WebApi.Endpoints;

public static class TimelineEndpoints
{
    public static void MapTimeline(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        // Mirrors `knit timeline --json`, minus the window: the SPA owns the
        // range selector / overdue back-extension client-side, so the server
        // returns every (filtered) node and lets the client trim.
        app.MapGet("/api/timeline", (HttpRequest req) =>
        {
            var (q, parseErr) = ItemQueryParser.Parse(req.Query);
            if (q is null) return Results.BadRequest(parseErr);

            TimelineDepth depth;
            var depthRaw = req.Query["depth"].ToString();
            switch (string.IsNullOrWhiteSpace(depthRaw) ? "all" : depthRaw.Trim().ToLowerInvariant())
            {
                case "all": depth = TimelineDepth.All; break;
                case "project": depth = TimelineDepth.Project; break;
                case "epic": depth = TimelineDepth.Epic; break;
                case "feature": depth = TimelineDepth.Feature; break;
                default: return Results.BadRequest($"unknown depth: {depthRaw} (expected project|epic|feature|all)");
            }
            var includeDoneRaw = req.Query["includeDone"].ToString().Trim().ToLowerInvariant();
            var includeDone = includeDoneRaw is "1" or "true" or "yes";
            var hideTodosRaw = req.Query["hideTodos"].ToString().Trim().ToLowerInvariant();
            var hideTodos = hideTodosRaw is "1" or "true" or "yes";
            var hideDisconnectedRaw = req.Query["hideDisconnected"].ToString().Trim().ToLowerInvariant();
            var hideDisconnected = hideDisconnectedRaw is "1" or "true" or "yes";

            var repo = new RepoServices(options);
            var graph = repo.Graph();
            // Risk always computes over the FULL graph — filters restrict what
            // is rendered, never the propagation (an out-of-filter blocker
            // must still endanger a rendered item).
            var milestonesOf = MilestoneInheritance.Compute(graph.Items);
            var milestoneDeadlines = MilestoneResolver.MilestoneDeadlines(repo.Root, repo.Yaml, milestonesOf);
            var risk = DeadlineRisk.Compute(graph, milestoneDeadlines);
            IEnumerable<TimelineNode> visible = TimelineRollup.Compute(graph, risk, depth, includeDone);

            // Hide-todos is a render filter, not a graph filter: a hidden todo
            // that endangers dated work still resurfaces as a ghost blocker.
            if (hideTodos)
                visible = visible.Where(n => n.Item.Type != ItemType.Todo);
            // Drop chain roots too fine for the selected rank — same
            // render-filter semantics as hide-todos; ghosts still resurface.
            if (hideDisconnected)
                visible = visible.Where(n => TimelineRollup.WithinDepth(n.Item.Type, depth));

            if (q.HasAnyMatchFilter || !string.IsNullOrEmpty(q.Under))
            {
                var result = ItemQueryRunner.Run(repo, graph, q);
                if (result.Error is not null)
                    return result.IsNotFound ? Results.NotFound(result.Error) : Results.BadRequest(result.Error);
                visible = visible.Where(n => result.MatchIds.Contains(n.Item.Id));
            }
            var rendered = visible.ToList();

            var renderedIds = new HashSet<string>(rendered.Select(n => n.Item.Id), StringComparer.Ordinal);
            var rolledIds = new HashSet<string>(rendered.SelectMany(n => n.RolledUpIds), StringComparer.Ordinal);
            var ghostIds = rendered
                .SelectMany(n => n.Risk.AtRiskBlockers)
                .Where(id => !renderedIds.Contains(id) && !rolledIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var shortener = repo.Shortener();
            var nodes = new List<TimelineNodeDto>(rendered.Count + ghostIds.Count);
            foreach (var n in rendered)
                nodes.Add(MapNode(graph, milestonesOf, n.Item, shortener, n.Risk, n.SpanStart, n.SpanEnd, n.SubtreeAtRisk, n.RolledUpIds, ghost: false));
            foreach (var id in ghostIds)
            {
                var item = graph.Get(id);
                if (item is null) continue;
                var r = risk.GetValueOrDefault(id) ?? new DeadlineRisk.ItemRisk(null, false, []);
                nodes.Add(MapNode(graph, milestonesOf, item, shortener, r, item.Due, item.Due, subtreeAtRisk: false, [], ghost: true));
            }

            var allIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.Ordinal);
            var edges = new List<TimelineEdgeDto>();
            foreach (var n in nodes)
                foreach (var target in graph.BlockedTargetsOf(n.Id))
                    if (allIds.Contains(target))
                        edges.Add(new TimelineEdgeDto(n.Id, target, n.AtRisk));

            // Overlay: dated milestones, narrowed by the milestone
            // *and* project filters — a project scope that didn't reach the
            // overlay showed other projects' roadmaps over this project's
            // work. Resolve the raw ref here so an ambiguous/unknown ref
            // returns the same 400 the node filter would (never a silent
            // empty overlay). No filter → every project's dated milestones.
            (string Project, string Slug)? only = null;
            if (!string.IsNullOrEmpty(q.Milestone))
            {
                var (mt, mErr) = MilestoneResolver.Resolve(repo.Store, repo.Root, null, q.Milestone);
                if (mt is null) return Results.BadRequest(mErr);
                only = (mt.ProjectSlug, mt.Slug);
            }
            var milestones = MilestoneResolver.DatedMilestones(repo.Root, repo.Yaml, only, q.Project)
                .Select(m => new TimelineMilestoneDto(
                    m.ProjectSlug, m.Slug, m.Title, DueDates.ToWire(m.Target!.Value), m.State.ToWire()))
                .ToList();

            return Results.Json(new TimelineDto(nodes, edges, milestones), ApiJsonContext.Default.TimelineDto);
        });
    }

    private static TimelineNodeDto MapNode(
        LinkGraph graph, IReadOnlyDictionary<string, MilestoneInheritance.Effective> milestonesOf, Item item, IdShortener shortener, DeadlineRisk.ItemRisk risk,
        DateOnly? spanStart, DateOnly? spanEnd, bool subtreeAtRisk,
        IReadOnlyList<string> rolledUp, bool ghost)
    {
        var remaining = TimelineRemaining.Compute(graph, item);
        return new TimelineNodeDto(
            Id: item.Id,
            ShortId: shortener.Short(item.Id),
            Title: item.Title,
            Type: item.Type.ToWire(),
            State: item.State.ToWire(),
            Due: item.Due is { } d ? DueDates.ToWire(d) : null,
            Priority: item.Priority.ToWire(),
            Effort: string.IsNullOrEmpty(item.Effort) ? null : item.Effort,
            Assignee: string.IsNullOrEmpty(item.Assignee) ? null : item.Assignee,
            Parent: string.IsNullOrEmpty(item.Parent) ? null : item.Parent,
            Blocked: item.BlockedSummary,
            Milestone: milestonesOf.GetValueOrDefault(item.Id)?.QualifiedSlug,
            ParentPath: TimelineRollup.ParentPath(graph, item),
            AtRisk: risk.AtRisk,
            ImpliedDeadline: risk.ImpliedDeadline is { } i ? DueDates.ToWire(i) : null,
            AtRiskBlockers: risk.AtRiskBlockers,
            SpanStart: spanStart is { } s ? DueDates.ToWire(s) : null,
            SpanEnd: spanEnd is { } e ? DueDates.ToWire(e) : null,
            SubtreeAtRisk: subtreeAtRisk,
            RolledUp: rolledUp
                .Select(graph.Get)
                .Where(r => r is not null)
                .Select(r => new TimelineRolledDto(
                    r!.Id, r.Title, r.Type.ToWire(),
                    r.Due is { } rd ? DueDates.ToWire(rd) : null))
                .ToList(),
            // Coarsest-first ancestor ids — the inverse of a per-node descendant
            // set, and O(depth) instead of O(subtree) on the wire. The canvas reads
            // "is under the selection" off it.
            AncestorIds: ItemContextResolver.ParentChain(graph, item)
                .Reverse()
                .Select(a => a.Id)
                .ToList(),
            Remaining: remaining.Rows
                .Select(r => new TimelineRemainingDto(
                    r.Item.Id,
                    shortener.Short(r.Item.Id),
                    r.Item.Title,
                    r.Item.Type.ToWire(),
                    r.Item.State.ToWire(),
                    r.Item.Due is { } rdue ? DueDates.ToWire(rdue) : null,
                    r.OpenBelow,
                    r.UndatedBelow,
                    r.Blocker))
                .ToList(),
            OpenBelow: remaining.OpenBelow,
            Ghost: ghost);
    }
}
