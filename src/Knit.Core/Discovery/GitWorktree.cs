using System.Text.RegularExpressions;

namespace Knit.Core.Discovery;

/// <summary>
/// Linked-worktree detection from the <c>.git</c> file alone — no git shell-out
/// in M1. A worktree's <c>.git</c> is a file reading
/// <c>gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, which names the main
/// checkout outright — unless the repo is bare, where there is none (see
/// <see cref="Bare"/>).
/// </summary>
public static partial class GitWorktree
{
    // The path is matched, not the file type: `--separate-git-dir` also leaves a
    // `.git` file, pointing somewhere with no `worktrees/` segment, and reading
    // that as a worktree would misdirect an ordinary checkout.
    [GeneratedRegex(@"^(?<main>.+?)[\\/]\.git[\\/]worktrees[\\/][^\\/]+[\\/]?$")]
    private static partial Regex WorktreeGitDir();

    /// <summary>
    /// The main checkout's root for a worktree whose git root is
    /// <paramref name="gitRoot"/>, or null when it is not a linked worktree or
    /// belongs to a bare repo.
    /// </summary>
    public static string? MainCheckout(string gitRoot)
    {
        if (!File.Exists(Path.Combine(gitRoot, ".git")) || GitDir(gitRoot) is not { } gitDir) return null;
        // A bare repo named `.git` matches the pattern, but the folder it sits
        // in is no checkout — taking it for one sent every worktree there.
        if (BareCommonDir(gitDir) is not null) return null;
        var m = WorktreeGitDir().Match(gitDir);
        return m.Success ? m.Groups["main"].Value : null;
    }

