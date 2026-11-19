using System.Text.Json;
using Knit.Core.Domain;
using Knit.Core.Ids;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli.Output;

/// <summary>
/// Output helpers. Plain text vs JSON, stderr for errors.
/// </summary>
public static class Out
{
    /// <summary>
    /// A console on stderr. Spectre's CI enrichers (GitHub Actions, Azure
    /// Pipelines, …) are off: they would switch ANSI on and change the width
    /// for redirected output.
    /// </summary>
    public static IAnsiConsole Stderr() => Sized(AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
        Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
    }));

    /// <summary>
    /// Test seam: a fixed width for every console knit creates. Without a
    /// console (CI) Spectre falls back to 80 columns, so wrapping would depend
    /// on the host running the tests.
    /// </summary>
    internal static int? WidthOverride { get; set; }

    internal static IAnsiConsole Sized(IAnsiConsole console)
    {
        if (WidthOverride is { } width) console.Profile.Width = width;
        return console;
    }

    /// <summary>
    /// Emit an error. The exit code is derived from the category. The
    /// <paramref name="msg"/> carries the specific situation (no
    /// internals, no stack traces — user-facing only).
    /// </summary>
    public static int Error(string msg, ErrorCode code, bool json = false)
    {
        var wire = code.ToWire();
        var exit = (int)code;
        var formatted = string.IsNullOrEmpty(wire) ? msg : $"[{wire}] {msg}";
        if (json)
        {
            var err = new JsonError(formatted, exit, string.IsNullOrEmpty(wire) ? null : wire);
            Console.Error.WriteLine(JsonSerializer.Serialize(err, AgentJsonContext.Default.JsonError));
        }
        else
        {
            var stderr = Out.Stderr();
            stderr.MarkupLine($"[red]error:[/] {Markup.Escape(formatted)}");
        }
        return exit;
    }

    /// <summary>
    /// Note that a minted id differs from what the caller asked for. Goes to
    /// <b>stderr</b> so the id the verb prints on stdout stays capture-clean —
    /// an agent that pipes the id still sees why it isn't the one it typed.
    /// Silent when only lowercasing / spacing changed: that much is expected.
    /// </summary>
    public static void MintNote(string raw, SlugResult slug)
    {
        if (!slug.Changed) return;
        var stderr = Out.Stderr();
        stderr.MarkupLine(
            $"[yellow]note:[/] id {Markup.Escape(raw)} → {Markup.Escape(slug.Value)} [dim]({Markup.Escape(slug.Reason!)})[/]");
    }

    public static void Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, info));
    }

    /// <summary>
    /// Render an item id at the shortest prefix that's unique among current
    /// items, with an 8-char floor (see <see cref="IdShortener"/>). Always
    /// emit the full uuid in <c>--json</c> output instead of this.
    /// </summary>
    public static string Short(CliContext ctx, string id) => ctx.Shortener.Short(id);

    /// <summary>
    /// Short id + quoted title (<c>k3f9 "Add rate limiting"</c>), the form the
    /// skills teach — agents copy the shape they see. Human output must never
    /// carry a bare id: an agent relaying "k3f9" can't say what it is.
    /// Returns ready-to-print markup: do not escape the result again.
    /// </summary>
    public static string Titled(CliContext ctx, Item item) => Titled(ctx, item.Id, item.Title);

    public static string Titled(CliContext ctx, string id) => Titled(ctx, id, ctx.Store.Read(id)?.Title);

    public static string Titled(CliContext ctx, string id, string? title)
    {
        var shortId = Markup.Escape(Short(ctx, id));
        return string.IsNullOrWhiteSpace(title) ? shortId : $"{shortId} {Quoted(title)}";
    }

    /// <summary><see cref="Titled(CliContext,string,string?)"/> as plain text, for error messages.</summary>
    public static string TitledPlain(CliContext ctx, Item item) =>
        string.IsNullOrWhiteSpace(item.Title) ? Short(ctx, item.Id) : $"{Short(ctx, item.Id)} \"{item.Title}\"";

    public static string TitledPlain(CliContext ctx, string id) =>
        ctx.Store.Read(id) is { } item ? TitledPlain(ctx, item) : Short(ctx, id);

    /// <summary>
    /// Trailing quoted title, for lines where the id can't be immediately
    /// followed by it (e.g. <c>set k3f9.state = doing</c>).
    /// </summary>
    public static string TitleSuffix(Item item) =>
        string.IsNullOrWhiteSpace(item.Title) ? "" : $" {Quoted(item.Title)}";

    private static string Quoted(string title) => $"[dim]\"{Markup.Escape(title)}\"[/]";

    /// <summary>
    /// A free-text table cell (title, summary, notes). Wraps on word
    /// boundaries and ellipsizes a word too long for the column — Spectre's
    /// default folds instead, which splits words mid-letter once a squeezed
    /// column is narrower than the word ("Authentica / tion").
    /// </summary>
    public static Markup Prose(string? text) =>
        new Markup(Markup.Escape(text ?? "")).Overflow(Overflow.Ellipsis);

    /// <summary>
    /// Emit a single mutated item as JSON (mutator <c>--json</c> contract).
    /// </summary>
    /// <summary>
    /// Row marker for a blocked item: <c>⊘</c> for its own block, <c>⊘↑</c>
    /// when only a blocked ancestor holds it — the arrow keeps the two apart
    /// once color is stripped.
    /// </summary>
    public static string BlockedMark(Item item, LinkGraph graph) =>
        item.IsBlocked ? " [red]⊘[/]"
        : graph.HeldBy(item.Id) is not null ? " [red]⊘[/][dim]↑[/]"
        : "";

    public static void EmitMutated(CliContext ctx, Item item, IReadOnlyList<BlockedDependent>? blockedDependents = null)
    {
        Json(AgentMapper.Map(item, ctx, effectiveSlate: ctx.EffectiveSlate(item.Id),
                blockedDependents: blockedDependents),
            AgentJsonContext.Default.JsonItem);
    }
}
