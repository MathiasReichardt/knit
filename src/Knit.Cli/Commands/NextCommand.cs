using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Domain;
using Knit.Data;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class NextCommand
{
    public static Command Build()
    {
        var cmd = new Command(
            "next",
            "Recommend one item to work on next. Considers leaf work only — a "
            + "project, or anything with open children, is a container you plan "
            + "with, not work you start (use `ready` for the full inventory). "
            + "Order: in-progress before untouched (finish what you started), "
            + "then overdue, then due-soon, then priority, then earlier due date, "
            + "then rank. Human output prints why the pick won.");
        var featureOpt = new Option<string?>("--feature") { Description = "Restrict to items tagged with this feature" };
        var mineOpt = new Option<bool>("--mine") { Description = "Only items assigned to me" };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Recommend for this handle instead — what someone else should pick up" };
        var underOpt = new Option<string?>("--under") { Description = "Restrict to the subtree rooted at this item id (or unique prefix)" };
        var slateOpt = ItemFilters.SlateOption();
        var noSlateOpt = ItemFilters.NoSlateOption();
        var idArg = new Argument<string?>("id") { Description = "Alias for --under; restricts to the subtree rooted at this item" };
        idArg.Arity = System.CommandLine.ArgumentArity.ZeroOrOne;
        cmd.Options.Add(featureOpt);
        cmd.Options.Add(mineOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(noSlateOpt);
        cmd.Arguments.Add(idArg);
        var watchOpt = Watch.Option();
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            var feature = parse.GetValue(featureOpt);
            var assigneeRaw = parse.GetValue(assigneeOpt);
            if (parse.GetValue(mineOpt) && !string.IsNullOrEmpty(assigneeRaw))
                return Out.Error("--mine cannot be combined with --assignee", ErrorCode.BadArgument, json);
            var assignee = parse.GetValue(mineOpt)
                ? ctx.Config.Handle
                : string.IsNullOrEmpty(assigneeRaw) ? null : assigneeRaw;
            var under = parse.GetValue(underOpt);
            var idArgValue = parse.GetValue(idArg);
            if (!string.IsNullOrEmpty(under) && !string.IsNullOrEmpty(idArgValue))
                return Out.Error("specify --under or the positional <id>, not both", ErrorCode.BadArgument, json);
            var subtreeRoot = under ?? idArgValue;
            if (!string.IsNullOrEmpty(feature) && !string.IsNullOrEmpty(subtreeRoot))
                return Out.Error("--feature and --under are mutually exclusive", ErrorCode.BadArgument, json);

            if (!string.IsNullOrEmpty(subtreeRoot))
            {
                var (rootItem, err) = ItemUtils.Resolve(ctx, subtreeRoot);
                if (rootItem is null) return Out.Error(err!, ErrorCode.NotFound, json);
            }

            var (slateFilter, slateErr, slateCode) = ItemFilters.ParseSlate(
                ctx.Root, parse.GetValue(slateOpt), parse.GetValue(noSlateOpt), ctx.Store.AllItems());
            if (slateFilter is null) return Out.Error(slateErr!, slateCode, json);

            Item? ComputeReady()
            {
                var items = ctx.Store.AllItems().ToList();
                var graph = new LinkGraph(items);
                IEnumerable<Item> scope = items;
                if (!string.IsNullOrEmpty(subtreeRoot))
                {
                    var (rootItem, _) = ItemUtils.Resolve(ctx, subtreeRoot);
                    if (rootItem is not null) scope = graph.Subtree(rootItem.Id);
                }
                else if (!string.IsNullOrEmpty(feature))
                {
                    scope = items.Where(i => i.Feature.Contains(feature, StringComparer.OrdinalIgnoreCase));
                }
                var slates = slateFilter.WithInheritance(items);
                var scopeIds = new HashSet<string>(scope.Where(slates.Matches).Select(i => i.Id), StringComparer.Ordinal);
                var candidates = graph.Ready(assignee)
                    .Where(i => scopeIds.Contains(i.Id))
                    .Where(i => NextPick.IsLeafWork(graph, i));
                return NextPick.Order(candidates, Today(), ctx.Config.DueSoonDays).FirstOrDefault();
            }

            if (json)
            {
                var ready = ComputeReady();
                if (ready is null) { Console.WriteLine("null"); return 0; }
                Out.EmitMutated(ctx, ready);
                return 0;
            }

            IRenderable Render()
            {
                var ready = ComputeReady();
                if (ready is null) return new Markup("[dim]nothing ready[/]");
                return new Rows(
                    new Markup(
                        $"{ready.Type.Icon()} [dim]{Markup.Escape(Out.Short(ctx, ready.Id))}[/] {Markup.Escape(ready.Title)}"),
                    new Markup(
                        $"  [dim]{Markup.Escape(NextPick.Reason(ready, Today(), ctx.Config.DueSoonDays))}[/]"));
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        }, needsHandle: p => p.GetValue(mineOpt));
        return cmd;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
}
