using System.CommandLine;
using Knit.Cli.Commands;
using Spectre.Console.Rendering;

namespace Knit.Cli.Output;

/// <summary>
/// Wires the per-command <c>--watch</c> flag to <see cref="LiveView.Run"/>.
/// Enforces the two non-negotiables: <c>--watch</c> + <c>--json</c> is
/// rejected (machine-readable output never animates), and <c>--watch</c>
/// against a redirected / non-interactive stdout is rejected (no point
/// repainting a captured stream).
/// </summary>
public static class Watch
{
    public const string HelpText =
        "Live-refresh the view for human monitoring (TTY-only, not for "
        + "agent use; agents should re-run the one-shot command).";

    public static Option<bool> Option() =>
        new("--watch") { Description = HelpText };

    /// <summary>
    /// Validates the watch invariants when <paramref name="watch"/> is
    /// set. Returns the error exit code if a gate fails, or <c>null</c>
    /// if the invocation may proceed. Call this **before** any branch
    /// that would short-circuit on <c>--json</c>, so <c>--watch --json</c>
    /// errors instead of silently producing JSON.
    /// </summary>
    public static int? Gate(ParseResult parse, Option<bool> watch)
    {
        if (!parse.GetValue(watch)) return null;
        if (parse.GetValue(GlobalOptions.Json))
            return Out.Error("--watch is incompatible with --json", ErrorCode.BadArgument);
        if (Console.IsOutputRedirected)
            return Out.Error("--watch requires an interactive terminal; drop --watch", ErrorCode.BadArgument);
        return null;
    }

    /// <summary>
    /// Validates the watch invariants and dispatches to <see cref="LiveView.Run"/>.
    /// Returns the exit code; callers should return this directly.
    /// </summary>
    public static int Run(CliContext ctx, ParseResult parse, Func<IRenderable> render)
    {
        if (parse.GetValue(GlobalOptions.Json))
            return Out.Error("--watch is incompatible with --json", ErrorCode.BadArgument);
        if (Console.IsOutputRedirected)
            return Out.Error("--watch requires an interactive terminal; drop --watch", ErrorCode.BadArgument);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return LiveView.Run(ctx, render, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
