using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Cli.Output;
using Knit.Data;
using Spectre.Console;

namespace Knit.Cli;

/// <summary>
/// Per-invocation services: resolved knit root, config, store.
/// </summary>
public sealed class CliContext
{
    public string Root { get; }

    /// <summary>How <see cref="Root"/> was found — including the checkout a redirect started from.</summary>
    public RepoDiscovery.RootResolution Resolution { get; }
    public KnitConfig Config { get; }
    public ItemStore Store { get; }
    public IFrontmatterSerializer Yaml { get; }

    private IdShortener? _shortener;
    public IdShortener Shortener => _shortener ??= IdShortener.FromItemsDir(Root);
    /// <summary>Re-read after files changed behind the store (a rename, a <c>--watch</c> repaint).</summary>
    public void Refresh()
    {
        _shortener = null;
        Store.Forget();
    }

    private (IReadOnlyList<Item> Items, IReadOnlyDictionary<string, string?> Map)? _slates;

    /// <summary>
    /// Read-time effective slate for an item: its own <c>slate:</c> when set,
    /// else the nearest ancestor's. Computed once per item list the store
    /// holds — a write drops that list, so the map reflects the write.
    /// </summary>
    public string? EffectiveSlate(string id)
    {
        var items = Store.AllItems();
        if (_slates?.Items != items) _slates = (items, SlateInheritance.Compute(items));
        return _slates.Value.Map.GetValueOrDefault(id);
    }

    private (IReadOnlyList<Item> Items, IReadOnlyDictionary<string, MilestoneInheritance.Effective> Map)? _milestones;

    /// <summary>
    /// Read-time effective milestone (<c>project/slug</c>): the item's own
    /// <c>Milestone</c> link, else the nearest ancestor's. Cached like
    /// <see cref="EffectiveSlate"/>.
    /// </summary>
    public string? EffectiveMilestone(string id)
    {
        var items = Store.AllItems();
        if (_milestones?.Items != items) _milestones = (items, MilestoneInheritance.Compute(items));
        return _milestones.Value.Map.GetValueOrDefault(id)?.QualifiedSlug;
    }

    /// <summary>
    /// Mtime of the item's file, found through the store's read-once scan; a
    /// folder listing per item made <c>--json</c> on a big repo quadratic.
    /// </summary>
    public DateTimeOffset Modified(Item item) =>
        Store.PathOf(item.Id) is { } path && File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : AgentMapper.ResolveModified(Root, item);

    private CliContext(
        RepoDiscovery.RootResolution resolution, KnitConfig config, ItemStore store, IFrontmatterSerializer yaml)
    {
        Root = resolution.Root!; Resolution = resolution; Config = config; Store = store; Yaml = yaml;
    }

    public static CliContext? TryLoad(string? rootOverride, bool verbose = false)
    {
        var cwd = Directory.GetCurrentDirectory();
        var resolution = RepoDiscovery.ResolveDetailed(cwd, rootOverride);
        if (resolution.Root is null) return null;

        RootNotice.Announce(resolution, cwd, verbose);

        var root = resolution.Root;
        var knitDir = RepoDiscovery.KnitDir(root);
        var config = ConfigLoader.Load(knitDir);
        var yaml = new SimpleFrontmatterSerializer();
        var store = new ItemStore(root, yaml);
        return new CliContext(resolution, config, store, yaml);
    }

