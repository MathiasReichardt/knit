using System.CommandLine;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.IO;

namespace Knit.Cli.Commands;

/// <summary>
/// Almost every command needs a resolved knit root before it can do anything.
/// Binding the action through here rather than resolving inside each one makes
/// that a structural fact: a new command can't forget the check, and the root
/// announcement + the not-found message live in exactly one place.
/// </summary>
public static class RepoAction
{
    /// <summary>
    /// Bind an action that runs only with a resolved repo. Resolution happens
    /// before any argument validation in the body — finding the repo is a
    /// precondition of the command running at all, so its error wins.
    /// </summary>
    /// <param name="cmd">The command to bind.</param>
    /// <param name="body">Runs with the resolved repo.</param>
    /// <param name="needsHandle">
    /// When this invocation records or asks about <i>you</i> — authoring,
    /// assigning to yourself, <c>--mine</c>. Declared here rather than checked
    /// in the body for the same reason the root is: an unset handle is a
    /// precondition, and one refusal wording serves every command.
    /// </param>
    /// <param name="exclusive">
    /// The operation name, for a mutation that rewrites many files: the body
    /// runs holding <see cref="RepoLock"/>, so two never interleave.
    /// </param>
    /// <remarks>
    /// A <see cref="WriteRefusedException"/> — someone else changed the item,
    /// holds the lock or holds the file — is a conflict, not a crash. So is a
    /// <see cref="FileLockedException"/> on a read.
    /// </remarks>
    public static void SetRepoAction(
        this Command cmd, Func<CliContext, ParseResult, int> body, Func<ParseResult, bool>? needsHandle = null,
        string? exclusive = null)
        => cmd.SetAction(parse =>
        {
            var rootOverride = parse.GetValue(GlobalOptions.Root);
            var json = parse.GetValue(GlobalOptions.Json);
            var ctx = CliContext.TryLoad(rootOverride, parse.GetValue(GlobalOptions.Verbose));
            if (ctx is null) return CliContext.NoRepoError(rootOverride, json);
            if (ctx.Config.Handle is null && needsHandle?.Invoke(parse) == true)
                return Out.Error(ConfigKeys.NoHandle, ErrorCode.Conflict, json);
            try
            {
                using var held = exclusive is null
                    ? null
                    : RepoLock.Acquire(ctx.Root, exclusive, note => Console.Error.WriteLine($"note: {note}"));
                return body(ctx, parse);
            }
            catch (Exception ex) when (ex is WriteRefusedException or FileLockedException)
            {
                return Out.Error(ex.Message, ErrorCode.Conflict, json);
            }
        });
}
