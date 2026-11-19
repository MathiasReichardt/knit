using System.CommandLine;
using System.Text;
using System.Text.Json;
using Knit.Cli.Output;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Spectre.Console;

namespace Knit.Cli.Commands;

/// <summary>
/// <c>knit root</c> — show or set where this checkout's knit state lives.
/// Only ever writes the checkout's <c>config.local.json</c>: a
/// committed path would follow the branch into main and every other worktree.
/// Runs before root resolution on purpose — it is the diagnostic and the
/// escape for a redirect that no longer resolves, so it cannot depend on one.
/// </summary>
public static class KnitRootCommand
{
    public static Command Build()
    {
        var cmd = new Command("root",
            "Show or set where this checkout's knit state lives: a worktree's main checkout, a shared knit repo, "
            + "or its own .knit/. Bare prints the effective root, its origin and the checkout (and offers to "
            + $"change it on a terminal). Writes {ConfigLoader.LocalFileName} only.");
        var pathArg = new Argument<string?>("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "A knit repo to use instead of this checkout's .knit/ — validated before anything is written",
        };
        var worktreeOpt = new Option<bool>("--worktree") { Description = "Use the main checkout of this git worktree" };
        var localOpt = new Option<bool>("--local") { Description = "Use this checkout's own .knit/, on purpose (silences the worktree nudge)" };
        var offOpt = new Option<bool>("--off") { Description = "Remove the setting — back to undecided, this checkout's own .knit/" };
        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(worktreeOpt);
        cmd.Options.Add(localOpt);
        cmd.Options.Add(offOpt);

        cmd.SetAction(parse =>
        {
            var json = parse.GetValue(GlobalOptions.Json);
            var rootOverride = parse.GetValue(GlobalOptions.Root);
            var path = parse.GetValue(pathArg);
            var worktree = parse.GetValue(worktreeOpt);
            var local = parse.GetValue(localOpt);
            var off = parse.GetValue(offOpt);

            var modes = (path is null ? 0 : 1) + (worktree ? 1 : 0) + (local ? 1 : 0) + (off ? 1 : 0);
            if (modes > 1)
                return Out.Error("pass one of <path>, --worktree, --local, --off", ErrorCode.BadArgument, json);

            var r = Resolve(rootOverride);
            if (modes == 0)
            {
                if (r.Checkout is null) return CliContext.NoRepoError(rootOverride, json);
                Show(r, json);
                return CanInterview(json) ? Interview(r, rootOverride) : 0;
            }

            if (r.Checkout is null) return NoCheckout(r, rootOverride, json);
            if (off) return Off(r, rootOverride, json);
            if (local) return Write(r.Checkout, RepoDiscovery.LocalKnitRoot, [], rootOverride, json);
            return worktree
                ? SetWorktree(r, rootOverride, json)
                : SetPath(r, Path.GetFullPath(path!), rootOverride, json);
        });
        return cmd;
    }

    private static RepoDiscovery.RootResolution Resolve(string? rootOverride) =>
        RepoDiscovery.ResolveDetailed(Directory.GetCurrentDirectory(), rootOverride);

    /// <summary>
    /// A redirect lives in a checkout's <c>.knit/</c>, so there has to be one —
    /// scaffolding it here would make a redirect verb create repo content.
    /// </summary>
    internal static int NoCheckout(RepoDiscovery.RootResolution r, string? rootOverride, bool json) =>
        r.Origin == RepoDiscovery.RootOrigin.Walk
            ? Out.Error(
                $"no {RepoDiscovery.KnitDirName}/ here — a knit root is stored in this checkout's " +
                $"{RepoDiscovery.KnitDirName}/{ConfigLoader.LocalFileName}, so it needs one: " +
                (CliContext.BranchWithoutKnit(Directory.GetCurrentDirectory())
                 ?? "own tracker: 'knit init'; shared root: 'knit init --external <path>'"),
                ErrorCode.BadArgument, json)
            : CliContext.NoRepoError(rootOverride, json);

