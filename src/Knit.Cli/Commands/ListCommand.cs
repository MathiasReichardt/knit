using System.CommandLine;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class ListCommand
{
    public static Command Build()
    {
        var cmd = new Command("list", "List items with filters");
        var queryOpt = new Option<string?>("-q", "--query") { Description = "Substring match on title" };
        var stateOpt = new Option<string[]>("--state") { Description = "Filter by state (repeatable; OR within flag)" };
        var typeOpt = new Option<string[]>("--type") { Description = "Filter by type (repeatable; OR within flag)" };
        var excludeStateOpt = new Option<string[]>("--exclude-state") { Description = "Hide items in these states (repeatable)" };
        var excludeTypeOpt = new Option<string[]>("--exclude-type") { Description = "Hide items of these types (repeatable)" };
        var openOpt = new Option<bool>("--open") { Description = "Shorthand for --exclude-state done --exclude-state dismissed. Mutually exclusive with --state / --exclude-state." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Filter by assignee" };
        var mineOpt = new Option<bool>("--mine") { Description = "Only items assigned to me" };
        var underOpt = new Option<string?>("--under") { Description = "Scope to the subtree rooted at this item id (or unique prefix); includes the item itself and all transitive descendants" };
        var readyOpt = new Option<bool>("--ready") { Description = "Only items ready to work on (any open state, no `blocked` flag); same predicate as `knit ready`. Pair with `--under <epic>` to see what's actionable in one branch — finish the current branch before starting a new group." };
        var blockedOpt = new Option<bool>("--blocked") { Description = "Only items carrying the `blocked` flag (what `knit block` sets); a Blocked-by link row alone doesn't count. The state column marks them with ⊘ — run `knit show <id>` for the reason." };
        var overdueOpt = new Option<bool>("--overdue") { Description = "Only open items whose due date is in the past." };
        var dueBeforeOpt = new Option<string?>("--due-before") { Description = "Only items due strictly before this date (YYYY-MM-DD). A raw date window — unlike --overdue it keeps done/dismissed items, so add --open for live work only." };
        var dueAfterOpt = new Option<string?>("--due-after") { Description = "Only items due strictly after this date (YYYY-MM-DD). A raw date window — unlike --overdue it keeps done/dismissed items, so add --open for live work only." };
        var minPriorityOpt = new Option<string?>("--min-priority") { Description = "Only items at or above this priority (someday|normal|important|critical). E.g. --min-priority important shows important + critical." };
        var tagOpt = new Option<string[]>("--tag") { Description = "Only items carrying this tag (repeatable; AND — item must carry all)." };
        var noTagOpt = new Option<string[]>("--no-tag") { Description = "Hide items carrying this tag (repeatable)." };
        var slateOpt = ItemFilters.SlateOption();
        var noSlateOpt = ItemFilters.NoSlateOption();
        var milestoneOpt = new Option<string?>("--milestone") { Description = "Only items linked to this milestone: <slug> (unambiguous across projects) or <project>/<slug>." };
        var healthOpt = new Option<string[]>("--health") { Description = "Only items carrying this health signal (on-track|at-risk|off-track; repeatable, OR). Health lives on projects; items with no signal never match." };
        var sortOpt = new Option<string?>("--sort") { Description = "Sort by <key>[:asc|desc]. Keys: tree (hierarchy order, siblings per tree_sort), rank (default), type, state (done → review → in-progress → todo → draft; dismissed last both ways), assignee, created, due (nulls last), priority (critical-first)." };
        cmd.Options.Add(queryOpt);
        cmd.Options.Add(stateOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(excludeStateOpt);
        cmd.Options.Add(excludeTypeOpt);
        cmd.Options.Add(openOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(mineOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(readyOpt);
        cmd.Options.Add(blockedOpt);
        cmd.Options.Add(overdueOpt);
        cmd.Options.Add(dueBeforeOpt);
        cmd.Options.Add(dueAfterOpt);
        cmd.Options.Add(minPriorityOpt);
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(noTagOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(noSlateOpt);
        cmd.Options.Add(milestoneOpt);
        cmd.Options.Add(healthOpt);
        cmd.Options.Add(sortOpt);
        var dumpOpt = DumpOptions.Dump();
        var dumpMaxOpt = DumpOptions.Max();
        cmd.Options.Add(dumpOpt);
        cmd.Options.Add(dumpMaxOpt);
        var watchOpt = Watch.Option();
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            if (parse.GetValue(blockedOpt) && parse.GetValue(readyOpt))
                return Out.Error("--blocked cannot be combined with --ready — ready means unblocked", ErrorCode.BadArgument, json);

            (string Project, string Slug)? milestoneTarget = null;
            var milestoneRaw = parse.GetValue(milestoneOpt);
            if (!string.IsNullOrEmpty(milestoneRaw))
            {
                var (mt, mErr) = MilestoneResolver.Resolve(ctx.Store, ctx.Root, null, milestoneRaw);
                if (mt is null) return Out.Error(mErr!, ErrorCode.BadArgument, json);
                milestoneTarget = (mt.ProjectSlug, mt.Slug);
            }

            var (filter, filterErr) = ItemFilters.Parse(
                parse.GetValue(typeOpt),
                parse.GetValue(excludeTypeOpt),
                parse.GetValue(stateOpt),
                parse.GetValue(excludeStateOpt),
                parse.GetValue(openOpt),
                parse.GetValue(overdueOpt),
                parse.GetValue(dueBeforeOpt),
                parse.GetValue(dueAfterOpt),
                ctx.Config.DueSoonDays,
                parse.GetValue(minPriorityOpt),
                parse.GetValue(tagOpt),
                parse.GetValue(noTagOpt),
                parse.GetValue(slateOpt),
                parse.GetValue(noSlateOpt),
                milestoneTarget,
                parse.GetValue(blockedOpt),
                parse.GetValue(healthOpt));
            if (filter is null) return Out.Error(filterErr!, ErrorCode.BadArgument, json);
            if (ItemFilters.UnknownSlate(ctx.Root, filter.Slate, ctx.Store.AllItems()) is { } slateErr)
                return Out.Error(slateErr, ErrorCode.NotFound, json);

            var under = parse.GetValue(underOpt);
            if (!string.IsNullOrEmpty(under))
            {
                var (root, err) = ItemUtils.Resolve(ctx, under);
                if (root is null) return Out.Error(err!, ErrorCode.NotFound, json);
            }

            var (sortKey, sortDir, sortErr) = ItemSort.Parse(parse.GetValue(sortOpt), defaultKey: SortKey.Rank);
            if (sortErr is not null) return Out.Error(sortErr, ErrorCode.BadArgument, json);
            var treeSort = TreeSort.Rank;
            if (sortKey == SortKey.Tree)
            {
                (treeSort, var treeSortErr) = SiblingOrder.Resolve(null, ctx.Config.TreeSort);
                if (treeSortErr is not null) return Out.Error(treeSortErr, ErrorCode.BadArgument, json);
            }

            // Slate and milestone filters match on the effective (inherited)
            // value, which needs the loaded item set — attach the maps per load.
            ItemFilter ResolveFilter(IReadOnlyCollection<Item> allItems) => filter.WithInheritance(allItems);

            (List<Item> Items, LinkGraph Graph) ComputeSorted()
            {
                var allItems = ctx.Store.AllItems().ToList();
                IEnumerable<Item> items = allItems;
                var graph = new LinkGraph(allItems);
                var f = ResolveFilter(allItems);
                if (!string.IsNullOrEmpty(under))
                {
                    var (root, _) = ItemUtils.Resolve(ctx, under);
                    if (root is not null) items = graph.Subtree(root.Id);
                }
                if (parse.GetValue(readyOpt))
                {
                    var readyIds = new HashSet<string>(graph.Ready().Select(i => i.Id), StringComparer.Ordinal);
                    items = items.Where(i => readyIds.Contains(i.Id));
                }
                var q = parse.GetValue(queryOpt);
                if (!string.IsNullOrEmpty(q))
                    items = items.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (!f.IsEmpty)
                {
                    items = items.Where(f.Matches);
                    // Subtree-hide: when a parent is excluded by state
                    // (e.g. --open drops done/dismissed), drop its
                    // descendants too regardless of their own state.
                    if (f.ExcludeStates is { Count: > 0 })
                    {
                        var hiddenRoots = allItems.Where(i => f.ExcludeStates.Contains(i.State)).Select(i => i.Id);
                        var hiddenDescendants = graph.DescendantsOf(hiddenRoots);
                        items = items.Where(i => !hiddenDescendants.Contains(i.Id));
                    }
                }
                var assignee = parse.GetValue(assigneeOpt);
                if (!string.IsNullOrEmpty(assignee))
                    items = items.Where(i => string.Equals(i.Assignee, assignee, StringComparison.Ordinal));
                if (parse.GetValue(mineOpt))
                    items = items.Where(i => string.Equals(i.Assignee, ctx.Config.Handle, StringComparison.Ordinal));
                return (ItemSort.Apply(items, sortKey, sortDir, graph, treeSort).ToList(), graph);
            }

            var dump = parse.GetValue(dumpOpt);
            var dumpMax = parse.GetValue(dumpMaxOpt);
            if (dump && parse.GetValue(watchOpt))
                return Out.Error("--dump cannot be combined with --watch — a dump is a one-shot read", ErrorCode.BadArgument, json);

            if (json)
            {
                var (sorted, graph) = ComputeSorted();
                if (dump)
                {
                    if (DumpOptions.Guard(sorted, dumpMax, json, NarrowHint) is int refused) return refused;
                    DumpOptions.Report(sorted.Count, ItemDump.WriteJsonArray(ctx, sorted));
                    return 0;
                }
                Out.Json(new JsonItemList(sorted
                        .Select(i => AgentMapper.Map(i, ctx, effectiveSlate: ctx.EffectiveSlate(i.Id),
                            heldBy: graph.HeldBy(i.Id)?.Id))
                        .ToList()),
                    AgentJsonContext.Default.JsonItemList);
                return 0;
            }

            if (dump)
            {
                var (sorted, graph) = ComputeSorted();
                if (DumpOptions.Guard(sorted, dumpMax, json, NarrowHint) is int refused) return refused;
                // The index before the contents: an agent wants to see the
                // shape of the slice before reading it.
                AnsiConsole.Write(RenderTable(ctx, sorted, graph));
                var sb = new System.Text.StringBuilder().AppendLine();
                foreach (var i in sorted) ItemDump.Write(sb, ctx, i);
                DumpOptions.Emit(sb, sorted.Count);
                return 0;
            }

            IRenderable Render()
            {
                var (sorted, graph) = ComputeSorted();
                return RenderTable(ctx, sorted, graph);
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        }, needsHandle: p => p.GetValue(mineOpt));
        return cmd;
    }

    private const string NarrowHint = "--under, --slate, --state, --min-priority";

    private static IRenderable RenderTable(CliContext ctx, List<Item> sorted, LinkGraph graph)
    {
        if (sorted.Count == 0) return new Markup("[dim](no items)[/]");
        var today = DateOnly.FromDateTime(DateTime.Now);
        var window = ctx.Config.DueSoonDays;
        var t = new Table().Border(TableBorder.Rounded);
        // The blocked mark and health ride in the state cell: the reason
        // lives in `show`, and health only lands on projects, so a column of
        // either would be empty on nearly every row.
        t.AddColumns("id", "type", "state", "priority", "title", "assignee", "due");
        foreach (var i in sorted)
        {
            t.AddRow(
                new Markup(Out.Short(ctx, i.Id)),
                new Markup($"{i.Type.Icon()} {i.Type.ToWire()}"),
                new Markup($"{i.State.Glyph()} {i.State.ToWire()}{Out.BlockedMark(i, graph)}{HealthFormat.Suffix(i.Health)}"),
                new Markup(Markup.Escape(PriorityFormat.Cell(i.Priority))),
                Out.Prose(i.Title),
                Out.Prose(i.Assignee),
                new Markup(DueFormat.Cell(i.Due, i.State, today, window)));
        }
        return t;
    }
}