    /// <summary>
    /// The git directory of the checkout at <paramref name="gitRoot"/>: its
    /// <c>.git</c> folder, or where a <c>.git</c> file points (a worktree,
    /// <c>--separate-git-dir</c>). Null when there is neither.
    /// </summary>
    public static string? GitDir(string gitRoot)
    {
        var dotGit = Path.Combine(gitRoot, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (!File.Exists(dotGit)) return null;

        string text;
        try { text = File.ReadAllText(dotGit); }
        catch (IOException) { return null; }

        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
        if (line is null) return null;

        // Relative when git's `worktree.useRelativePaths` is on — relative to
        // the `.git` file's own directory.
        return Path.GetFullPath(line["gitdir:".Length..].Trim(), gitRoot);
    }

    /// <summary>
    /// Where <paramref name="dir"/> sits in the main checkout — or, in a bare
    /// repo, in its <see cref="BareRepo.DefaultWorktree"/> — the same
    /// subdirectory, since a knit root need not be the git root. Null outside a
    /// linked worktree, in that default worktree itself, and when there is none.
    /// </summary>
    public static string? Counterpart(string dir) => Map(dir)?.At;

    /// <summary>
    /// The knit repo the main checkout (bare: the default worktree) holds for
    /// <paramref name="dir"/>: its <see cref="Counterpart"/>, walked up to that
    /// checkout's root the way discovery would walk there. Null outside a
    /// worktree or when there is none.
    /// </summary>
    public static string? MainKnitRepo(string dir)
    {
        if (Map(dir) is not { } m) return null;
        for (var d = new DirectoryInfo(m.At); d is not null; d = d.Parent)
        {
            if (RepoDiscovery.IsRepo(d.FullName)) return d.FullName;
            if (RepoDiscovery.SamePath(d.FullName, m.Main)) break;
        }
        return null;
    }

    private static (string Main, string At)? Map(string dir)
    {
        var full = Path.GetFullPath(dir);
        var gitRoot = RepoDiscovery.FindGitRoot(full);
        if (gitRoot is null) return null;
        var main = Bare(gitRoot) is { } bare ? bare.DefaultWorktree : MainCheckout(gitRoot);
        if (main is null || RepoDiscovery.SamePath(main, gitRoot)) return null;
        return (main, At(main, gitRoot, full));
    }

    private static string At(string checkout, string gitRoot, string full) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(checkout, Path.GetRelativePath(gitRoot, full))));

    /// <summary>
    /// The bare-clone layout: a bare repo (<c>&lt;dir&gt;/.git</c>,
    /// <c>&lt;dir&gt;/.bare</c>, …) with every branch a linked worktree, so
    /// there is no main checkout.
    /// </summary>
    /// <param name="CommonDir">The bare repo itself.</param>
    /// <param name="Worktrees">Every worktree's root that exists on disk.</param>
    /// <param name="DefaultBranch">The bare repo's HEAD branch, or null — detached, or unreadable under reftable.</param>
    /// <param name="DefaultWorktree">The worktree that has <paramref name="DefaultBranch"/> checked out, or null.</param>
    public sealed record BareRepo(
        string CommonDir, IReadOnlyList<string> Worktrees, string? DefaultBranch, string? DefaultWorktree);

    /// <summary>
    /// The bare repo <paramref name="dir"/> is a worktree of, or null for
    /// every other layout. Read from files: the worktree's <c>commondir</c>,
    /// <c>core.bare</c> in the repo's <c>config</c>, and the
    /// <c>worktrees/*/gitdir</c> + <c>HEAD</c> back-pointers.
    /// </summary>
    public static BareRepo? Bare(string dir)
    {
        if (RepoDiscovery.FindGitRoot(Path.GetFullPath(dir)) is not { } gitRoot) return null;
        if (!File.Exists(Path.Combine(gitRoot, ".git")) || GitDir(gitRoot) is not { } gitDir) return null;
        if (BareCommonDir(gitDir) is not { } common) return null;

        var worktrees = new List<(string Root, string? Head)>();
        var admin = Path.Combine(common, "worktrees");
        if (Directory.Exists(admin))
            foreach (var entry in Directory.EnumerateDirectories(admin))
            {
                // gitdir names the worktree's `.git` file — relative to this
                // entry when `worktree.useRelativePaths` is on.
                if (ReadLine(Path.Combine(entry, "gitdir")) is not { } dotGit) continue;
                var root = Path.GetDirectoryName(Path.GetFullPath(dotGit, entry));
                if (root is not null && Directory.Exists(root))
                    worktrees.Add((Path.TrimEndingDirectorySeparator(root), ReadLine(Path.Combine(entry, "HEAD"))));
            }

        var branch = Branch(ReadLine(Path.Combine(common, "HEAD")));
        var main = branch is null ? null : worktrees.FirstOrDefault(w => Branch(w.Head) == branch).Root;
        return new BareRepo(common, worktrees.Select(w => w.Root).ToList(), branch, main);
    }

    /// <summary>
    /// The counterparts of <paramref name="dir"/> in the other worktrees of
    /// its bare repo that hold a knit repo — what a worktree can share when no
    /// default worktree can be named.
    /// </summary>
    public static IReadOnlyList<string> SiblingKnitRepos(string dir)
    {
        var full = Path.GetFullPath(dir);
        if (Bare(full) is not { } bare || RepoDiscovery.FindGitRoot(full) is not { } gitRoot) return [];
        return bare.Worktrees
            .Where(w => !RepoDiscovery.SamePath(w, gitRoot))
            .Select(w => At(w, gitRoot, full))
            .Where(RepoDiscovery.IsRepo)
            .ToList();
    }

    /// <summary>
    /// A worktree still on its own <c>.knit/</c> without having chosen it,
    /// while another checkout of the repo holds one it could share.
    /// </summary>
    /// <param name="Target">The checkout to share — main, or a bare repo's default branch's worktree; null when none can be named.</param>
    /// <param name="Bare">The repo is bare, so there is no main checkout and the share is by path.</param>
    public sealed record UnsharedWorktree(string? Target, bool Bare)
    {
        /// <summary>The command that shares it.</summary>
        public string Command => Bare ? $"knit root {Target ?? "<path>"}" : "knit root --worktree";
    }

    /// <summary>
    /// The split state waiting to happen — two agents on two trees, each sure
    /// theirs is the one — for a root found by the walk with no
    /// <c>knit_root</c> set; null otherwise.
    /// </summary>
    public static UnsharedWorktree? Unshared(RepoDiscovery.RootResolution r) =>
        r.Origin == RepoDiscovery.RootOrigin.Walk && r.KnitRoot is null && r.Checkout is { } checkout
            ? UnsharedAt(checkout)
            : null;

    /// <summary>
    /// <see cref="Unshared(RepoDiscovery.RootResolution)"/> for a knit repo
    /// named directly (<c>--root</c>): judged by its own <c>knit_root</c>,
    /// since no walk ran. The web UI uses this — its page stays open, so it
    /// says so even for a root someone typed.
    /// </summary>
    public static UnsharedWorktree? UnsharedAt(string checkout)
    {
        if (RepoDiscovery.ReadKnitRoot(checkout) is not null) return null;
        var main = Counterpart(checkout) is { } m && RepoDiscovery.IsRepo(m) ? m : null;
        if (Bare(checkout) is not { } bare) return main is null ? null : new UnsharedWorktree(main, Bare: false);
        if (main is not null) return new UnsharedWorktree(main, Bare: true);
        // Reftable, or the default branch has no worktree: say so without a path.
        return bare.DefaultWorktree is null && SiblingKnitRepos(checkout).Count > 0
            ? new UnsharedWorktree(null, Bare: true)
            : null;
    }

    private const string BranchRef = "ref: refs/heads/";

    // Reftable leaves this placeholder in every HEAD; the real refs live in
    // binary tables we do not parse — so no branch, and no default worktree.
    private const string ReftablePlaceholder = ".invalid";

    private static string? Branch(string? head) =>
        head is not null && head.StartsWith(BranchRef, StringComparison.Ordinal)
        && head[BranchRef.Length..] is { Length: > 0 } b && b != ReftablePlaceholder
            ? b
            : null;

    /// <summary>
    /// The directory holding the repo's shared state — <c>config</c>, refs,
    /// objects: a worktree's git dir names it in its <c>commondir</c> file;
    /// every other git dir is its own.
    /// </summary>
    public static string CommonDirOf(string gitDir) =>
        ReadLine(Path.Combine(gitDir, "commondir")) is { } rel
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(rel, gitDir))
            : Path.TrimEndingDirectorySeparator(gitDir);

    private static string? BareCommonDir(string gitDir)
    {
        if (ReadLine(Path.Combine(gitDir, "commondir")) is null) return null;
        var common = CommonDirOf(gitDir);
        return IsBareConfig(Path.Combine(common, "config")) ? common : null;
    }

    private static bool IsBareConfig(string config) =>
        GitConfigFile.Get(GitConfigFile.Read(config), "core", null, "bare")?.Trim().ToLowerInvariant()
            is "true" or "yes" or "on" or "1";

    private static string? ReadLine(string file)
    {
        try { return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } t ? t : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
