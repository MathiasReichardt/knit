using System.CommandLine;

namespace Knit.Cli.Commands;

/// <summary>
/// Shared option instances, reused across commands. Recursively recursive
/// flags reside on the root command and apply to all subcommands.
/// </summary>
public static class GlobalOptions
{
    public static readonly Option<string?> Root = new("--root") { Description = "knit root override (skips walk-up)", Recursive = true };
    public static readonly Option<bool> Verbose = new("--verbose", "-v") { Description = "Print resolved knit root and other diagnostics", Recursive = true };
    public static readonly Option<bool> Json = new("--json") { Description = "Emit JSON output (agent-friendly)", Recursive = true };
    public static readonly Option<bool> NoColor = new("--no-color") { Description = "Disable ANSI colors and styling", Recursive = true };

    /// <summary>
    /// Walk up from the current command's parse result to find a global option value.
    /// </summary>
    public static T? GlobalValue<T>(this ParseResult parse, Option<T> option)
        => parse.GetValue(option);
}