    /// <summary>
    /// The one shared "couldn't resolve a root" error. Every command used to
    /// say <c>no .knit/ found</c>, which never revealed that the search looked
    /// somewhere else than the caller assumed.
    /// Names the directory the walk started from, the git root it stopped at,
    /// and an override that was set but didn't resolve.
    /// </summary>
    public static int NoRepoError(string? rootOverride, bool json)
    {
        var cwd = Directory.GetCurrentDirectory();
        var r = RepoDiscovery.ResolveDetailed(cwd, rootOverride);
        var skipped = r.Skipped.Count == 0
            ? ""
            : $"; ignored {string.Join(", ", r.Skipped)} (no {ConfigLoader.FileName} — not a knit repo)";

        if (r.Redirected) return Out.Error(RedirectRefusal(r), ErrorCode.Conflict, json);

        // An override that is set but unresolvable must not read as "no repo
        // anywhere": the fix is the variable, not an init.
        if (r.Origin != RepoDiscovery.RootOrigin.Walk)
        {
            var named = r.Origin == RepoDiscovery.RootOrigin.Explicit ? "--root" : RepoDiscovery.EnvOverride;
            return Out.Error(
                $"{named} points at '{r.OverridePath}', which is not a knit repo " +
                $"({RepoDiscovery.KnitDirName}/{ConfigLoader.FileName} missing){skipped} — " +
                "the walk up from the working directory was not attempted",
                ErrorCode.BadArgument, json);
        }

        var bound = r.GitRoot is null
            ? "the filesystem root"
            : $"the git repo root '{r.GitRoot}'";
        var searched = $"no knit repo found: searched from '{cwd}' up to {bound}{skipped}";
        if (BranchWithoutKnit(cwd) is { } branch)
            return Out.Error($"{searched}; {branch}", ErrorCode.BadArgument, json);
        // Ordered by likelihood, one shape for every context: a
        // fresh repo, a worktree and a script all land here, and picking
        // between them would need detection this message does not have.
        return Out.Error(
            $"{searched}; " +
            "run 'knit init' here for a tracker of its own, 'knit init --external <path>' to use an existing one " +
            $"— or --root / {RepoDiscovery.EnvOverride} for a one-off",
            ErrorCode.BadArgument, json);
    }

    /// <summary>
    /// A worktree on a branch older than <c>.knit/</c>: the walk finds nothing,
    /// yet the main checkout has a tracker. Null otherwise. Init is named as the
    /// wrong way out — a second <c>config.json</c> on the branch add/add-conflicts
    /// with main's when the branch merges back.
    /// </summary>
    internal static string? BranchWithoutKnit(string cwd)
    {
        var conflict = $"not 'knit init' here, a second {ConfigLoader.FileName} on the branch conflicts with theirs";
        if (GitWorktree.Bare(cwd) is { } bare)
        {
            if (GitWorktree.MainKnitRepo(cwd) is { } shared)
                return $"this branch has no {RepoDiscovery.KnitDirName}/, but the default branch's worktree has one " +
                       $"at '{shared}' — merge {bare.DefaultBranch} into this branch, then 'knit root {shared}'; {conflict}";
            return bare.DefaultWorktree is null && GitWorktree.SiblingKnitRepos(cwd).Count > 0
                ? $"this branch has no {RepoDiscovery.KnitDirName}/, but another worktree of this bare repo has one — " +
                  $"merge its branch into this one, then 'knit root <path>' to it; {conflict}"
                : null;
        }
        return GitWorktree.MainKnitRepo(cwd) is { } main
            ? $"this branch has no {RepoDiscovery.KnitDirName}/, but the main checkout's '{main}' has one — " +
              $"merge main into this branch, then 'knit root --worktree'; {conflict}"
            : null;
    }