    private static int SetWorktree(RepoDiscovery.RootResolution r, string? rootOverride, bool json)
    {
        var checkout = r.Checkout!;
        // No main checkout to mean: the default branch's worktree is a sibling
        // that can switch branch, so it is named, not implied.
        if (GitWorktree.Bare(checkout) is { } bare)
            return Out.Error(
                "this is a worktree of a bare repo, which has no main checkout — " +
                (GitWorktree.Counterpart(checkout) is { } shared
                    ? $"name the worktree to share: 'knit root {shared}' (the default branch's worktree)"
                    : bare.DefaultWorktree is not null
                        ? "this one is on the default branch; share it from the others with 'knit root <path to here>'"
                        : "name the worktree to share: 'knit root <path>'"),
                ErrorCode.BadArgument, json);
        var main = GitWorktree.Counterpart(checkout);
        if (main is null)
            return Out.Error(
                $"'{checkout}' is not in a linked git worktree (its .git is not a file naming " +
                "<main>/.git/worktrees/<name>) — pass the root instead: knit root <path>",
                ErrorCode.BadArgument, json);
        // A worktree-local tracker is a legitimate choice, so this states the
        // case and both ways out rather than calling anything broken.
        if (!RepoDiscovery.IsRepo(main))
            return Out.Error(
                $"the main checkout '{main}' has no {RepoDiscovery.KnitDirName}/, so this worktree's own is in use — " +
                "run 'knit init' there for one shared root, or keep this one with 'knit root --local'",
                ErrorCode.NotFound, json);
        return SetPath(r, main, rootOverride, json);
    }

    private static int SetPath(RepoDiscovery.RootResolution r, string target, string? rootOverride, bool json)
    {
        var (end, via, error, code) = Validate(r.Checkout!, target);
        return end is null ? Out.Error(error!, code, json) : Write(r.Checkout!, end, via, rootOverride, json);
    }

