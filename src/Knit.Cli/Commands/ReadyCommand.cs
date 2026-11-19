using System.CommandLine;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Knit.Cli.Commands;

public static class ReadyCommand
{
    public static Command Build()
    {
        var cmd = new Command(
            "ready",
            "Inventory of items ready to work on: any open state (todo or "
            + "in-progress) with no `blocked` flag. `Blocked by` link rows don't "
            + "count — only the flag `knit block` sets does. Containers (a project, "
            + "an epic with open children) are listed too; for a single "
            + "recommendation of leaf work use `knit next`. Order: Rank ascending.");
        var mineOpt = new Option<bool>("--mine") { Description = "Only items assigned to me" };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Only items assigned to this handle — what someone else has to work with" };
        var underOpt = new Option<string?>("--under") { Description = "Restrict to the subtree rooted at this item id (or unique prefix)" };
        cmd.Options.Add(mineOpt);
        cmd.Options.Add(assigneeOpt);
        var slateOpt = ItemFilters.SlateOption();
        var noSlateOpt = ItemFilters.NoSlateOption();
        cmd.Options.Add(underOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(noSlateOpt);
        var watchOpt = Watch.Option();
        cmd.Options.Add(watchOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {
            if (Watch.Gate(parse, watchOpt) is int gateErr) return gateErr;
            var json = parse.GetValue(GlobalOptions.Json);

            if (parse.GetValue(mineOpt) && !string.IsNullOrEmpty(parse.GetValue(assigneeOpt)))
                return Out.Error("--mine cannot be combined with --assignee", ErrorCode.BadArgument, json);

            var under = parse.GetValue(underOpt);
            if (!string.IsNullOrEmpty(under))
            {
                var (root, err) = ItemUtils.Resolve(ctx, under);
                if (root is null) return Out.Error(err!, ErrorCode.NotFound, json);
            }

            var (slateFilter, slateErr, slateCode) = ItemFilters.ParseSlate(
                ctx.Root, parse.GetValue(slateOpt), parse.GetValue(noSlateOpt), ctx.Store.AllItems());
            if (slateFilter is null) return Out.Error(slateErr!, slateCode, json);

            List<Item> ComputeReady()
            {
                var graph = new LinkGraph(ctx.Store.AllItems());
                var slates = slateFilter.WithInheritance(graph.Items);
                var who = parse.GetValue(assigneeOpt);
                var assignee = parse.GetValue(mineOpt)
                    ? ctx.Config.Handle
                    : string.IsNullOrEmpty(who) ? null : who;
                HashSet<string>? scopeIds = null;
                if (!string.IsNullOrEmpty(under))
                {
                    var (root, _) = ItemUtils.Resolve(ctx, under);
                    if (root is not null)
                        scopeIds = new HashSet<string>(graph.Subtree(root.Id).Select(i => i.Id), StringComparer.Ordinal);
                }
                return graph.Ready(assignee)
                    .Where(i => scopeIds is null || scopeIds.Contains(i.Id))
                    .Where(slates.Matches)
                    .OrderBy(i => i.Rank, StringComparer.Ordinal)
                    .ToList();
            }

            if (json)
            {
                var ready = ComputeReady();
                Out.Json(new JsonItemList(ready
                        .Select(i => AgentMapper.Map(i, ctx, effectiveSlate: ctx.EffectiveSlate(i.Id)))
                        .ToList()),
                    AgentJsonContext.Default.JsonItemList);
                return 0;
            }

            IRenderable Render()
            {
                var ready = ComputeReady();
                if (ready.Count == 0) return new Markup("[dim](nothing ready)[/]");
                var rows = ready
                    .Select(i => (IRenderable)new Markup(
                        $"{i.Type.Icon()} [dim]{Out.Short(ctx, i.Id)}[/] {Markup.Escape(i.Title)}"))
                    .ToList();
                return new Rows(rows);
            }

            if (parse.GetValue(watchOpt))
                return Watch.Run(ctx, parse, Render);

            AnsiConsole.Write(Render());
            return 0;
        }, needsHandle: p => p.GetValue(mineOpt));
        return cmd;
    }
}
