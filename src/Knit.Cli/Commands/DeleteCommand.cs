using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// Permanently removes an item's file/folder from disk. Use <c>knit dismiss</c>
/// for the soft-delete (state=dismissed) equivalent. Back-references in other
/// items are NOT cleaned up — run <c>knit verify</c> afterwards to surface
/// broken links.
/// </summary>
public static class DeleteCommand
{
    public static Command Build()
    {
        var cmd = new Command("delete", "Permanently remove an item file (and its folder, for folder-form items). Use `knit dismiss` for soft-delete.");
        var idArg = new Argument<string>("id");
        var cascadeOpt = new Option<bool>("--cascade") { Description = "Also delete every descendant; required (or interactive confirm) when children exist" };
        var yesOpt = new Option<bool>("--yes") { Aliases = { "-y" }, Description = "Skip the confirmation prompt" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(cascadeOpt);
        cmd.Options.Add(yesOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var cascade = parse.GetValue(cascadeOpt);
            var yes = parse.GetValue(yesOpt);

            var graph = new LinkGraph(ctx.Store.AllItems());
            var descendants = graph.Subtree(item.Id)
                .Where(i => i.Id != item.Id)
                .ToList();

            if (descendants.Count > 0 && !cascade)
            {
                if (json || yes || Console.IsErrorRedirected)
                    return Out.Error(
                        $"item has {descendants.Count} descendant(s); pass --cascade to delete the whole subtree",
                        ErrorCode.BadArgument, json);
                var answer = AnsiConsole.Confirm(
                    $"This item has {descendants.Count} descendant(s). Delete them too?",
                    defaultValue: false);
                if (!answer) return Out.Error("aborted", ErrorCode.BadArgument, json);
                cascade = true;
            }

            if (!yes && !json && !Console.IsErrorRedirected)
            {
                var label = cascade && descendants.Count > 0
                    ? $"Delete {Out.Titled(ctx, item)} (and {descendants.Count} descendant(s))?"
                    : $"Delete {Out.Titled(ctx, item)}?";
                if (!AnsiConsole.Confirm(label, defaultValue: false))
                    return Out.Error("aborted", ErrorCode.BadArgument, json);
            }

            var cascadedIds = new List<string>();
            if (cascade)
            {
                foreach (var d in descendants)
                {
                    if (ctx.Store.Delete(d.Id)) cascadedIds.Add(d.Id);
                }
            }
            if (!ctx.Store.Delete(item.Id))
                return Out.Error("item file already gone", ErrorCode.NotFound, json);

            if (json)
            {
                Out.Json(new JsonDeleteResult(item.Id, cascadedIds), AgentJsonContext.Default.JsonDeleteResult);
                return 0;
            }
            AnsiConsole.MarkupLine($"[red]deleted[/] {Out.Titled(ctx, item)}");
            if (cascadedIds.Count > 0)
                AnsiConsole.MarkupLine($"[dim]cascaded {cascadedIds.Count} descendant(s)[/]");
            AnsiConsole.MarkupLine("[dim]back-references in other items are kept; run `knit verify` to find broken links[/]");
            return 0;
        });
        return cmd;
    }
}
