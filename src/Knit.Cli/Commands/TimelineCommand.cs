using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Projects;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class TimelineCommand
{
    public static Command Build()
    {
        var cmd = new Command("timeline", "Deadline agenda ordered by due date, with at-risk blocker analysis");
        var depthOpt = new Option<string?>("--depth") { Description = "Coarsest type shown: project|epic|feature|all (default all). Hidden descendants roll their due dates + risk into the nearest shown ancestor, or the chain root when none is shown; only a parentless item shows at its own level." };
        var rangeOpt = new Option<string?>("--range") { Description = "Window from today: week|month|3m|year|all (default month). The start extends back to the earliest overdue item so overdue work is always visible (done ones too, with --include-done)." };
        var fromOpt = new Option<string?>("--from") { Description = "Explicit window start (YYYY-MM-DD). Mutually exclusive with --range." };
        var toOpt = new Option<string?>("--to") { Description = "Explicit window end (YYYY-MM-DD). Mutually exclusive with --range." };
        var slateOpt = new Option<string?>("--slate") { Description = "Only items in this slate (effective/inherited, like list/tree)." };
        var milestoneOpt = new Option<string?>("--milestone") { Description = "Only items linked to this milestone: <slug> (unambiguous across projects) or <project>/<slug>." };
        var projectOpt = new Option<string?>("--project") { Description = "Only items in this project's tree (the project doc-folder slug). Unknown slug errors instead of an empty timeline." };
        var minPriorityOpt = new Option<string?>("--min-priority") { Description = "Only items at or above this priority (someday|normal|important|critical)." };
        var underOpt = new Option<string?>("--under") { Description = "Scope to the subtree rooted at this item id (or unique prefix)." };
        var includeDoneOpt = new Option<bool>("--include-done") { Description = "Include done/dismissed items (hidden by default; they are never at-risk either way)." };
        var hideTodosOpt = new Option<bool>("--hide-todos") { Description = "Hide todo-typed items (shown by default). A todo blocking dated work still surfaces as an at-risk blocker." };
        var hideDisconnectedOpt = new Option<bool>("--hide-disconnected") { Description = "Hide items whose parent chain never reaches the selected --depth rank (shown by default) — a clean 'real <rank> only' view. No effect at --depth all. A hidden at-risk blocker still resurfaces as a ghost." };
        cmd.Options.Add(depthOpt);
        cmd.Options.Add(rangeOpt);
        cmd.Options.Add(fromOpt);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(milestoneOpt);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(minPriorityOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(includeDoneOpt);
        cmd.Options.Add(hideTodosOpt);
        cmd.Options.Add(hideDisconnectedOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var depthRaw = parse.GetValue(depthOpt);
            TimelineDepth depth;
            switch (depthRaw?.ToLowerInvariant())
            {
                case null or "all": depth = TimelineDepth.All; break;
                case "project": depth = TimelineDepth.Project; break;
                case "epic": depth = TimelineDepth.Epic; break;
                case "feature": depth = TimelineDepth.Feature; break;
                default: return Out.Error($"unknown --depth '{depthRaw}' — expected project|epic|feature|all", ErrorCode.BadArgument, json);
            }

            var rangeRaw = parse.GetValue(rangeOpt);
            var fromRaw = parse.GetValue(fromOpt);
            var toRaw = parse.GetValue(toOpt);
            if (rangeRaw is not null && (fromRaw is not null || toRaw is not null))
                return Out.Error("--range cannot be combined with --from/--to", ErrorCode.BadArgument, json);

            var today = DateOnly.FromDateTime(DateTime.Now);
            DateOnly? explicitFrom = null, windowTo = null;
            if (fromRaw is not null || toRaw is not null)
            {
                if (fromRaw is not null)
                {
                    if (!DueDates.TryParse(fromRaw, out var f)) return Out.Error($"invalid --from date '{fromRaw}' — expected YYYY-MM-DD", ErrorCode.BadArgument, json);
                    explicitFrom = f;
                }
                if (toRaw is not null)
                {
                    if (!DueDates.TryParse(toRaw, out var t)) return Out.Error($"invalid --to date '{toRaw}' — expected YYYY-MM-DD", ErrorCode.BadArgument, json);
                    windowTo = t;
                }
                if (explicitFrom is { } ef && windowTo is { } wt && ef > wt)
                    return Out.Error("--from must not be after --to", ErrorCode.BadArgument, json);
            }
            else
            {
                switch (rangeRaw?.ToLowerInvariant())
                {
                    case "week": windowTo = today.AddDays(7); break;
                    case null or "month": windowTo = today.AddMonths(1); break;
                    case "3m": windowTo = today.AddMonths(3); break;
                    case "year": windowTo = today.AddYears(1); break;
                    case "all": windowTo = null; break;
                    default: return Out.Error($"unknown --range '{rangeRaw}' — expected week|month|3m|year|all", ErrorCode.BadArgument, json);
                }
            }

            (string Project, string Slug)? milestoneTarget = null;
            var milestoneRaw = parse.GetValue(milestoneOpt);
            if (!string.IsNullOrEmpty(milestoneRaw))
            {
                var (mt, mErr) = MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, milestoneRaw);
                if (mt is null) return Out.Error(mErr!, ErrorCode.BadArgument, json);
                milestoneTarget = (mt.ProjectSlug, mt.Slug);
            }

            var projectSlug = parse.GetValue(projectOpt);
            if (!string.IsNullOrEmpty(projectSlug) &&
                !ProjectsLayout.ProjectSlugs(ctx.Root).Contains(projectSlug, StringComparer.Ordinal))
                return Out.Error(
                    $"unknown project '{projectSlug}' — no folder under .knit/projects/", ErrorCode.BadArgument, json);

            var (filter, filterErr) = ItemFilters.Parse(
                null, null, null, null, open: false,
                dueSoonDays: ctx.Config.DueSoonDays,
                minPriority: parse.GetValue(minPriorityOpt),
                slate: parse.GetValue(slateOpt),
                milestone: milestoneTarget);
            if (filter is null) return Out.Error(filterErr!, ErrorCode.BadArgument, json);

            var under = parse.GetValue(underOpt);
            Item? underRoot = null;
            if (!string.IsNullOrEmpty(under))
            {
                var (root, err) = ItemUtils.Resolve(ctx, under);
                if (root is null) return Out.Error(err!, ErrorCode.NotFound, json);
                underRoot = root;
            }

            var allItems = ctx.Store.AllItems().ToList();
            var graph = new LinkGraph(allItems);
            // Risk always computes over the FULL graph — filters restrict what
            // is rendered, never the propagation (an out-of-filter blocker
            // must still endanger a rendered item).
            var milestonesOf = MilestoneInheritance.Compute(allItems);
            var milestoneDeadlines = MilestoneResolver.MilestoneDeadlines(ctx.Root, ctx.Yaml, milestonesOf);
            var risk = DeadlineRisk.Compute(graph, milestoneDeadlines);
            var includeDone = parse.GetValue(includeDoneOpt);
            IEnumerable<TimelineNode> visible = TimelineRollup.Compute(graph, risk, depth, includeDone);

            if (underRoot is not null)
            {
                var scope = new HashSet<string>(graph.Subtree(underRoot.Id).Select(i => i.Id), StringComparer.Ordinal);
                visible = visible.Where(n => scope.Contains(n.Item.Id));
            }
            if (!filter.IsEmpty)
            {
                var f = filter.WithInheritance(allItems);
                visible = visible.Where(n => f.Matches(n.Item));
            }
            // Project scope: item's nearest project-type ancestor points at
            // this doc folder (containment, not a link). Render filter — an
            // out-of-project at-risk blocker still resurfaces as a ghost.
            if (!string.IsNullOrEmpty(projectSlug))
                visible = visible.Where(n =>
                    string.Equals(MilestoneResolver.ProjectScopeOf(ctx.Store, n.Item), projectSlug, StringComparison.Ordinal));
            // Hide-todos is a render filter, not a graph filter: a hidden todo
            // that endangers dated work still resurfaces as a ghost blocker.
            if (parse.GetValue(hideTodosOpt))
                visible = visible.Where(n => n.Item.Type != ItemType.Todo);
            // Hide-disconnected: drop chain roots too fine for the selected
            // rank (they surface only because nothing coarser exists). Render
            // filter — a hidden at-risk blocker still ghosts back in.
            if (parse.GetValue(hideDisconnectedOpt))
                visible = visible.Where(n => TimelineRollup.WithinDepth(n.Item.Type, depth));
            var candidates = visible.ToList();

            // Overlay: dated milestones, narrowed by --milestone and
            // --project. Dropped milestones are the roadmap's dismissed, so
            // they follow --include-done.
            var milestoneDocs = MilestoneResolver
                .DatedMilestones(ctx.Root, ctx.Yaml, milestoneTarget, projectSlug)
                .Where(m => includeDone || m.State != MilestoneState.Dropped)
                .ToList();

            // Future-focused window start, extended back to the earliest
            // overdue item so overdue work is visible on load. Every candidate
            // counts, done ones included: `candidates` only holds done items
            // when --include-done asked for them, and extending for open work
            // alone would fetch them and then drop them behind the start.
            var windowFrom = explicitFrom ?? today;
            if (explicitFrom is null)
            {
                foreach (var n in candidates)
                {
                    if (n.SpanStart is { } s && s < windowFrom) windowFrom = s;
                }
                // A passed-but-unreached milestone widens the start the same
                // way overdue work does (mirrors the SPA's computeWindow).
                foreach (var m in milestoneDocs)
                {
                    if (m.State != MilestoneState.Planned) continue;
                    if (m.Target!.Value < windowFrom) windowFrom = m.Target.Value;
                }
            }

            var rendered = candidates
                .Where(n => n.SpanStart is null ||
                    (n.SpanEnd >= windowFrom && (windowTo is null || n.SpanStart <= windowTo)))
                .ToList();

            if (json)
            {
                // Unclipped on purpose: the SPA widens its own axis from these,
                // so clipping to [from, to] here would drop flags it needs.
                var milestones = milestoneDocs
                    .Select(m => new JsonTimelineMilestone(
                        m.ProjectSlug, m.Slug, m.Title, DueDates.ToWire(m.Target!.Value), m.State.ToWire()))
                    .ToList();
                Out.Json(BuildJson(graph, milestonesOf, risk, rendered, windowFrom, windowTo, milestones),
                    AgentJsonContext.Default.JsonTimeline);
                return 0;
            }

            var overlay = milestoneDocs
                .Where(m => m.Target!.Value >= windowFrom && (windowTo is null || m.Target.Value <= windowTo))
                .Select(m => new MilestoneRow(m, OpenLinkedCount(allItems, milestonesOf, m.QualifiedSlug)))
                .ToList();
            RenderAgenda(ctx, graph, rendered, overlay, today, windowFrom, windowTo);
            return 0;
        });
        return cmd;
    }

    private static JsonTimeline BuildJson(
        LinkGraph graph,
        IReadOnlyDictionary<string, MilestoneInheritance.Effective> milestonesOf,
        IReadOnlyDictionary<string, DeadlineRisk.ItemRisk> risk,
        IReadOnlyList<TimelineNode> rendered,
        DateOnly from,
        DateOnly? to,
        IReadOnlyList<JsonTimelineMilestone> milestones)
    {
        var renderedIds = new HashSet<string>(rendered.Select(n => n.Item.Id), StringComparer.Ordinal);
        var rolledIds = new HashSet<string>(rendered.SelectMany(n => n.RolledUpIds), StringComparer.Ordinal);

        // Out-of-filter at-risk blockers surface as ghost nodes so the JSON
        // graph never silently drops the at-risk signal. Rolled-up ids are
        // already accounted for via their anchor's subtree_at_risk.
        var ghostIds = rendered
            .SelectMany(n => n.Risk.AtRiskBlockers)
            .Where(id => !renderedIds.Contains(id) && !rolledIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var nodes = new List<JsonTimelineNode>(rendered.Count + ghostIds.Count);
        foreach (var n in rendered)
            nodes.Add(MapNode(graph, milestonesOf, n.Item, n.Risk, n.SpanStart, n.SpanEnd, n.SubtreeAtRisk, n.RolledUpIds, ghost: false));
        foreach (var id in ghostIds)
        {
            var item = graph.Get(id);
            if (item is null) continue;
            var r = risk.GetValueOrDefault(id) ?? new DeadlineRisk.ItemRisk(null, false, []);
            nodes.Add(MapNode(graph, milestonesOf, item, r, item.Due, item.Due, subtreeAtRisk: false, [], ghost: true));
        }

        var allIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.Ordinal);
        var edges = new List<JsonTimelineEdge>();
        foreach (var n in nodes)
        {
            foreach (var target in graph.BlockedTargetsOf(n.Id))
                if (allIds.Contains(target))
                    edges.Add(new JsonTimelineEdge(n.Id, target, "blocks", n.AtRisk));
        }

        return new JsonTimeline(
            DueDates.ToWire(from),
            to is { } t ? DueDates.ToWire(t) : null,
            nodes, edges, milestones);
    }

    private static JsonTimelineNode MapNode(
        LinkGraph graph, IReadOnlyDictionary<string, MilestoneInheritance.Effective> milestonesOf, Item item, DeadlineRisk.ItemRisk risk,
        DateOnly? spanStart, DateOnly? spanEnd, bool subtreeAtRisk,
        IReadOnlyList<string> rolledUp, bool ghost) => new(
        Id: item.Id,
        Title: item.Title,
        Type: item.Type.ToWire(),
        State: item.State.ToWire(),
        Due: item.Due is { } d ? DueDates.ToWire(d) : null,
        Priority: item.Priority.ToWire(),
        Effort: string.IsNullOrEmpty(item.Effort) ? null : item.Effort,
        Assignee: string.IsNullOrEmpty(item.Assignee) ? null : item.Assignee,
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
            .Select(r => new JsonTimelineRolled(
                r!.Id, r.Title, r.Type.ToWire(),
                r.Due is { } rd ? DueDates.ToWire(rd) : null))
            .ToList(),
        Ghost: ghost);

    /// <summary>A dated milestone on the agenda, with the number of still-open
    /// items in it (own link or inherited) — what makes a passed target actionable.</summary>
    private sealed record MilestoneRow(MilestoneDoc Doc, int OpenItems);

    private static int OpenLinkedCount(IEnumerable<Item> items, IReadOnlyDictionary<string, MilestoneInheritance.Effective> milestonesOf, string qualifiedSlug) =>
        items.Count(i =>
            i.State is not (ItemState.Done or ItemState.Dismissed) &&
            string.Equals(milestonesOf.GetValueOrDefault(i.Id)?.QualifiedSlug, qualifiedSlug, StringComparison.Ordinal));

    private static void RenderAgenda(
        CliContext ctx,
        LinkGraph graph,
        IReadOnlyList<TimelineNode> rendered,
        IReadOnlyList<MilestoneRow> milestones,
        DateOnly today,
        DateOnly from,
        DateOnly? to)
    {
        if (rendered.Count == 0 && milestones.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim](no items on the timeline)[/]");
            return;
        }

        var dated = rendered.Where(n => n.SpanStart is not null).ToList();
        var undated = rendered.Where(n => n.SpanStart is null).ToList();

        // Day groups for windows up to ~5 weeks, week groups beyond.
        var byWeek = to is null || to.Value.DayNumber - from.DayNumber > 35;
        var itemGroups = dated.GroupBy(n => GroupKey(n.SpanStart!.Value, byWeek))
            .ToDictionary(g => g.Key, g => g.ToList());
        var milestoneGroups = milestones.GroupBy(m => GroupKey(m.Doc.Target!.Value, byWeek))
            .ToDictionary(g => g.Key, g => g.ToList());

        var first = true;
        foreach (var key in itemGroups.Keys.Union(milestoneGroups.Keys).Order())
        {
            if (!first) AnsiConsole.WriteLine();
            first = false;
            AnsiConsole.MarkupLine(byWeek
                ? $"[bold]Week of {key:yyyy-MM-dd}[/]"
                : $"[bold]{key:ddd yyyy-MM-dd}[/]");
            // Milestones lead their group — the deadline, then the work that
            // has to land by it.
            foreach (var m in milestoneGroups.GetValueOrDefault(key, [])) RenderMilestone(m, today);
            foreach (var n in itemGroups.GetValueOrDefault(key, [])) RenderNode(ctx, graph, n, today);
        }

        if (undated.Count > 0)
        {
            if (!first) AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold](undated)[/]");
            foreach (var n in undated) RenderNode(ctx, graph, n, today);
        }
    }

    private static void RenderMilestone(MilestoneRow row, DateOnly today)
    {
        var m = row.Doc;
        var parts = new List<string>
        {
            " ",
            m.State == MilestoneState.Reached ? "[dim]✓[/]" : "◆",
            $"[bold]{Markup.Escape(m.QualifiedSlug)}[/]",
            Markup.Escape(m.Title),
        };
        if (m.State == MilestoneState.Dropped) parts.Add("[dim]dropped[/]");
        else if (m.State == MilestoneState.Reached) parts.Add("[dim]reached[/]");
        else if (m.Target!.Value < today) parts.Add("[red]⚠ target passed[/]");
        if (row.OpenItems > 0) parts.Add($"[dim]{row.OpenItems} open[/]");
        AnsiConsole.MarkupLine(string.Join(" ", parts));
    }

    private static DateOnly GroupKey(DateOnly date, bool byWeek) =>
        byWeek ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : date; // Monday week start

    private static void RenderNode(CliContext ctx, LinkGraph graph, TimelineNode n, DateOnly today)
    {
        var i = n.Item;
        var parts = new List<string>
        {
            " ",
            i.State.Glyph(),
            Markup.Escape(Out.Short(ctx, i.Id)),
            Markup.Escape(i.Title),
        };
        var prio = PriorityFormat.TreeSuffix(i.Priority);
        if (prio.Length > 0) parts.Add(prio);
        if (!string.IsNullOrEmpty(i.Effort)) parts.Add($"[dim]{Markup.Escape(i.Effort)}[/]");
        // "Who's on it — and is anyone?": an unassigned at-risk item is the
        // actionable crisis and must read differently from a handled one.
        if (!string.IsNullOrEmpty(i.Assignee)) parts.Add($"[dim]@{Markup.Escape(i.Assignee)}[/]");
        else if (n.Risk.AtRisk || n.Risk.Endangered) parts.Add("[red]unassigned![/]");
        if (Proximity(i.Due, i.State, today, ctx.Config.DueSoonDays) is { } prox) parts.Add(prox);
        // Live blocked flag: a currently blocked dated item is the
        // highest-signal thing the agenda can show.
        if (i.BlockedSummary is { } blocked) parts.Add($"[red]⊘ {Markup.Escape(blocked)}[/]");
        if (n.SpanStart is { } s && n.SpanEnd is { } e && (e != s || i.Due != s))
            parts.Add($"[dim]spans {DueDates.ToWire(s)} → {DueDates.ToWire(e)} ({n.RolledUpIds.Count} rolled up)[/]");
        if (n.Risk.AtRisk)
            parts.Add(n.Risk.ImpliedDeadline is { } d
                ? $"[red]⚠ at risk — needed by {DueDates.ToWire(d)}[/]"
                : "[red]⚠ at risk[/]");
        if (n.SubtreeAtRisk) parts.Add("[red]⚠ subtree at risk[/]");
        AnsiConsole.MarkupLine(string.Join(" ", parts));

        foreach (var blockerId in n.Risk.AtRiskBlockers)
        {
            var b = graph.Get(blockerId);
            var reason = b?.Due is { } bd ? $"due {DueDates.ToWire(bd)}" : "undated";
            if (b is not null && string.IsNullOrEmpty(b.Assignee)) reason += ", unassigned";
            AnsiConsole.MarkupLine($"      [red]⚠ blocked by {Out.Titled(ctx, blockerId, b?.Title)} ({reason})[/]");
        }
    }

    private static string? Proximity(DateOnly? due, ItemState state, DateOnly today, int dueSoonDays)
    {
        if (due is not { } d) return null;
        return DueDates.Status(due, state, today, dueSoonDays) switch
        {
            DueStatus.Overdue => $"[red]⚠ {today.DayNumber - d.DayNumber}d overdue[/]",
            DueStatus.DueSoon => $"[yellow]⚠ due in {d.DayNumber - today.DayNumber}d[/]",
            _ => null,
        };
    }
}
