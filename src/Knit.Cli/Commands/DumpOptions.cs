using System.CommandLine;
using System.Text;
using Knit.Cli.Output;
using Knit.Core.Domain;

namespace Knit.Cli.Commands;

/// <summary>
/// The shared <c>--dump</c> surface of <c>list</c> and <c>tree</c>.
/// The flag changes nothing about selection — every filter resolves exactly
/// as it does without it — only whether a selected item prints as a row or in
/// full.
/// </summary>
internal static class DumpOptions
{
    /// <summary>
    /// Deliberately low. The flag exists to produce large output, so the
    /// default is the point at which an agent should have to say it meant it
    /// — a dump that quietly eats a context window is the failure this
    /// guards.
    /// </summary>
    internal const int DefaultMax = 40;

    internal static Option<bool> Dump() => new("--dump")
    {
        Description =
            "Print each selected item in full (frontmatter, body, links, comments) instead of one row — "
            + "read a whole slice in one call instead of looping `knit show`. Refuses above --dump-max items.",
    };

    internal static Option<int> Max() => new("--dump-max")
    {
        Description = $"Item ceiling for --dump (default {DefaultMax}). Raise it only after seeing the refusal's count.",
        DefaultValueFactory = _ => DefaultMax,
    };

    /// <summary>
    /// Refuse rather than truncate. A short dump that *looks* complete
    /// is the same failure as a silently hidden file: the caller can't tell
    /// what it didn't get. The refusal names the count so the ceiling can be
    /// raised deliberately.
    /// </summary>
    internal static int? Guard(IReadOnlyCollection<Item> items, int max, bool json, string narrowHint)
    {
        if (items.Count <= max) return null;
        return Out.Error(
            $"--dump selected {items.Count} items (~{Kb(Estimate(items))}), above the {max}-item limit — "
            + $"narrow it ({narrowHint}) or raise it with --dump-max {items.Count}",
            ErrorCode.BadArgument, json);
    }

    /// <summary>
    /// Size goes to stderr, always: stdout stays capture-clean, and the caller
    /// learns what a dump costs at the moment it pays for it.
    /// </summary>
    internal static void Report(int count, int bytes) =>
        Console.Error.WriteLine($"dumped {count} item{(count == 1 ? "" : "s")}, {Kb(bytes)}");

    private static int Estimate(IEnumerable<Item> items) =>
        items.Sum(i => i.Title.Length + i.Body.Length + i.Comments.Sum(c => c.Body.Length + 60) + 200);

    private static string Kb(int bytes) =>
        bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";

    /// <summary>Write a rendered dump to stdout and report its real size.</summary>
    internal static void Emit(StringBuilder sb, int count)
    {
        var text = sb.ToString();
        Console.Write(text);
        Report(count, Encoding.UTF8.GetByteCount(text));
    }
}
