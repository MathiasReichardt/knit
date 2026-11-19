using System.CommandLine;
using Knit.Cli.Commands;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;

namespace Knit.Cli;

/// <summary>
/// Forward-safety preflight: refuse to touch a repo written by a newer knit
/// rather than misreading a format this binary doesn't know. Runs once in
/// <see cref="Program"/> before dispatch — the alternative (a check inside
/// <c>CliContext.TryLoad</c>) can't report its own error, since every caller
/// collapses a null context into "no .knit/ found".
/// </summary>
public static class FormatGuard
{
    public static int? Check(ParseResult parse, string[] args)
    {
        // Let System.CommandLine own parse errors and --help. `version` stays
        // usable in a future repo — it's what you need to diagnose one.
        if (parse.Errors.Count > 0) return null;
        if (args.Contains("--help") || args.Contains("-h")) return null;
        // `knit root` neither: it is the diagnostic and the escape for a
        // redirect, so it must run whatever the target turns out to be.
        if (parse.CommandResult.Command.Name is "version" or "root") return null;

        var root = RepoDiscovery.Resolve(Directory.GetCurrentDirectory(), parse.GetValue(GlobalOptions.Root));
        if (root is null) return null;

        var config = ConfigLoader.Load(RepoDiscovery.KnitDir(root));
        if (config is null || !FormatVersion.IsFuture(config.FormatVersion)) return null;

        return Out.Error(
            FormatVersion.UpgradeMessage(config.FormatVersion),
            ErrorCode.Conflict,
            parse.GetValue(GlobalOptions.Json));
    }
}