    /// <summary>
    /// The target as it will be written, or why nothing may be. Validated
    /// before writing, so a broken redirect cannot be self-inflicted: `knit
    /// init` refuses under a redirect, and one that pointed at nothing would
    /// leave no command able to repair it.
    /// </summary>
    internal static (string? End, IReadOnlyList<string> Via, string? Error, ErrorCode Code) Validate(
        string checkout, string target)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!Directory.Exists(target))
            return (null, [], $"'{target}' does not exist — nothing written", ErrorCode.NotFound);
        if (!RepoDiscovery.IsRepo(target))
            return (null, [],
                $"'{target}' is not a knit repo ({RepoDiscovery.KnitDirName}/{ConfigLoader.FileName} missing) — " +
                "nothing written; run 'knit init' there first, then point at it",
                ErrorCode.BadArgument);
        if (RepoDiscovery.SamePath(target, checkout))
            return (null, [], "that is this checkout — 'knit root --local' keeps its own .knit/ on purpose",
                ErrorCode.BadArgument);

        // Flattened here, never followed at runtime: resolution is one hop,
        // and pinning the end means a later repoint of the middle repo does
        // not silently move this checkout too.
        var via = new List<string>();
        var visited = new List<string> { checkout, target };
        var end = target;
        while (RepoDiscovery.ReadKnitRoot(end) is { } next && next != RepoDiscovery.LocalKnitRoot)
        {
            var hop = RepoDiscovery.ResolveKnitRootPath(end, next);
            if (visited.Any(v => RepoDiscovery.SamePath(v, hop)))
                return (null, [],
                    $"'{end}' redirects to '{hop}', which leads back round — {string.Join(" → ", visited)} → {hop}; " +
                    "nothing written",
                    ErrorCode.Conflict);
            if (!RepoDiscovery.IsRepo(hop))
                return (null, [],
                    $"'{end}' redirects to '{hop}', which is not a knit repo — nothing written; point at a repo directly",
                    ErrorCode.Conflict);
            via.Add(end);
            visited.Add(hop);
            end = hop;
        }
        return (end, via, null, ErrorCode.None);
    }

    private static int Off(RepoDiscovery.RootResolution r, string? rootOverride, bool json)
    {
        if (r.KnitRoot is null)
        {
            if (json) return Show(Resolve(rootOverride), json);
            AnsiConsole.MarkupLine(
                $"[dim]no knit root set in {Markup.Escape(LocalFile(r.Checkout!))} — nothing to clear[/]");
            return 0;
        }
        return Write(r.Checkout!, null, [], rootOverride, json);
    }

    /// <summary>
    /// Writes the key and nothing else — the one code path every mode, the
    /// interview, <c>knit config setup</c> and <c>knit init --external</c> go
    /// through. Returns why it could not, or null.
    /// </summary>
    internal static string? Store(string checkout, string? value)
    {
        var knitDir = RepoDiscovery.KnitDir(checkout);

        if (ConfigLoader.ReadForWrite(LocalFile(checkout)) is not { } raw)
            return ConfigLoader.Unwritable(LocalFile(checkout));
        if (value is null) raw.Remove(RepoDiscovery.KnitRootKey);
        else raw[RepoDiscovery.KnitRootKey] = value;

        if (raw.Count == 0) File.Delete(LocalFile(checkout));
        else ConfigLoader.SaveRawLayer(knitDir, ConfigLoader.LocalFileName, raw);
        // Older repos predate the entry; a machine path must never be committed.
        RepoDiscovery.EnsureKnitGitignore(checkout);
        return null;
    }

    internal static int Write(
        string checkout, string? value, IReadOnlyList<string> via, string? rootOverride, bool json)
    {
        if (Store(checkout, value) is { } unreadable) return Out.Error(unreadable, ErrorCode.Conflict, json);

        var now = Resolve(rootOverride);
        if (json) return Show(now, json);

        AnsiConsole.MarkupLine(value switch
        {
            null => $"[green]cleared[/] knit root [dim]— this checkout's own .knit/ ({Markup.Escape(LocalFile(checkout))})[/]",
            RepoDiscovery.LocalKnitRoot =>
                $"[green]set[/] knit root [dim]to this checkout's own .knit/, on purpose ({Markup.Escape(LocalFile(checkout))})[/]",
            _ => $"[green]set[/] knit root → {Markup.Escape(value)} [dim]({Markup.Escape(LocalFile(checkout))})[/]",
        });
        if (via.Count > 0)
            AnsiConsole.MarkupLine(
                $"[dim]{Markup.Escape(string.Join(" → ", via))} redirects to {Markup.Escape(value!)} — pointing at " +
                $"{Markup.Escape(value!)}[/]");

        // Redirecting an existing tracker is a migration; a silent
        // disappearance is the wrong shape for one.
        var hidden = HiddenItems(checkout, value);
        if (hidden > 0)
            AnsiConsole.MarkupLine(
                $"[yellow]note:[/] {hidden} item{(hidden == 1 ? "" : "s")} in this checkout's " +
                $".knit/items/ {(hidden == 1 ? "is" : "are")} now hidden — not moved");
        // A worktree's redirect is this machine's layout, not the project's:
        // declared on the branch, it would reach main and demand one there too.
        if (hidden > 0 && !DeclaresKnitRoot(checkout) && GitWorktree.Counterpart(checkout) is null
            && GitWorktree.Bare(checkout) is null)
            AnsiConsole.MarkupLine(
                "[dim]declare it for clones: knit config set " +
                $"{Requirements.Key}.{RepoDiscovery.KnitRootKey} \"<where to get it>\"[/]");
        if (value is not null && SiblingNote(checkout, value) is { } sibling) AnsiConsole.MarkupLine(sibling);
        if (now.Origin is RepoDiscovery.RootOrigin.Explicit or RepoDiscovery.RootOrigin.Environment)
            AnsiConsole.MarkupLine(
                $"[yellow]note:[/] {OverrideName(now)} is set and overrides knit root in this shell");
        return 0;
    }

    /// <summary>
    /// A note when <paramref name="target"/> is another worktree of this
    /// checkout's bare repo, else null. Not guarded at runtime: switching that
    /// folder's branch moves every checkout pointing at it.
    /// </summary>
    internal static string? SiblingNote(string checkout, string target)
    {
        if (target == RepoDiscovery.LocalKnitRoot
            || GitWorktree.Bare(checkout) is not { } bare
            || RepoDiscovery.FindGitRoot(target) is not { } targetGit
            || !bare.Worktrees.Any(w => RepoDiscovery.SamePath(w, targetGit))
            || GitHead.Read(targetGit)?.Branch is not { } branch)
            return null;
        return $"[yellow]note:[/] {Markup.Escape(target)} is the worktree of branch '{Markup.Escape(branch)}' — " +
               $"knit root is a path, so keep '{Markup.Escape(branch)}' checked out there";
    }

    private static bool DeclaresKnitRoot(string checkout) =>
        ConfigLoader.Load(RepoDiscovery.KnitDir(checkout)).Requires?.ContainsKey(RepoDiscovery.KnitRootKey) == true;

    private static int HiddenItems(string checkout, string? knitRoot) =>
        knitRoot is null or RepoDiscovery.LocalKnitRoot
            ? 0
            : ItemLayout.EnumerateFiles(RepoDiscovery.ItemsDir(checkout)).Count();

    private static int Show(RepoDiscovery.RootResolution r, bool json)
    {
        if (json)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("root", r.Root);
                w.WriteString("origin", OriginWire(r));
                w.WriteString("checkout", r.Checkout);
                w.WriteString("knit_root", r.KnitRoot);
                w.WriteString("failure", FailureWire(r.Failure));
                w.WriteString("target", r.Redirected ? r.OverridePath : null);
                w.WriteNumber("hidden_items", r.Checkout is null ? 0 : HiddenItems(r.Checkout, r.KnitRoot));
                w.WriteEndObject();
            }
            Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            return 0;
        }

        var t = new Table().Border(TableBorder.None).HideHeaders();
        t.AddColumn("k");
        t.AddColumn("v");
        t.AddRow(new Markup("[bold]knit root[/]"),
            new Markup(r.Root is null ? "[red](unresolved)[/]" : Markup.Escape(r.Root)));
        t.AddRow(new Markup("[bold]origin[/]"), new Markup(Markup.Escape(OriginText(r))));
        t.AddRow(new Markup("[bold]checkout[/]"), new Markup(Markup.Escape(r.Checkout ?? "—")));
        AnsiConsole.Write(t);

        if (r.Redirected && r.Root is null)
            AnsiConsole.MarkupLine($"[red]unresolved:[/] {Markup.Escape(CliContext.RedirectRefusal(r))}");
        if (r.Origin is RepoDiscovery.RootOrigin.Explicit or RepoDiscovery.RootOrigin.Environment
            && r.KnitRoot is { } kr && kr != RepoDiscovery.LocalKnitRoot)
            AnsiConsole.MarkupLine(
                $"[yellow]note:[/] this repo's knit root ({Markup.Escape(kr)}) is not followed — " +
                $"{OverrideName(r)} names a root directly");
        return 0;
    }

    private static string OriginText(RepoDiscovery.RootResolution r) => r.Origin switch
    {
        RepoDiscovery.RootOrigin.Explicit => "--root",
        RepoDiscovery.RootOrigin.Environment => RepoDiscovery.EnvOverride,
        RepoDiscovery.RootOrigin.Redirect => $"redirect — knit_root in {LocalFile(r.Checkout!)}",
        _ when Requirements.KnitRootOverriddenLocally(r) =>
            $"local (declared external, overridden here) — knit_root \".\" in {LocalFile(r.Checkout!)}",
        _ when r.KnitRoot == RepoDiscovery.LocalKnitRoot =>
            $"this checkout, on purpose — knit_root \".\" in {LocalFile(r.Checkout!)}",
        _ => "this checkout (no knit root set)",
    };

    private static string OriginWire(RepoDiscovery.RootResolution r) => r.Origin switch
    {
        RepoDiscovery.RootOrigin.Explicit => "explicit",
        RepoDiscovery.RootOrigin.Environment => "environment",
        RepoDiscovery.RootOrigin.Redirect => "redirect",
        _ when r.KnitRoot == RepoDiscovery.LocalKnitRoot => "local",
        _ => "walk",
    };

    private static string? FailureWire(RepoDiscovery.RedirectFailure f) => f switch
    {
        RepoDiscovery.RedirectFailure.TargetMissing => "target_missing",
        RepoDiscovery.RedirectFailure.TargetNotARepo => "target_not_a_repo",
        RepoDiscovery.RedirectFailure.Chained => "chained",
        _ => null,
    };

    private static string OverrideName(RepoDiscovery.RootResolution r) =>
        r.Origin == RepoDiscovery.RootOrigin.Explicit ? "--root" : RepoDiscovery.EnvOverride;

    private static string LocalFile(string checkout) =>
        Path.Combine(RepoDiscovery.KnitDir(checkout), ConfigLoader.LocalFileName);

    // Only with a person at both ends: bare `knit root` is also what a script
    // captures, and agents create worktrees and cannot answer — the flags are
    // the primary surface.
    private static bool CanInterview(bool json) =>
        !json && !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private const string Keep = "keep it as it is";
    private const string Pick = "a path to another knit repo";
    private const string Own = "this checkout's own .knit/, on purpose";
    private const string Clear = "clear the setting";

    private static int Interview(RepoDiscovery.RootResolution r, string? rootOverride)
    {
        var main = GitWorktree.Counterpart(r.Checkout!);
        var worktree = main is not null && RepoDiscovery.IsRepo(main)
            ? GitWorktree.Bare(r.Checkout!) is not null
                ? $"the default branch's worktree ({main})"
                : $"the main checkout of this worktree ({main})"
            : null;

        var prompt = new SelectionPrompt<string>().Title("Change where this checkout's knit state lives?");
        prompt.AddChoice(Keep);
        if (worktree is not null) prompt.AddChoice(worktree);
        prompt.AddChoice(Pick);
        if (r.KnitRoot != RepoDiscovery.LocalKnitRoot) prompt.AddChoice(Own);
        if (r.KnitRoot is not null) prompt.AddChoice(Clear);

        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(prompt);
        return choice switch
        {
            Keep => 0,
            Pick => SetPath(r, Path.GetFullPath(AnsiConsole.Ask<string>("Knit repo [green]path[/]:")), rootOverride, json: false),
            Own => Write(r.Checkout!, RepoDiscovery.LocalKnitRoot, [], rootOverride, json: false),
            Clear => Write(r.Checkout!, null, [], rootOverride, json: false),
            _ => SetPath(r, main!, rootOverride, json: false),
        };
    }
}