    /// <summary>
    /// A configured redirect that does not resolve refuses the command — a
    /// quiet fallback to the checkout's own <c>.knit/</c> is the split state
    /// the redirect exists to prevent. Remedies are ranked restore → repoint →
    /// <c>--off</c>: leading with <c>--off</c> would have someone discard a
    /// correct setting because a share is disconnected.
    /// </summary>
    public static string RedirectRefusal(RepoDiscovery.RootResolution r)
    {
        var where = $"set by {Path.Combine(RepoDiscovery.KnitDir(r.Checkout!), ConfigLoader.LocalFileName)}";
        var repoint = "repoint it with 'knit root <path>'";
        var off = $"or 'knit root --off' to use this checkout's own {RepoDiscovery.KnitDirName}/ instead";
        return r.Failure switch
        {
            RepoDiscovery.RedirectFailure.TargetMissing =>
                $"knit root '{r.OverridePath}' does not exist ({where}) — usually a disconnected share or drive: " +
                $"reconnect it; {repoint}; {off}",
            RepoDiscovery.RedirectFailure.TargetNotARepo =>
                $"knit root '{r.OverridePath}' exists but holds no knit repo " +
                $"({RepoDiscovery.KnitDirName}/{ConfigLoader.FileName} missing; {where}) — restore the repo there; " +
                $"{repoint}; {off}",
            _ =>
                $"knit root '{r.OverridePath}' redirects again, to '{r.ChainTarget}' ({where}) — a redirect is one hop: " +
                $"'knit root {r.ChainTarget}' points here straight at the end; {off}",
        };
    }
}

/// <summary>
/// Stderr notes about the resolved root. Two things must never be silent: a
/// root that is not the obvious one for the working directory (a write
/// landing in a repo nobody meant to address), and a directory
/// named <c>.knit</c> that discovery walked past.
/// </summary>
internal static class RootNotice
{
    public static void Announce(RepoDiscovery.RootResolution resolution, string cwd, bool verbose)
    {
        var stderr = Out.Stderr();

        foreach (var dir in resolution.Skipped)
            stderr.MarkupLine(
                $"[yellow]note:[/] skipped {Markup.Escape(dir)} [dim]— no {ConfigLoader.FileName}, so not a knit repo[/]");

        var root = resolution.Root!;

        // A working redirect is a setting someone wrote on purpose, not the
        // unintended root the line below catches — so it is quiet unless
        // asked. A line on every call trains agents to skim stderr, which
        // costs the notes that matter. `knit status`, the SPA header and
        // `knit root` carry it instead.
        if (resolution.Redirected)
        {
            if (!verbose) return;
            stderr.MarkupLine(
                $"[yellow]using knit root:[/] {Markup.Escape(root)} [dim]— redirected by this checkout's " +
                $"{ConfigLoader.LocalFileName} (knit root)[/]");
            return;
        }

        WorktreeNudge(stderr, resolution);

        // Silent for the ordinary layouts: the repo you are standing in, or a
        // subdirectory of it. Printed every time would be noise nobody reads,
        // and this line is the only thing that surfaces an unintended root.

        if (Same(root, cwd) || Same(root, resolution.GitRoot)) return;
        stderr.MarkupLine($"[yellow]using knit root:[/] {Markup.Escape(root)}");
    }

    /// <summary>
    /// A worktree still undecided while its main checkout has a tracker is
    /// split state waiting to happen: two agents on two trees, each sure theirs
    /// is the one. Nothing is redirected for them — they are told, until
    /// <c>knit root --worktree</c> or <c>--local</c> writes the key.
    /// </summary>
    private static void WorktreeNudge(IAnsiConsole stderr, RepoDiscovery.RootResolution r)
    {
        if (GitWorktree.Unshared(r) is not { } u) return;
        var command = Markup.Escape(u.Command);
        var (other, share) = u switch
        {
            { Bare: false } => ($"the main checkout {Markup.Escape(u.Target!)} has", $"'{command}' to share main's"),
            { Target: { } t } => ($"the default branch's worktree {Markup.Escape(t)} has", $"'{command}' to share it"),
            _ => ("other worktrees of this bare repo have", $"'{command}' to share one"),
        };
        stderr.MarkupLine(
            $"[yellow]note:[/] this git worktree uses its own {RepoDiscovery.KnitDirName}/, and {other} one too " +
            $"[dim]— {share}, 'knit root --local' to keep this one (either silences this note)[/]");
    }

    private static bool Same(string a, string? b) =>
        b is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
