using System.CommandLine;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Cli.Output;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class PickCommand
{
    public static Command Build()
    {
        var description = """
            Interactively pick an item; the chosen full uuid is printed to stdout for shell substitution.

            Examples:
              knit start $(knit pick --state todo --mine)
              knit done  $(knit pick --under <epic-id>)
              knit show  $(knit pick -q oauth)

            Filters mirror `knit list`. The picker UI runs on stderr, so stdout
            stays clean for capture. Requires an interactive terminal (stderr
            must be a TTY); use `knit list --json` for non-interactive contexts.
            """;
        var cmd = new Command("pick", description);
        var queryOpt = new Option<string?>("-q", "--query") { Description = "Substring match on title" };
        var stateOpt = new Option<string?>("--state") { Description = "Filter by state" };
        var typeOpt = new Option<string?>("--type") { Description = "Filter by type" };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Filter by assignee" };
        var mineOpt = new Option<bool>("--mine") { Description = "Only items assigned to me" };
        var underOpt = new Option<string?>("--under") { Description = "Scope to a subtree (id or unique prefix)" };
        var slateOpt = ItemFilters.SlateOption();
        var noSlateOpt = ItemFilters.NoSlateOption();
        cmd.Options.Add(queryOpt);
        cmd.Options.Add(stateOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(mineOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(slateOpt);
        cmd.Options.Add(noSlateOpt);

        cmd.SetRepoAction((ctx, parse) =>
        {

            if (Console.IsErrorRedirected)
                return Out.Error(
                    "knit pick needs an interactive terminal (stderr is redirected); use 'knit list --json' for non-interactive contexts",
                    ErrorCode.BadArgument);

            var (sorted, error, code) = Candidates(ctx, new PickFilter(
                parse.GetValue(queryOpt), parse.GetValue(stateOpt), parse.GetValue(typeOpt),
                parse.GetValue(mineOpt) ? ctx.Config.Handle : parse.GetValue(assigneeOpt),
                parse.GetValue(underOpt), parse.GetValue(slateOpt), parse.GetValue(noSlateOpt)));
            if (sorted is null) return Out.Error(error!, code);
            if (sorted.Count == 0)
                return Out.Error("no items match the given filters", ErrorCode.NotFound);

            // UI to stderr — keeps stdout clean for `$(knit pick)` substitution.
            var ui = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(Console.Error),
            });

            var prompt = new SelectionPrompt<Item>()
                .Title("[bold]Pick an item[/]")
                .PageSize(Math.Min(20, Math.Max(5, sorted.Count)))
                .UseConverter(i =>
                    $"{i.Type.Icon()} {i.State.Glyph()} {Out.Short(ctx, i.Id)}  {Markup.Escape(i.Title)}");
            if (sorted.Count > 10) prompt.MoreChoicesText("[dim](move up/down for more)[/]");
            prompt.AddChoices(sorted);

            try
            {
                var chosen = ui.Prompt(prompt);
                Console.WriteLine(chosen.Id);
                return 0;
            }
            catch (Exception ex)
            {
                return Out.Error($"pick cancelled or failed: {ex.Message}", ErrorCode.BadArgument);
            }
        }, needsHandle: p => p.GetValue(mineOpt));
        return cmd;
    }

    /// <param name="Assignee">The handle to match — <c>--mine</c> resolves to the caller's.</param>
    public sealed record PickFilter(
        string? Query = null, string? State = null, string? Type = null, string? Assignee = null,
        string? Under = null, string? Slate = null, bool NoSlate = false);

    /// <summary>The items the picker offers, in rank order — the part of <c>pick</c> that needs no terminal.</summary>
    public static (List<Item>? Items, string? Error, ErrorCode Code) Candidates(CliContext ctx, PickFilter f)
    {
        var all = ctx.Store.AllItems().ToList();
        IEnumerable<Item> items = all;
        if (!string.IsNullOrEmpty(f.Under))
        {
            var (rootItem, err) = ItemUtils.Resolve(ctx, f.Under);
            if (rootItem is null) return (null, err, ErrorCode.NotFound);
            items = new LinkGraph(all).Subtree(rootItem.Id);
        }

        var (slates, slateErr, slateCode) = ItemFilters.ParseSlate(ctx.Root, f.Slate, f.NoSlate, all);
        if (slates is null) return (null, slateErr, slateCode);
        items = items.Where(slates.WithInheritance(all).Matches);

        if (!string.IsNullOrEmpty(f.Query))
            items = items.Where(i => i.Title.Contains(f.Query, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(f.State))
        {
            if (!EnumStrings.TryParseItemState(f.State, out var st)) return (null, $"unknown state: {f.State}", ErrorCode.BadArgument);
            items = items.Where(i => i.State == st);
        }
        if (!string.IsNullOrEmpty(f.Type))
        {
            if (!EnumStrings.TryParseItemType(f.Type, out var tp)) return (null, $"unknown type: {f.Type}", ErrorCode.BadArgument);
            items = items.Where(i => i.Type == tp);
        }
        if (!string.IsNullOrEmpty(f.Assignee))
            items = items.Where(i => string.Equals(i.Assignee, f.Assignee, StringComparison.Ordinal));

        return (items.OrderBy(i => i.Rank, StringComparer.Ordinal).ToList(), null, default);
    }
}
