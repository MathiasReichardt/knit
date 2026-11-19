using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Domain;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit retype &lt;id&gt; --to &lt;type&gt; [--parent &lt;id&gt;]</c> —
/// change an item's type, optionally moving it to a new parent in the
/// same atomic write. Distinct from <c>knit set type=…</c>: the latter
/// would skip hierarchy validation.
/// </summary>
public static class RetypeCommand
{
    public static Command Build()
    {
        var cmd = new Command(
            "retype",
            "Change an item's type (and optionally re-parent it) atomically; validates hierarchy.");
        var idArg = new Argument<string>("id");
        var toOpt = new Option<string>("--to") { Description = "New type: project|epic|feature|bug|task|todo", Required = true };
        var parentOpt = new Option<string?>("--parent")
        {
            Description = "New parent id (omit to keep current parent; pass empty string '' to detach to root)",
        };
        var yesOpt = new Option<bool>("--yes") { Description = "Skip the interactive confirmation prompt" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(parentOpt);
        cmd.Options.Add(yesOpt);
        cmd.SetRepoAction((ctx, parse) =>
        {
            var json = parse.GetValue(GlobalOptions.Json);

            var (item, err) = ItemUtils.Resolve(ctx, parse.GetValue(idArg)!);
            if (item is null) return Out.Error(err!, ErrorCode.NotFound, json);

            var toStr = parse.GetValue(toOpt)!;
            if (!EnumStrings.TryParseItemType(toStr, out var toType))
                return Out.Error($"unknown type '{toStr}' (expected project|epic|feature|bug|task|todo)",
                    ErrorCode.BadArgument, json);

            // Three states for --parent: omitted (null → keep current),
            // explicit empty (detach to root), non-empty (resolve).
            var parentValue = parse.GetValue(parentOpt);
            var parentChanging = parentValue is not null;

            Item? newParent;
            if (!parentChanging)
            {
                newParent = string.IsNullOrEmpty(item.Parent) ? null : ctx.Store.Read(item.Parent!);
            }
            else if (parentValue!.Length == 0)
            {
                newParent = null;
            }
            else
            {
                var (p, pe) = ItemUtils.Resolve(ctx, parentValue);
                if (p is null) return Out.Error(pe!, ErrorCode.NotFound, json);
                newParent = p;
            }

            var allItems = ctx.Store.AllItems().ToList();
            var validation = Retype.Validate(item, toType, newParent, allItems);
            if (!validation.IsOk)
            {
                var code = validation.ErrorCode switch
                {
                    Retype.ErrInvalidType => ErrorCode.BadArgument,
                    Retype.ErrInvalidParent => ErrorCode.BadArgument,
                    Retype.ErrWouldOrphanChildren => ErrorCode.Conflict,
                    Retype.ErrCycle => ErrorCode.Conflict,
                    _ => ErrorCode.Internal,
                };
                var detail = validation.OffendingChildIds.Count > 0
                    ? $"{validation.Message} (offending: {string.Join(", ", validation.OffendingChildIds.Select(i => Out.TitledPlain(ctx, i)))})"
                    : validation.Message!;
                return Out.Error($"[{validation.ErrorCode}] {detail}", code, json);
            }

            if (item.Type == toType && (newParent?.Id ?? null) == (item.Parent ?? null))
            {
                if (json) { Out.EmitMutated(ctx, item); return 0; }
                AnsiConsole.MarkupLine("[yellow]no change[/]");
                return 0;
            }

            var yes = parse.GetValue(yesOpt);
            if (!yes && !json && !Console.IsErrorRedirected)
            {
                var summary = $"Retype {Out.Titled(ctx, item)} from {item.Type.ToWire()} → {toType.ToWire()}";
                if (parentChanging)
                    summary += newParent is null
                        ? " and detach to root"
                        : $" and move under {Out.Titled(ctx, newParent)}";
                if (!AnsiConsole.Confirm($"{summary}. Proceed?", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]aborted[/]");
                    return 0;
                }
            }

            // Apply: type change always; parent change only when requested.
            var updated = item with { Type = toType };
            if (parentChanging)
            {
                updated = newParent is null
                    ? ItemUtils.WithoutParent(updated)
                    : ItemUtils.WithParent(ctx, updated, newParent.Id);
            }
            ItemUtils.Save(ctx, updated);

            if (json) { Out.EmitMutated(ctx, updated); return 0; }
            AnsiConsole.MarkupLine(
                $"[green]retyped[/] {Out.Titled(ctx, item)}: " +
                $"{Markup.Escape(item.Type.ToWire())} → {Markup.Escape(toType.ToWire())}" +
                (parentChanging
                    ? newParent is null ? " [dim](detached)[/]" : $" [dim](parent → {Out.Titled(ctx, newParent)})[/]"
                    : ""));
            return 0;
        }, exclusive: "retype");
        return cmd;
    }
}
