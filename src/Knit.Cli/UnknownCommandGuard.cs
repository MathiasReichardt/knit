using System.CommandLine;
using System.CommandLine.Parsing;
using Knit.Cli.Commands;
using Knit.Cli.Output;

namespace Knit.Cli;

/// <summary>
/// Name the token nothing matched. System.CommandLine runs the help action
/// whenever <c>--help</c> is present, so a typo'd verb would otherwise print
/// the help of whatever command *did* match and exit 0 — the one way a
/// mistyped command reads as success. Runs in <see cref="Program"/> before
/// dispatch, ahead of <see cref="FormatGuard"/>.
/// </summary>
public static class UnknownCommandGuard
{
    public static int? Check(ParseResult parse)
    {
        // A stray `--flag` is System.CommandLine's error to phrase; only a
        // bare token was meant to be a verb or an argument.
        var unknown = parse.UnmatchedTokens.FirstOrDefault(t => !t.StartsWith('-'));
        if (unknown is null) return null;

        var matched = parse.CommandResult.Command;
        var path = Path(parse.CommandResult);
        var scope = path.Length == 0 ? "knit" : $"knit {path}";
        var what = matched.Subcommands.Count > 0
            ? $"unknown command: {(path.Length == 0 ? unknown : $"{path} {unknown}")}"
            : $"unexpected argument: {unknown}";

        return Out.Error($"{what} — run `{scope} --help` for what is valid here",
            ErrorCode.BadArgument, parse.GetValue(GlobalOptions.Json));
    }

    /// <summary>The matched command chain, root excluded (its name is the
    /// executable's, which is the test host under `dotnet test`).</summary>
    private static string Path(CommandResult result)
    {
        var names = new List<string>();
        for (SymbolResult? s = result; s is not null; s = s.Parent)
            if (s is CommandResult { Parent: not null } c)
                names.Add(c.Command.Name);
        names.Reverse();
        return string.Join(' ', names);
    }
}
