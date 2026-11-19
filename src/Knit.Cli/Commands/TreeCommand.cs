using System.CommandLine;
using System.Text.Json;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class TreeCommand
{
    public static Command Build()
    {
        var cmd = new Command("tree", "Show items as a hierarchy");
        var unparentedOpt = new Option<bool>("--unparented") { Description = "Only show items without a parent" };
        var underOpt = new Option<string?>("--under") { Description = "Scope to the subtree rooted at this item id (or unique prefix); the item itself is included" };
        var readyOpt = new Option<bool>("--ready") { Description = "Only show items ready to work on (any open state, no `blocked` flag). Non-matching ancestors are kept (dimmed) so structural context survives the filter; pass --flat to drop them." };
        var stateOpt = new Option<string[]>("--state") { Description = "Filter by state (repeatable). Non-matching ancestors are dimmed; pass --flat to promote matches up." };
        var typeOpt = new Option<string[]>("--type") { Description = "Filter by type (repeatable). Same dim-ancestor rule as --state." };
        var excludeStateOpt = new Option<string[]>("--exclude-state") { Description = "Hide items in these states (repeatable)." };
        var excludeTypeOpt = new Option<string[]>("--exclude-type") { Description = "Hide items of these types (repeatable)." };
        var openOpt = new Option<bool>("--open") { Description = "Shorthand for --exclude-state done --exclude-state dismissed. Mutually exclusive with --state / --exclude-state." };
        var queryOpt = new Option<string?>("-q", "--query") { Description = "Substring match on title (ancestor-context rule)." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Filter by assignee (ancestor-context rule)." };
        var mineOpt = new Option<bool>("--mine") { Description = "Only items assigned to me (ancestor-context rule)." };
        var flatOpt = new Option<bool>("--flat") { Description = "Drop non-matching ancestors and promote matches up (today's CLI behaviour). Useful for piping into grep/awk where ANSI dim is stripped." };
        var blockedOpt = new Option<bool>("--blocked") { Description = "Only items carrying the `blocked` flag (what `knit block` sets); a Blocked-by link row alone doesn't count. Blocked items are marked ⊘ in every tree (ancestor-context rule)." };
        var overdueOpt = new Option<bool>("--overdue") { Description = "Only open items whose due date is in the past (ancestor-context rule)." };
        var dueBeforeOpt = new Option<string?>("--due-before") { Description = "Only items due strictly before this date (YYYY-MM-DD). A raw date window — unlike --overdue it keeps done/dismissed items, so add --open for live work only." };
        var dueAfterOpt = new Option<string?>("--due-after") { Description = "Only items due strictly after this date (YYYY-MM-DD). A raw date window — unlike --overdue it keeps done/dismissed items, so add --open for live work only." };
        var minPriorityOpt = new Option<string?>("--min-priority") { Description = "Only items at or above this priority (someday|normal|important|critical); ancestor-context rule." };
        var tagOpt = new Option<string[]>("--tag") { Description = "Only items carrying this tag (repeatable; AND — item must carry all); ancestor-context rule." };
        var noTagOpt = new Option<string[]>("--no-tag") { Description = "Hide items carrying this tag (repeatable)." };
        var slateOpt = new Option<string?>("--slate") { Description = "Only items in this slate. Inherited: matches items whose nearest ancestor with a slate has it, unless the item sets its own (ancestor-context rule)." };
        var noSlateOpt = new Option<bool>("--no-slate") { Description = "Only items with no slate (own or inherited)." };
        var milestoneOpt = new Option<string?>("--milestone") { Description = "Only items linked to this milestone: <slug> (unambiguous across projects) or <project>/<slug>; ancestor-context rule." };
        var healthOpt = new Option<string[]>("--health") { Description = "Only items carrying this health signal (on-track|at-risk|off-track; repeatable, OR). Health lives on projects; items with no signal never match. Ancestor-context rule." };
        var sortOpt = new Option<string?>("--sort") { Description = "Order each sibling group by rank (the plan) or state (done → review → in-progress → todo → draft, dismissed last; rank within a state). Defaults to the tree_sort config key." };
        var watchOpt = Watch.Option();
        cmd.Options.Add(unparentedOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(readyOpt);
        cmd.Options.Add(stateOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(excludeStateOpt);
        cmd.Options.Add(excludeTypeOpt);
        cmd.Options.Add(openOpt);
        cmd.Options.Add(queryOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(mineOpt);
        cmd.Options.Add(flatOpt);
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
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            var unparentedShown = parse.GetValue(unparentedOpt);
            var under = parse.GetValue(underOpt);
            var readyOnly = parse.GetValue(readyOpt);
            var queryStr = parse.GetValue(queryOpt);
            var assignee = parse.GetValue(assigneeOpt);
            var mine = parse.GetValue(mineOpt);
            var flat = parse.GetValue(flatOpt);

            if (!string.IsNullOrEmpty(under) && unparentedShown)
                return Out.Error("--under cannot be combined with --unparented", ErrorCode.BadArgument, json);
            if (parse.GetValue(blockedOpt) && readyOnly)
                return Out.Error("--blocked cannot be combined with --ready — ready means unblocked", ErrorCode.BadArgument, json);
            var (treeSort, sortErr) = SiblingOrder.Resolve(parse.GetValue(sortOpt), ctx.Config.TreeSort);
            if (sortErr is not null) return Out.Error(sortErr, ErrorCode.BadArgument, json);
            var cmp = SiblingOrder.Comparer(treeSort);

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

            // Slate and milestone filters match on the effective (inherited)
            // value, which needs the loaded item set — attach the maps per load.
            ItemFilter ResolveFilter(IReadOnlyCollection<Item> allItems) => filter.WithInheritance(allItems);

            var graph = new LinkGraph(ctx.Store.AllItems());
            var matches = BuildPredicate(graph, readyOnly, ResolveFilter(graph.Items), queryStr, assignee, mine ? ctx.Config.Handle : null);

            Item? underItemResolved = null;
            if (!string.IsNullOrEmpty(under))
            {
                var (resolved, err) = ItemUtils.Resolve(ctx, under);
                if (resolved is null) return Out.Error(err!, ErrorCode.NotFound, json);
                underItemResolved = resolved;
            }

            // In context mode, build the include set = matches ∪ ancestors-of-matches.
            HashSet<string>? includeIds = null;
            if (matches is not null && !flat)
            {
                includeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var i in graph.Items)
                    if (matches(i)) MarkAncestorChain(i, graph, includeIds);
            }

            if (json && parse.GetValue(dumpOpt))
            {
                var rows = Walk(graph, matches, includeIds, flat, underItemResolved, unparentedShown, cmp);
                if (DumpOptions.Guard(rows.Select(r => r.Item).ToList(), parse.GetValue(dumpMaxOpt), json, NarrowHint) is int refusedJson)
                    return refusedJson;
                DumpOptions.Report(rows.Count, WriteJsonDump(ctx, graph, rows, matches, cmp));
                return 0;
            }

            if (json)
            {
                var graphJson = graph;
                if (underItemResolved is not null)
                {
                    if (matches is null)
                    {
                        Out.Json(new JsonTree([BuildJson(ctx, underItemResolved, graphJson, matches, includeIds, flat, cmp)], []), AgentJsonContext.Default.JsonTree);
                    }
                    else if (flat && !matches(underItemResolved))
                    {
                        var promotedRoots = PromotedMatchingDescendants(underItemResolved, graphJson, matches)
                            .OrderBy(i => i, cmp)
                            .Select(i => BuildJson(ctx, i, graphJson, matches, includeIds, flat, cmp))
                            .ToList();
                        Out.Json(new JsonTree(promotedRoots, []), AgentJsonContext.Default.JsonTree);
                    }
                    else
                    {
                        Out.Json(new JsonTree([BuildJson(ctx, underItemResolved, graphJson, matches, includeIds, flat, cmp)], []), AgentJsonContext.Default.JsonTree);
                    }
                    return 0;
                }
                var roots = RootsForOutput(graphJson, matches, includeIds, flat)
                    .OrderBy(p => p.item, cmp)
                    .Select(p => BuildJson(ctx, p.item, graphJson, matches, includeIds, flat, cmp))
                    .ToList();
                var unparented = graphJson.Items
                    .Where(i => !string.IsNullOrEmpty(i.Parent) && graphJson.Get(i.Parent!) is null)
                    .Where(i => matches is null || matches(i))
                    .OrderBy(i => i, cmp)
                    .Select(i => AgentMapper.Map(i, ctx, effectiveSlate: ctx.EffectiveSlate(i.Id),
                        heldBy: graphJson.HeldBy(i.Id)?.Id))
                    .ToList();
                Out.Json(new JsonTree(roots, unparented), AgentJsonContext.Default.JsonTree);
                return 0;
            }

            (LinkGraph Graph, List<TreeRow> Rows) Rows()
            {
                var g = new LinkGraph(ctx.Store.AllItems());
                var m = BuildPredicate(g, readyOnly, ResolveFilter(g.Items), queryStr, assignee, mine ? ctx.Config.Handle : null);
                HashSet<string>? include = null;
                if (m is not null && !flat)
                {
                    include = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var i in g.Items)
                        if (m(i)) MarkAncestorChain(i, g, include);
                }

                Item? underItem = null;
                if (!string.IsNullOrEmpty(under))
                {
                    var (resolved, _) = ItemUtils.Resolve(ctx, under);
                    underItem = resolved;
                }
                return (g, Walk(g, m, include, flat, underItem, unparentedShown, cmp));
            }

            IRenderable Render()
            {
                var (g, rows) = Rows();
                return BuildTree(ctx, g, rows);
            }

            if (parse.GetValue(dumpOpt))
            {
                if (parse.GetValue(watchOpt))
                    return Out.Error("--dump cannot be combined with --watch — a dump is a one-shot read", ErrorCode.BadArgument, json);
                var (dumpGraph, rows) = Rows();
                if (DumpOptions.Guard(rows.Select(r => r.Item).ToList(), parse.GetValue(dumpMaxOpt), json, NarrowHint) is int refused)
                    return refused;
                // The structure is the point of dumping a tree, so the tree
                // itself is printed first and the bodies follow in its order.
                AnsiConsole.Write(BuildTree(ctx, dumpGraph, rows));
                var sb = new System.Text.StringBuilder().AppendLine();
                foreach (var r in rows) ItemDump.Write(sb, ctx, r.Item, context: !r.IsMatch);
                DumpOptions.Emit(sb, rows.Count);
                return 0;
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        }, needsHandle: p => p.GetValue(mineOpt));
        return cmd;
    }

    private static Func<Item, bool>? BuildPredicate(
        LinkGraph graph, bool readyOnly, ItemFilter filter,
        string? query, string? assignee, string? mineHandle)
    {
        HashSet<string>? readyIds = readyOnly
            ? new HashSet<string>(graph.Ready().Select(i => i.Id), StringComparer.Ordinal)
            : null;
        // Subtree-hide: when ExcludeStates is set (e.g. --open drops
        // done/dismissed) propagate the exclusion to descendants.
        HashSet<string>? hiddenSubtree = null;
        if (filter.ExcludeStates is { Count: > 0 })
        {
            var hiddenRoots = graph.Items.Where(i => filter.ExcludeStates.Contains(i.State)).Select(i => i.Id);
            hiddenSubtree = graph.DescendantsOf(hiddenRoots);
        }
        if (readyIds is null && filter.IsEmpty
            && string.IsNullOrEmpty(query)
            && string.IsNullOrEmpty(assignee)
            && string.IsNullOrEmpty(mineHandle)
            && (hiddenSubtree is null || hiddenSubtree.Count == 0)) return null;
        return i =>
        {
            if (readyIds is not null && !readyIds.Contains(i.Id)) return false;
            if (!filter.IsEmpty && !filter.Matches(i)) return false;
            if (hiddenSubtree is not null && hiddenSubtree.Contains(i.Id)) return false;
            if (!string.IsNullOrEmpty(query) && !i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(assignee) && !string.Equals(i.Assignee, assignee, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(mineHandle) && !string.Equals(i.Assignee, mineHandle, StringComparison.Ordinal)) return false;
            return true;
        };
    }

    private static void MarkAncestorChain(Item item, LinkGraph graph, HashSet<string> acc)
    {
        var current = (Item?)item;
        while (current is not null && acc.Add(current.Id))
        {
            if (string.IsNullOrEmpty(current.Parent)) break;
            current = graph.Get(current.Parent!);
        }
    }

    /// <summary>
    /// <c>tree --json --dump</c>: the <c>JsonTree</c> envelope with the
    /// node payload swapped for the <c>show --json</c> shape, so structure and
    /// content arrive together.
    /// </summary>
    private static int WriteJsonDump(CliContext ctx, LinkGraph graph, List<TreeRow> rows, Func<Item, bool>? matches, IComparer<Item> cmp)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("roots");
            var i = 0;
            while (i < rows.Count) WriteJsonNode(w, ctx, rows, ref i, 0);
            w.WriteEndArray();
            w.WriteStartArray("unparented");
            foreach (var item in graph.Items
                .Where(it => !string.IsNullOrEmpty(it.Parent) && graph.Get(it.Parent!) is null)
                .Where(it => matches is null || matches(it))
                .OrderBy(it => it, cmp))
                ShowJson.WriteTo(w, item, ctx.Root, ctx.EffectiveSlate(item.Id), effectiveMilestone: ctx.EffectiveMilestone(item.Id));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var bytes = stream.ToArray();
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(bytes));
        return bytes.Length;
    }

    private static void WriteJsonNode(Utf8JsonWriter w, CliContext ctx, List<TreeRow> rows, ref int i, int depth)
    {
        var row = rows[i++];
        w.WriteStartObject();
        w.WritePropertyName("item");
        ShowJson.WriteTo(w, row.Item, ctx.Root, ctx.EffectiveSlate(row.Item.Id), effectiveMilestone: ctx.EffectiveMilestone(row.Item.Id));
        w.WriteBoolean("match", row.IsMatch);
        w.WriteStartArray("children");
        while (i < rows.Count && rows[i].Depth > depth) WriteJsonNode(w, ctx, rows, ref i, depth + 1);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private const string NarrowHint = "--under, --flat, --slate, --state";

    /// <summary>
    /// One rendered row, flattened with its depth. The tree's order is
    /// non-trivial (promotion, the dim-ancestor set, per-level rank sort), so
    /// the rendered tree and <c>--dump</c> read it from here rather than each
    /// walking the graph their own way and drifting apart.
    /// </summary>
    private sealed record TreeRow(Item Item, int Depth, bool Promoted, bool IsMatch);

    private static List<TreeRow> Walk(
        LinkGraph g, Func<Item, bool>? m, HashSet<string>? include, bool flat,
        Item? underItem, bool unparentedShown, IComparer<Item> cmp)
    {
        var rows = new List<TreeRow>();
        if (underItem is not null)
        {
            if (m is not null && flat && !m(underItem))
            {
                foreach (var promoted in PromotedMatchingDescendants(underItem, g, m)
                    .OrderBy(i => i, cmp))
                    Descend(rows, promoted, 0, promoted: true, isMatch: true, g, m, include, flat, cmp);
            }
            else
            {
                Descend(rows, underItem, 0, false, m is null || m(underItem), g, m, include, flat, cmp);
            }
            return rows;
        }

        if (unparentedShown)
        {
            foreach (var item in g.Items
                .Where(i => string.IsNullOrEmpty(i.Parent) || g.Get(i.Parent!) is null)
                .Where(i => m is null || m(i))
                .OrderBy(i => i, cmp))
                rows.Add(new TreeRow(item, 0, false, true));
            return rows;
        }

        foreach (var pair in RootsForOutput(g, m, include, flat)
            .OrderBy(p => p.item, cmp))
            Descend(rows, pair.item, 0, pair.promoted, m is null || m(pair.item), g, m, include, flat, cmp);
        return rows;
    }

    private static void Descend(
        List<TreeRow> rows, Item item, int depth, bool promoted, bool isMatch,
        LinkGraph g, Func<Item, bool>? m, HashSet<string>? include, bool flat, IComparer<Item> cmp)
    {
        rows.Add(new TreeRow(item, depth, promoted, isMatch));
        foreach (var (child, childPromoted, childMatch) in ResolveChildren(item, g, m, include, flat)
            .OrderBy(p => p.item, cmp))
            Descend(rows, child, depth + 1, childPromoted, childMatch, g, m, include, flat, cmp);
    }

    /// <summary>Rebuild the Spectre tree from the flattened rows — a row's
    /// depth is never more than one past its predecessor's, so the open node
    /// per level is all the state this needs.</summary>
    private static IRenderable BuildTree(CliContext ctx, LinkGraph graph, List<TreeRow> rows)
    {
        var tree = new Tree("[bold]knit[/]");
        var open = new List<TreeNode>();
        foreach (var r in rows)
        {
            var label = FormatItem(r.Item, ctx, graph, r.Promoted, r.IsMatch);
            var node = r.Depth == 0 ? tree.AddNode(label) : open[r.Depth - 1].AddNode(label);
            if (open.Count > r.Depth) open[r.Depth] = node;
            else open.Add(node);
        }
        return tree;
    }

    private static string FormatItem(Item i, CliContext ctx, LinkGraph graph, bool promoted, bool isMatch)
    {
        var prefix = promoted ? "[grey]↑[/] " : "";
        var due = DueFormat.Cell(i.Due, i.State, DateOnly.FromDateTime(DateTime.Now), ctx.Config.DueSoonDays);
        var dueSuffix = due.Length > 0 ? $" {due}" : "";
        var prio = PriorityFormat.TreeSuffix(i.Priority);
        var prioSuffix = prio.Length > 0 ? $" {Markup.Escape(prio)}" : "";
        var blocked = Out.BlockedMark(i, graph);
        var health = HealthFormat.Suffix(i.Health);
        var line = $"{prefix}{i.Type.Icon()} {i.State.Glyph()} [dim]{Markup.Escape(Out.Short(ctx, i.Id))}[/] {Markup.Escape(i.Title)}{blocked}{health}{prioSuffix}{dueSuffix}";
        return isMatch ? line : $"[dim]{line}[/]";
    }

    private static JsonTreeNode BuildJson(
        CliContext ctx, Item item, LinkGraph graph, Func<Item, bool>? matches,
        HashSet<string>? includeIds, bool flat, IComparer<Item> cmp)
    {
        var children = ResolveChildren(item, graph, matches, includeIds, flat)
            .OrderBy(p => p.item, cmp)
            .Select(p => BuildJson(ctx, p.item, graph, matches, includeIds, flat, cmp))
            .ToList();
        var isMatch = matches is null || matches(item);
        return new JsonTreeNode(
            AgentMapper.Map(item, ctx, effectiveSlate: ctx.EffectiveSlate(item.Id),
                heldBy: graph.HeldBy(item.Id)?.Id),
            children, Match: isMatch);
    }

    /// <summary>
    /// Direct children of <paramref name="parent"/> with their visibility
    /// + match annotations:
    /// - No filter → all children, match=true.
    /// - Flat mode → matches at this level, non-matches contribute their
    ///   matching descendants (promoted up).
    /// - Context mode → children in <paramref name="includeIds"/> (the
    ///   ancestor-chain set); non-matches render dim.
    /// </summary>
    private static IEnumerable<(Item item, bool promoted, bool isMatch)> ResolveChildren(
        Item parent, LinkGraph graph, Func<Item, bool>? matches,
        HashSet<string>? includeIds, bool flat)
    {
        if (matches is null)
        {
            foreach (var childId in graph.ChildrenOf(parent.Id))
            {
                var child = graph.Get(childId);
                if (child is null) continue;
                yield return (child, false, true);
            }
            yield break;
        }

        if (flat)
        {
            foreach (var childId in graph.ChildrenOf(parent.Id))
            {
                var child = graph.Get(childId);
                if (child is null) continue;
                if (matches(child)) { yield return (child, false, true); continue; }
                foreach (var promoted in PromotedMatchingDescendants(child, graph, matches))
                    yield return (promoted, true, true);
            }
            yield break;
        }

        foreach (var childId in graph.ChildrenOf(parent.Id))
        {
            var child = graph.Get(childId);
            if (child is null) continue;
            if (includeIds is null || !includeIds.Contains(child.Id)) continue;
            yield return (child, false, matches(child));
        }
    }

    private static IEnumerable<Item> PromotedMatchingDescendants(
        Item nonMatching, LinkGraph graph, Func<Item, bool> matches)
    {
        foreach (var grandChildId in graph.ChildrenOf(nonMatching.Id))
        {
            var grandChild = graph.Get(grandChildId);
            if (grandChild is null) continue;
            if (matches(grandChild))
                yield return grandChild;
            else
                foreach (var deeper in PromotedMatchingDescendants(grandChild, graph, matches))
                    yield return deeper;
        }
    }

    private static IEnumerable<(Item item, bool promoted)> RootsForOutput(
        LinkGraph graph, Func<Item, bool>? matches,
        HashSet<string>? includeIds, bool flat)
    {
        if (matches is null) return graph.Roots().Select(r => (r, false));
        if (flat)
        {
            return graph.Roots().SelectMany(r => matches(r)
                ? new[] { (r, false) }
                : PromotedMatchingDescendants(r, graph, matches).Select(p => (p, true)));
        }
        // Context mode: a root contributes if it's on the ancestor chain
        // of some match.
        return graph.Roots()
            .Where(r => includeIds is null || includeIds.Contains(r.Id))
            .Select(r => (r, false));
    }
}
