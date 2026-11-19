using System.CommandLine;
using Knit.Cli.Commands;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;

namespace Knit.Cli;

/// <summary>
/// A checkout whose <c>config.json</c> declares a local-only setting it has
/// not filled in refuses the command. Not a warning: an unset
/// <c>knit_root</c> leaves the walk on this checkout's own <c>.knit/</c>, so a
/// warning would let someone read a tree of items, believe it and act on it
/// while the real tracker is elsewhere.
/// </summary>
public static class RequiresGuard
{
    // The diagnostic and repair verbs, and those that never read a repo.
    private static readonly HashSet<string> Exempt = ["root", "config", "mount", "init", "version", "licenses"];

    public static int? Check(ParseResult parse, string[] args)
    {
        if (parse.Errors.Count > 0) return null;
        if (args.Contains("--help") || args.Contains("-h")) return null;
        for (var c = parse.CommandResult; c is not null; c = c.Parent as System.CommandLine.Parsing.CommandResult)
        {
            if (c.Command is RootCommand) break;
            if (Exempt.Contains(c.Command.Name)) return null;
        }
        if (parse.CommandResult.Command is RootCommand) return null;

        var r = RepoDiscovery.ResolveDetailed(Directory.GetCurrentDirectory(), parse.GetValue(GlobalOptions.Root));
        var unmet = Requirements.Check(r).Where(q => !q.Met).ToList();
        if (unmet.Count == 0) return null;
        return Out.Error(Refusal(r.Checkout!, unmet), ErrorCode.Conflict, parse.GetValue(GlobalOptions.Json));
    }

    internal static string Refusal(string checkout, IReadOnlyList<Requirement> unmet)
    {
        var what = string.Join("; ", unmet.Select(q => $"{q.Setting} — \"{q.Hint}\""));
        var direct = string.Join(", ", unmet.Select(q => q.Setting == RepoDiscovery.KnitRootKey
            ? "'knit root <path>' (or '--local' to keep this checkout's own)"
            : $"'knit mount set {q.Setting[Requirements.MountPrefix.Length..]} <path> --local'"));
        return $"{Path.Combine(RepoDiscovery.KnitDir(checkout), ConfigLoader.FileName)} requires settings this checkout " +
               $"has not filled in: {what} — run 'knit config setup', or directly {direct}";
    }
}
