using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Output;

/// <summary>
/// Human hint after a close: the open items still blocked by the closed one
/// (<see cref="CloseEffects"/>), each with the command that resolves its row.
/// </summary>
public static class CloseReport
{
    public static void Print(CliContext ctx, string blockerId, IReadOnlyList<BlockedDependent> dependents)
    {
        if (dependents.Count == 0) return;
        var blocker = Out.Short(ctx, blockerId);
        var noun = dependents.Count == 1 ? "item" : "items";
        AnsiConsole.MarkupLine(
            $"  [yellow]{dependents.Count} {noun} still blocked by {Markup.Escape(blocker)}[/] " +
            "[dim]— resolve each, or point it at a new blocker[/]");
        foreach (var d in dependents)
        {
            var id = Out.Short(ctx, d.Item.Id);
            var fix = d.Remaining is null ? $"knit unblock {id}" : $"knit unlink {id} {blocker}";
            var also = d.Remaining is null ? "" : $" [dim](also {Markup.Escape(d.Remaining)})[/]";
            AnsiConsole.MarkupLine($"    {Out.Titled(ctx, d.Item)}{also} [dim]→[/] {Markup.Escape(fix)}");
        }
    }
}
