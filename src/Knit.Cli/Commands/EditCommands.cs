using System.CommandLine;
using System.Diagnostics;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Kb;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class EditCommands
{
    public static Command Set()
    {
        var cmd = new Command("set", "Set a frontmatter field by name (allowed: title, assignee, effort, due, priority, health, rank, agent_skills, tags, state, blocked, design, project, slate)");
        var idArg = new Argument<string>("id");
        var assignmentArg = new Argument<string>("assignment") { Description = "field=value. agent_skills / tags take a comma-separated list (replace); empty value clears the field." };
        var forceOpt = new Option<bool>("--force") { Description = "Allow slate=<name> to forward-reference a slate not (yet) in the registry." };
        cmd.Arguments.Add(idArg);
        cmd.Arguments.Add(assignmentArg);
        cmd.Options.Add(forceOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var assignment = parse.GetValue(assignmentArg)!;
            var eq = assignment.IndexOf('=');
            if (eq <= 0) return Out.Error("expected field=value", ErrorCode.BadArgument, json);
            var field = assignment[..eq].Trim();
            var value = assignment[(eq + 1)..].Trim();

            var result = FieldSet.Apply(ctx, item, field, value, parse.GetValue(forceOpt));
            if (result.Error is not null) return Out.Error(result.Error, result.Code, json);
            var updated = result.Item!;
            ItemUtils.Save(ctx, updated);
            var dependents = CloseEffects.BlockedDependents(ctx.Store, [(item, updated)]).GetValueOrDefault(item.Id);
            if (json) { Out.EmitMutated(ctx, updated, dependents); return 0; }
            AnsiConsole.MarkupLine($"[green]set[/] {Markup.Escape(Out.Short(ctx, item.Id))}.{Markup.Escape(field)} = {Markup.Escape(result.Display)}{Out.TitleSuffix(item)}");
            if (dependents is not null) CloseReport.Print(ctx, item.Id, dependents);
            return 0;
        });
        return cmd;
    }

    public static Command Reorder()
    {
        var cmd = new Command(
            "reorder",
            "Reorder an item by rank (<id> --before / --after), or bake a sort into the ranks of a scope (--by state --under <id> | --all).");
        var idArg = new Argument<string?>("id") { Arity = ArgumentArity.ZeroOrOne };
        var beforeOpt = new Option<string?>("--before");
        var afterOpt = new Option<string?>("--after");
        var byOpt = new Option<string?>("--by") { Description = "Rewrite ranks so each sibling group follows this sort (state). Only items out of order move." };
        var underOpt = new Option<string?>("--under") { Description = "With --by: every sibling group in this item's subtree." };
        var shallowOpt = new Option<bool>("--shallow") { Description = "With --under: only the item's direct children." };
        var allOpt = new Option<bool>("--all") { Description = "With --by: every sibling group in the repo." };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "With --by: list the moves, write nothing." };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(beforeOpt);
        cmd.Options.Add(afterOpt);
        cmd.Options.Add(byOpt);
        cmd.Options.Add(underOpt);
        cmd.Options.Add(shallowOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(dryRunOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var idValue = parse.GetValue(idArg);
            var by = parse.GetValue(byOpt);
            var under = parse.GetValue(underOpt);
            var shallow = parse.GetValue(shallowOpt);
            var all = parse.GetValue(allOpt);
            var dryRun = parse.GetValue(dryRunOpt);
            if (by is not null)
            {
                if (idValue is not null || parse.GetValue(beforeOpt) is not null || parse.GetValue(afterOpt) is not null)
                    return Out.Error("--by reorders a whole scope — drop <id> / --before / --after and pick --under <id> or --all", ErrorCode.BadArgument, json);
                return ReorderBy(ctx, by, under, shallow, all, dryRun, json);
            }
            if (under is not null || shallow || all || dryRun)
                return Out.Error("--under / --shallow / --all / --dry-run go with --by", ErrorCode.BadArgument, json);
            if (idValue is null)
                return Out.Error("specify <id> --before / --after, or --by state with --under <id> or --all", ErrorCode.BadArgument, json);

            var (item, err) = ItemUtils.Resolve(ctx, idValue);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var before = parse.GetValue(beforeOpt);
            var after = parse.GetValue(afterOpt);
            if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after))
                return Out.Error("specify --before or --after", ErrorCode.BadArgument, json);

            var (target, targetErr) = ItemUtils.Resolve(ctx, !string.IsNullOrEmpty(before) ? before : after!);
            if (target is null) return Out.Error(targetErr!, ErrorCode.NotFound, json);
            if (target.Id == item.Id)
                return Out.Error("cannot reorder an item relative to itself", ErrorCode.BadArgument, json);
            // Ranks only order siblings, so a target under a different parent
            // has no meaning: the computed rank would be compared against the
            // item's own siblings instead, placing it arbitrarily. Refuse
            // rather than write that.
            if (target.Parent != item.Parent)
                return Out.Error(
                    $"{Out.TitledPlain(ctx, target)} is not a sibling of {Out.TitledPlain(ctx, item)} — " +
                    "rank only orders items under the same parent; re-parent first with " +
                    "`knit link <id> --parent <id>`",
                    ErrorCode.BadArgument, json);

            // Ranks written by older Knit versions can leave a slot no value fits.
            // Respace the group first so the move has somewhere to land, and
            // say so afterwards — refusing would leave no way forward, and
            // the caller already asked to reorder here.
            var group = RankRebalancer.GroupOf(ctx.Store, target.Parent);
            var respaced = group is { Blocked: true } ? RankRebalancer.Rebalance(ctx.Store, group) : 0;
            if (respaced > 0)
            {
                item = ctx.Store.Read(item.Id) ?? item;
                target = ctx.Store.Read(target.Id) ?? target;
            }

            var siblings = ctx.Store.AllItems()
                .Where(i => i.Id != item.Id && i.Parent == target.Parent && Rank.IsValid(i.Rank))
                .OrderBy(i => i.Rank, StringComparer.Ordinal)
                .ToList();

            string lo = "", hi = "";
            var idx = siblings.FindIndex(i => i.Id == target.Id);
            if (!string.IsNullOrEmpty(before))
            {
                hi = target.Rank ?? "";
                if (idx > 0) lo = siblings[idx - 1].Rank ?? "";
            }
            else
            {
                lo = target.Rank ?? "";
                if (idx >= 0 && idx + 1 < siblings.Count) hi = siblings[idx + 1].Rank ?? "";
            }

            var newRank = Rank.Between(lo, hi);
            var updated = item with { Rank = newRank };
            ItemUtils.Save(ctx, updated);
            if (json) { Out.EmitMutated(ctx, updated); return 0; }
            AnsiConsole.MarkupLine($"[green]moved[/] {Out.Titled(ctx, item)} → rank {newRank}");
            if (respaced > 0)
                AnsiConsole.MarkupLine(
                    $"[yellow]respaced[/] {respaced} sibling rank(s) to make room");
            return 0;
        });
        return cmd;
    }

    private static int ReorderBy(CliContext ctx, string by, string? under, bool shallow, bool all, bool dryRun, bool json)
    {
        if (!string.Equals(by.Trim(), "state", StringComparison.OrdinalIgnoreCase))
            return Out.Error($"unknown --by key: {by} — expected state", ErrorCode.BadArgument, json);
        if ((under is not null) == all)
            return Out.Error("--by needs a scope: --under <id> or --all (not both)", ErrorCode.BadArgument, json);
        if (shallow && under is null)
            return Out.Error("--shallow goes with --under", ErrorCode.BadArgument, json);

        string? root = null;
        if (under is not null)
        {
            var (item, err) = ItemUtils.Resolve(ctx, under);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);
            root = item.Id;
        }

        var plan = RankSortApplier.Plan(ctx.Store, root, shallow, SiblingOrder.Comparer(TreeSort.State));
        if (!dryRun) RankSortApplier.Write(ctx.Store, plan);

        if (json)
        {
            var moves = plan.Moves.Select(m => new JsonRankMove(m.Item.Id, m.Item.Title, m.Item.Rank, m.Rank)).ToList();
            Out.Json(new JsonReorderResult(dryRun, plan.Moved, plan.Total, plan.GroupsMoved, moves), AgentJsonContext.Default.JsonReorderResult);
            return 0;
        }
        foreach (var m in plan.Moves)
            AnsiConsole.MarkupLine($"  {Out.Titled(ctx, m.Item)} [dim]{Markup.Escape(m.Item.Rank ?? "-")} →[/] {Markup.Escape(m.Rank)}");
        var verb = dryRun ? "would reorder" : "reordered";
        AnsiConsole.MarkupLine($"[green]{verb}[/] {plan.Moved} of {plan.Total} items in {plan.GroupsMoved} groups");
        return 0;
    }

    public static Command Edit()
    {
        var cmd = new Command(
            "edit",
            "Edit an item. Default (no flags): open in $EDITOR — intended for humans. " +
            "Use --set-section <name> --value <text> to write a body section non-interactively (for scripts and agents).");
        var idArg = new Argument<string>("id");
        var setSectionOpt = new Option<string?>("--set-section") { Description = $"h2 section name to replace or append (e.g. \"{BodySections.AcceptanceHeading}\", \"{BodySections.PointersHeading}\"). Forbidden: Links, Comments." };
        var valueOpt = new Option<string?>("--value") { Description = "Body text for the section (required with --set-section). Empty string clears the section content but keeps the heading." };
        var deleteSectionOpt = new Option<string[]>("--delete-section")
        {
            Description = "h2 section to remove, heading and all. Repeatable. Use this for template headings that don't apply; --set-section --value \"\" is the other operation (keeps the heading). Forbidden: Links, Comments.",
            AllowMultipleArgumentsPerToken = false,
        };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(setSectionOpt);
        cmd.Options.Add(valueOpt);
        cmd.Options.Add(deleteSectionOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var idValue = parse.GetValue(idArg)!;
            var setSection = parse.GetValue(setSectionOpt);
            var value = parse.GetValue(valueOpt);
            var deleteSections = parse.GetValue(deleteSectionOpt) ?? [];

            if (deleteSections.Length > 0)
            {
                if (!string.IsNullOrEmpty(setSection))
                    return Out.Error("--delete-section and --set-section are separate operations; run them one at a time", ErrorCode.BadArgument, json);
                var (item, err) = ItemUtils.Resolve(ctx, idValue);
                if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

                // Every name is checked before anything is written: a typo in
                // the third one must not leave the first two deleted.
                var body = item.Body;
                foreach (var name in deleteSections)
                {
                    bool removed;
                    try { removed = BodySections.TryDeleteSection(body, name, out body); }
                    catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
                    if (!removed)
                    {
                        var present = BodySections.Headings(item.Body);
                        var have = present.Count == 0 ? "none" : string.Join(", ", present);
                        return Out.Error($"no section '{name}' on {Out.TitledPlain(ctx, item)} (has: {have})", ErrorCode.NotFound, json);
                    }
                }

                var stripped = item with { Body = body };
                ItemUtils.Save(ctx, stripped);
                if (json) { Out.EmitMutated(ctx, stripped); return 0; }
                var list = string.Join(", ", deleteSections.Select(n => $"## {Markup.Escape(n)}"));
                AnsiConsole.MarkupLine($"[green]deleted section[/] {Markup.Escape(Out.Short(ctx, item.Id))}.[bold]{list}[/]{Out.TitleSuffix(item)}");
                return 0;
            }

            if (!string.IsNullOrEmpty(setSection))
            {
                if (value is null) return Out.Error("--value is required with --set-section", ErrorCode.BadArgument, json);
                var (item, err) = ItemUtils.Resolve(ctx, idValue);
                if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);
                string newBody;
                try { newBody = Knit.Core.Frontmatter.BodySections.SetSection(item.Body, setSection, value); }
                catch (ArgumentException ex) { return Out.Error(ex.Message, ErrorCode.BadArgument, json); }
                var updated = item with { Body = newBody };
                ItemUtils.Save(ctx, updated);
                if (json) { Out.EmitMutated(ctx, updated); return 0; }
                AnsiConsole.MarkupLine($"[green]set section[/] {Markup.Escape(Out.Short(ctx, item.Id))}.[bold]## {Markup.Escape(setSection)}[/]{Out.TitleSuffix(item)}");
                return 0;
            }

            var path = ctx.Store.FindItemFile(idValue);
            if (path is null) return Out.Error("item not found", ErrorCode.NotFound, json);

            var editor = Environment.GetEnvironmentVariable("EDITOR")
                ?? (OperatingSystem.IsWindows() ? "notepad" : "vi");

            try
            {
                var psi = new ProcessStartInfo(editor, $"\"{path}\"") { UseShellExecute = false };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                return Out.Error($"failed to spawn editor '{editor}': {ex.Message}", ErrorCode.Internal, json);
            }

            var item2 = ctx.Store.Read(idValue);
            if (item2 is not null) ItemUtils.Save(ctx, item2);

            if (json && item2 is not null) { Out.EmitMutated(ctx, item2); return 0; }
            AnsiConsole.MarkupLine($"[green]edited[/] {Markup.Escape(path)}");
            return 0;
        });
        return cmd;
    }

    public static Command Comment()
    {
        var cmd = new Command("comment", "Append a comment");
        var idArg = new Argument<string>("id");
        var textArg = new Argument<string>("text");
        cmd.Arguments.Add(idArg);
        cmd.Arguments.Add(textArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var c = new Comment(
                IdGenerator.New(),
                ctx.Config.Handle!,
                DateTimeOffset.UtcNow,
                parse.GetValue(textArg)!);
            var updated = ItemUtils.WithComment(item, c);
            ItemUtils.Save(ctx, updated);
            if (json)
            {
                var result = new JsonCommentResult(
                    item.Id,
                    new JsonComment(
                        c.Id,
                        c.Author,
                        c.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        c.Body));
                Out.Json(result, AgentJsonContext.Default.JsonCommentResult);
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]commented[/] {Out.Titled(ctx, item)}");
            return 0;
        }, needsHandle: _ => true);
        return cmd;
    }
}
