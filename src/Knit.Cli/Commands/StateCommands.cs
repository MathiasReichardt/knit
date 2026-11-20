using System.CommandLine;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Commands;

public static class StateCommands
{
    public static Command Start()
    {
        var cmd = new Command("start", "Set state to in-progress and assign to me (taking over someone else's item is reported; --keep-assignee leaves the assignee alone)");
        var idArg = new Argument<string>("id");
        var keepOpt = new Option<bool>("--keep-assignee") { Description = "Don't reassign — start the item while it stays with whoever it is assigned to" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(keepOpt);
        cmd.SetRepoAction((ctx, p) =>
        {
            var keep = p.GetValue(keepOpt);
            return Update(
                ctx, p, idArg,
                i => keep
                    ? i with { State = ItemState.InProgress }
                    : i with { State = ItemState.InProgress, Assignee = ctx.Config.Handle },
                "started",
                // Taking work off someone else is a side effect worth naming —
                // it used to happen silently.
                i => !keep && !string.IsNullOrEmpty(i.Assignee)
                        && !string.Equals(i.Assignee, ctx.Config.Handle, StringComparison.Ordinal)
                    ? $"[yellow]took over from[/] {Markup.Escape(i.Assignee!)} [dim]— pass --keep-assignee to leave the assignee alone[/]"
                    : null,
                DraftStartGate);
            // Starting assigns it to you, so an unset handle would hand it to nobody.
        }, needsHandle: p => !p.GetValue(keepOpt));
        return cmd;
    }

    public static Command Done()
    {
        var cmd = new Command("done", "Set state to done");
        var idArg = new Argument<string>("id");
        cmd.Arguments.Add(idArg);
        cmd.SetRepoAction((ctx, p) => Update(ctx, p, idArg, i => i with { State = ItemState.Done }, "done",
            guard: DraftDoneGate));
        return cmd;
    }

    public static Command Submit()
    {
        var cmd = new Command("submit", "Set state to review — implemented, a final review pending (the reviewer runs `done`, or `start` to send it back)");
        var idArg = new Argument<string>("id");
        cmd.Arguments.Add(idArg);
        cmd.SetRepoAction((ctx, p) => Update(ctx, p, idArg, i => i with { State = ItemState.Review }, "submitted",
            guard: (c, i, json) => i.State == ItemState.InProgress
                ? null
                : Out.Error(
                    $"{Out.TitledPlain(c, i)} is {i.State.ToWire()} — only in-progress work goes to review (`knit start {Out.Short(c, i.Id)}` first)",
                    ErrorCode.BadArgument, json)));
        return cmd;
    }

    public static Command Approve()
    {
        var cmd = new Command("approve", "Promote a draft to todo — the item is specified and may be picked up");
        var idArg = new Argument<string>("id");
        cmd.Arguments.Add(idArg);
        cmd.SetRepoAction((ctx, p) => Update(ctx, p, idArg, i => i with { State = ItemState.Todo }, "approved",
            guard: (c, i, json) => i.State == ItemState.Draft
                ? null
                : Out.Error($"{Out.TitledPlain(c, i)} is already {i.State.ToWire()}, not a draft", ErrorCode.BadArgument, json)));
        return cmd;
    }

    public static Command Reopen()
    {
        var cmd = new Command("reopen", "Set state to todo (use to revert done / dismissed / in-progress / review)");
        var idArg = new Argument<string>("id");
        cmd.Arguments.Add(idArg);
        cmd.SetRepoAction((ctx, p) => Update(ctx, p, idArg, i => i with { State = ItemState.Todo }, "reopened"));
        return cmd;
    }

    public static Command Block()
    {
        var cmd = new Command("block", "Mark an item blocked — by another item (--by, a Blocked-by link) and/or a free-text reason; state untouched");
        var idArg = new Argument<string>("id") { Description = "Item that is blocked" };
        var byOpt = new Option<string?>("--by") { Description = "Blocker item id (adds a blocked-by link)" };
        var reasonOpt = new Option<string?>("--reason") { Description = "Why: with --by the note on that link (removed with it), else the free-text reason for an impediment that is not an item; replaces a previous one" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(byOpt);
        cmd.Options.Add(reasonOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var byArg = parse.GetValue(byOpt);
            var reasonArg = parse.GetValue(reasonOpt);
            if (string.IsNullOrWhiteSpace(byArg) && string.IsNullOrWhiteSpace(reasonArg))
                return Out.Error("specify --reason and/or --by", ErrorCode.BadArgument, json);

            var (target, err1) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (target is null) return Out.Error(err1!, ErrorCode.NotFound, json);

            Item? blocker = null;
            if (!string.IsNullOrWhiteSpace(byArg))
            {
                var (b, err2) = ItemUtils.Resolve(ctx, byArg!);
                if (b is null) return Out.Error(err2!, ErrorCode.NotFound, json);
                if (b.Id == target.Id) return Out.Error("an item cannot block itself", ErrorCode.BadArgument, json);
                blocker = b;
            }

            // Each impediment is its own reason: a blocker item is a Blocked-by
            // row (--reason becomes its note, so it goes with the link), anything
            // else the free-text reason.
            var reason = string.IsNullOrWhiteSpace(reasonArg) ? null : reasonArg.Trim();
            var updated = target;
            string? replacedReason = null;
            if (blocker is not null)
            {
                updated = ItemUtils.WithLink(ctx, updated, LinkRel.BlockedBy, blocker.Id);
                if (reason is not null) updated = updated.WithBlockerNote(blocker.Id, reason, out replacedReason);
            }
            else if (reason is not null)
            {
                if (!string.IsNullOrEmpty(target.BlockedReason) && target.BlockedReason != reason)
                    replacedReason = target.BlockedReason;
                updated = updated with { BlockedReason = reason };
            }
            ItemUtils.Save(ctx, updated);

            if (json)
            {
                Out.Json(
                    AgentMapper.Map(updated, ctx, replacedReason: replacedReason,
                        effectiveSlate: ctx.EffectiveSlate(updated.Id)),
                    AgentJsonContext.Default.JsonItem);
                return 0;
            }
            if (replacedReason is not null)
                AnsiConsole.MarkupLine($"[yellow]Replaced previous reason:[/] \"{Markup.Escape(replacedReason)}\"");
            var byText = blocker is null ? "" : $" [dim]by[/] {Out.Titled(ctx, blocker)}";
            var reasonText = reason is null ? "" : $" [dim]—[/] {Markup.Escape(reason)}";
            AnsiConsole.MarkupLine($"[green]blocked[/] {Out.Titled(ctx, target)}{byText}{reasonText}");
            return 0;
        });
        return cmd;
    }

    public static Command Unblock()
    {
        var cmd = new Command("unblock", "Resolve every impediment: remove the Blocked-by rows and clear the free-text reason (resolve a single blocker with `knit unlink`)");
        var idArg = new Argument<string>("id");
        cmd.Arguments.Add(idArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var impediments = item.LinkRows.Where(r => r.Rel == LinkRel.BlockedBy)
                .Select(r => new Impediment(r, null))
                .ToList();
            if (!string.IsNullOrEmpty(item.BlockedReason))
                impediments.Insert(0, new Impediment(null, item.BlockedReason));
            if (impediments.Count == 0 && LinkGraph.HeldBy(item, ctx.Store.Read) is { } holder)
                return Out.Error(
                    $"{Out.TitledPlain(ctx, item)} has no block of its own — blocked parent {Out.TitledPlain(ctx, holder)} " +
                    $"holds it; resolve that one (`knit unblock {Out.Short(ctx, holder.Id)}`)",
                    ErrorCode.BadArgument, json);

            var resolved = impediments;
            if (impediments.Count > 0 && !json && !Console.IsErrorRedirected)
            {
                // Prompt UI on stderr, like `knit pick` — stdout stays
                // capture-clean.
                var ui = Out.Stderr();
                string Label(Impediment i)
                {
                    if (i.Row is null) return $"reason: {i.Text}";
                    var id = LinkPath.ItemIdFromTarget(i.Row.TargetPath);
                    var note = string.IsNullOrEmpty(i.Row.Notes) ? "" : $" — {i.Row.Notes}";
                    return id is null
                        ? $"blocked by {i.Row.TargetText}{note}"
                        : $"blocked by {Out.Short(ctx, id)} \"{i.Row.TargetText}\"{note}";
                }
                try
                {
                    if (impediments.Count == 1)
                    {
                        if (!ui.Confirm($"Resolve {Markup.Escape(Label(impediments[0]))} and unblock?"))
                        {
                            ui.MarkupLine("[dim]no changes[/]");
                            return 0;
                        }
                    }
                    else
                    {
                        var prompt = new MultiSelectionPrompt<Impediment>()
                            .Title("[bold]Which impediments are resolved?[/] [dim](all preselected; a partial selection keeps the item blocked; Ctrl+C cancels)[/]")
                            .NotRequired()
                            .UseConverter(i => Markup.Escape(Label(i)));
                        foreach (var i in impediments) prompt.AddChoice(i).Select();
                        resolved = ui.Prompt(prompt);
                        if (resolved.Count == 0)
                        {
                            ui.MarkupLine("[dim]no changes[/]");
                            return 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Out.Error($"unblock cancelled: {ex.Message}", ErrorCode.BadArgument, json);
                }
            }

            var removedRows = resolved.Where(i => i.Row is not null).Select(i => i.Row!).ToList();
            var updated = item with
            {
                BlockedReason = resolved.Any(i => i.Row is null) ? null : item.BlockedReason,
                LinkRows = item.LinkRows.Where(r => !removedRows.Contains(r)).ToList(),
            };
            ItemUtils.Save(ctx, updated);
            if (json) { Out.EmitMutated(ctx, updated); return 0; }
            if (updated.BlockedSummary is { } remaining)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]resolved {resolved.Count} impediment(s)[/] — {Out.Titled(ctx, item)} " +
                    $"[yellow]is still blocked:[/] {Markup.Escape(remaining)}");
            }
            else
            {
                var removedText = removedRows.Count > 0 ? $" [dim](removed {removedRows.Count} blocked-by link(s))[/]" : "";
                AnsiConsole.MarkupLine($"[green]unblocked[/] {Out.Titled(ctx, item)}{removedText}");
            }
            return 0;
        });
        return cmd;
    }

    /// <summary>One reason an item is blocked: a Blocked-by row, or the free-text reason.</summary>
    private sealed record Impediment(LinkRow? Row, string? Text);

    public static Command Dismiss()
    {
        var cmd = new Command("dismiss", "Set state to dismissed (abandoned without completing)");
        var idArg = new Argument<string>("id");
        var cascadeOpt = new Option<bool>("--cascade") { Description = "Also dismiss every open descendant; required for --json / non-TTY when there are open children" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(cascadeOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var cascade = parse.GetValue(cascadeOpt);
            var graph = new LinkGraph(ctx.Store.AllItems());
            var openDescendants = graph.Subtree(item.Id)
                .Where(i => i.Id != item.Id)
                .Where(i => i.State != ItemState.Done && i.State != ItemState.Dismissed)
                .ToList();

            if (openDescendants.Count > 0 && !cascade && !json && Console.IsErrorRedirected == false)
            {
                var answer = AnsiConsole.Confirm(
                    $"This item has {openDescendants.Count} open child(ren). Dismiss them too?",
                    defaultValue: false);
                cascade = answer;
            }

            var dismissed = item with { State = ItemState.Dismissed };
            ItemUtils.Save(ctx, dismissed);

            var changes = new List<(Item Before, Item After)> { (item, dismissed) };
            if (cascade)
            {
                foreach (var d in openDescendants)
                {
                    var c = d with { State = ItemState.Dismissed };
                    ItemUtils.Save(ctx, c);
                    changes.Add((d, c));
                }
            }
            var cascadedItems = changes.Skip(1).Select(c => c.After).ToList();
            var dependents = CloseEffects.BlockedDependents(ctx.Store, changes);

            if (json)
            {
                if (cascade)
                {
                    var payload = new JsonDismissResult(
                        AgentMapper.Map(dismissed, ctx, effectiveSlate: ctx.EffectiveSlate(dismissed.Id),
                            blockedDependents: dependents.GetValueOrDefault(dismissed.Id)),
                        cascadedItems
                            .Select(c => AgentMapper.Map(c, ctx, effectiveSlate: ctx.EffectiveSlate(c.Id),
                                blockedDependents: dependents.GetValueOrDefault(c.Id)))
                            .ToList());
                    Out.Json(payload, AgentJsonContext.Default.JsonDismissResult);
                }
                else
                {
                    Out.EmitMutated(ctx, dismissed, dependents.GetValueOrDefault(dismissed.Id));
                }
                return 0;
            }
            AnsiConsole.MarkupLine($"[green]dismissed[/] {Out.Titled(ctx, item)}");
            if (cascadedItems.Count > 0)
                AnsiConsole.MarkupLine($"[dim]cascaded {cascadedItems.Count} descendant(s)[/]");
            foreach (var (id, deps) in dependents) CloseReport.Print(ctx, id, deps);
            return 0;
        });
        return cmd;
    }

    public static Command Assign()
    {
        var cmd = new Command("assign", "Set assignee");
        var idArg = new Argument<string>("id");
        var handleArg = new Argument<string>("handle");
        cmd.Arguments.Add(idArg);
        cmd.Arguments.Add(handleArg);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);
            var handle = parse.GetValue(handleArg)!.Trim();
            if (string.IsNullOrEmpty(handle)) return Out.Error("handle cannot be empty", ErrorCode.BadArgument, json);
            var updated = item with { Assignee = handle };
            ItemUtils.Save(ctx, updated);
            if (json) { Out.EmitMutated(ctx, updated); return 0; }
            AnsiConsole.MarkupLine($"[green]assigned[/] {Out.Titled(ctx, item)} → {Markup.Escape(handle)}");
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// `start` on a draft. An agent is refused and pointed at the promotion;
    /// a human at a terminal is asked, and a yes promotes and starts in one
    /// step (the mutate lambda sets <c>in-progress</c> either way). Same split
    /// as `unblock`: a redirected stderr means nobody is there to answer.
    /// Returns an exit code to stop on, or null to proceed.
    /// </summary>
    private static int? DraftStartGate(CliContext ctx, Item item, bool json)
    {
        if (item.State != ItemState.Draft) return null;
        var shortId = Out.Short(ctx, item.Id);
        if (json || Console.IsErrorRedirected)
            return Out.Error(
                $"{Out.TitledPlain(ctx, item)} is a draft — approve it first with `knit approve {shortId}`",
                ErrorCode.BadArgument, json);

        var ui = Out.Stderr();
        try
        {
            if (ui.Confirm($"{Markup.Escape(item.Title)} is a draft. Approve and start it?", false)) return null;
            ui.MarkupLine("[dim]no changes[/]");
            return 0;
        }
        catch (Exception ex)
        {
            return Out.Error($"start cancelled: {ex.Message}", ErrorCode.BadArgument, json);
        }
    }

    /// <summary>
    /// `done` straight from a draft, refused in both modes: closing work that
    /// was never specified is a mistake, not a shortcut. `dismiss` is the verb
    /// for "never mind".
    /// </summary>
    private static int? DraftDoneGate(CliContext ctx, Item item, bool json)
    {
        if (item.State != ItemState.Draft) return null;
        var shortId = Out.Short(ctx, item.Id);
        return Out.Error(
            $"{Out.TitledPlain(ctx, item)} is a draft — approve it with `knit approve {shortId}` before closing it, or drop it with `knit dismiss {shortId}`",
            ErrorCode.BadArgument, json);
    }

    private static int Update(CliContext ctx, ParseResult parse, Argument<string> idArg,
        Func<Item, Item> mutate, string verb, Func<Item, string?>? notice = null,
        Func<CliContext, Item, bool, int?>? guard = null)
    {
        var json = parse.GetValue(GlobalOptions.Json);
        var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
        if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);
        if (guard?.Invoke(ctx, item, json) is int stop) return stop;
        var updated = mutate(item);
        ItemUtils.Save(ctx, updated);
        var dependents = CloseEffects.BlockedDependents(ctx.Store, [(item, updated)]).GetValueOrDefault(item.Id);
        if (json) { Out.EmitMutated(ctx, updated, dependents); return 0; }
        var suffix = notice?.Invoke(item) is { } n ? $" [dim]—[/] {n}" : "";
        AnsiConsole.MarkupLine($"[green]{verb}[/] {Out.Titled(ctx, item)}{suffix}");
        if (dependents is not null) CloseReport.Print(ctx, item.Id, dependents);
        return 0;
    }
}
