using System.Text.Json.Nodes;
using Knit.Core.Config;
using Knit.Core.IO;
using Knit.Core.Refs;

namespace Knit.Core.Discovery;

public static class RepoDiscovery
{
    public const string KnitDirName = ".knit";
    public const string EnvOverride = "KNIT_ROOT";

    /// <summary>How a resolved root was arrived at.</summary>
    public enum RootOrigin
    {
        /// <summary><c>--root</c>.</summary>
        Explicit,
        /// <summary><see cref="EnvOverride"/>.</summary>
        Environment,
        /// <summary>The upward walk from the working directory.</summary>
        Walk,
        /// <summary>
        /// The walk found a checkout whose <c>config.local.json</c> names another
        /// root in <see cref="KnitRootKey"/>.
        /// </summary>
        Redirect
    }

    /// <summary>Why a configured redirect did not resolve.</summary>
    public enum RedirectFailure
    {
        None,
        /// <summary>The target directory is gone — usually a disconnected share or drive.</summary>
        TargetMissing,
        /// <summary>The target exists but holds no knit repo.</summary>
        TargetNotARepo,
        /// <summary>The target redirects again. One hop only; <c>knit root</c> flattens at write time.</summary>
        Chained,
    }

    /// <summary>
    /// The redirect key. Read from the checkout's <c>config.local.json</c>
    /// only: a committed path would propagate to every other checkout.
    /// </summary>
    public const string KnitRootKey = "knit_root";

    /// <summary><see cref="KnitRootKey"/> value meaning "this checkout's own <c>.knit/</c>, on purpose".</summary>
    public const string LocalKnitRoot = ".";

    /// <summary>
    /// Everything discovery learned, not just the answer. The extras exist so
    /// a failure can say <i>where it looked</i> and a success can say which
    /// root it picked — a silent walk into an unintended repo is the bug this
    /// type was introduced for.
    /// </summary>
    /// <param name="Root">The resolved root, or null.</param>
    /// <param name="Origin">Which of the three mechanisms answered.</param>
    /// <param name="StartDir">Full path the walk started from.</param>
    /// <param name="GitRoot">Nearest enclosing git repo root, or null outside one.</param>
    /// <param name="OverridePath">
    /// The path an override named, when <paramref name="Origin"/> is not
    /// <see cref="RootOrigin.Walk"/> — set whether or not it resolved.
    /// </param>
    /// <param name="Skipped">
    /// Directories named <c>.knit</c> that failed the repo check and were
    /// walked past. Never silently dropped: callers report them.
    /// </param>
    /// <param name="Checkout">
    /// The repo the walk or override landed on, before any redirect — where
    /// <c>knit root</c> writes. Equals <paramref name="Root"/> unless redirected.
    /// </param>
    /// <param name="KnitRoot">The checkout's raw <see cref="KnitRootKey"/>, or null when unset.</param>
    /// <param name="Failure">Why <see cref="RootOrigin.Redirect"/> left <paramref name="Root"/> null.</param>
    /// <param name="ChainTarget">For <see cref="RedirectFailure.Chained"/>: where the target points on to.</param>
    public sealed record RootResolution(
        string? Root,
        RootOrigin Origin,
        string StartDir,
        string? GitRoot,
        string? OverridePath,
        IReadOnlyList<string> Skipped,
        string? Checkout = null,
        string? KnitRoot = null,
        RedirectFailure Failure = RedirectFailure.None,
        string? ChainTarget = null)
    {
        public bool Redirected => Origin == RootOrigin.Redirect;
    }

    /// <summary>
    /// A directory is a knit root when its <c>.knit/</c> holds a
    /// <c>config.json</c>. The name alone is not enough — anything unpacked
    /// into a dotfolder called <c>.knit</c> would otherwise capture every path
    /// below it. Existence only: a malformed config is a <c>verify</c>
    /// finding, never a reason to resolve somewhere else.
    /// </summary>
    public static bool IsRepo(string dir) =>
        File.Exists(Path.Combine(dir, KnitDirName, Config.ConfigLoader.FileName));

    private static bool LooksLikeKnitDir(string dir) =>
        Directory.Exists(Path.Combine(dir, KnitDirName));

    /// <summary>
    /// Nearest enclosing git repo root at or above <paramref name="startDir"/>,
    /// or null. <c>.git</c> counts as a file too — that is what a worktree has.
    /// </summary>
    public static string? FindGitRoot(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Resolve the knit root for a given starting directory.
    /// Precedence: explicit override > KNIT_ROOT env > the walked-to checkout's
    /// <see cref="KnitRootKey"/> > the walk itself. Returns null if no knit repo
    /// is found and no override is set, or a configured redirect is broken.
    /// </summary>
    public static string? Resolve(string startDir, string? explicitRoot = null)
        => ResolveDetailed(startDir, explicitRoot).Root;

    /// <summary>
    /// <see cref="Resolve"/> with the reasoning attached — see
    /// <see cref="RootResolution"/>.
    /// </summary>
    public static RootResolution ResolveDetailed(string startDir, string? explicitRoot = null)
    {
        var start = Path.GetFullPath(startDir);
        var gitRoot = FindGitRoot(start);

        if (!string.IsNullOrEmpty(explicitRoot))
            return Override(RootOrigin.Explicit, explicitRoot);

        var envRoot = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrEmpty(envRoot))
            return Override(RootOrigin.Environment, envRoot);

        // The walk stops at the enclosing git repo root: a knit repo is a git
        // repo's tracker, so a `.knit/` above the project you are standing in
        // is not yours by default. Outside a git repo there is no boundary.
        var skipped = new List<string>();
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (IsRepo(dir.FullName))
                return FollowRedirect(dir.FullName);
            if (LooksLikeKnitDir(dir.FullName))
                skipped.Add(KnitDir(dir.FullName));
            if (gitRoot is not null && PathsEqual(dir.FullName, gitRoot)) break;
            dir = dir.Parent;
        }
        return new RootResolution(null, RootOrigin.Walk, start, gitRoot, null, skipped);

        // An override names a root directly and does not read its knit_root —
        // an escape hatch that re-entered the mechanism could not escape a bad
        // redirect.
        RootResolution Override(RootOrigin origin, string path)
        {
            var resolved = Path.GetFullPath(path);
            var ok = IsRepo(resolved);
            var skippedOverride = !ok && LooksLikeKnitDir(resolved)
                ? new[] { KnitDir(resolved) }
                : Array.Empty<string>();
            return new RootResolution(
                ok ? resolved : null, origin, start, gitRoot, resolved, skippedOverride,
                Checkout: ok ? resolved : null, KnitRoot: ok ? ReadKnitRoot(resolved) : null);
        }

        // One hop, never chained: `knit root` flattens when it writes, so a
        // target that redirects again means something changed underneath —
        // refuse and name both rather than guess which one is meant.
        RootResolution FollowRedirect(string checkout)
        {
            var raw = ReadKnitRoot(checkout);
            var walked = new RootResolution(checkout, RootOrigin.Walk, start, gitRoot, null, skipped,
                Checkout: checkout, KnitRoot: raw);
            if (raw is null or LocalKnitRoot) return walked;

            var target = ResolveKnitRootPath(checkout, raw);
            var failure =
                !Directory.Exists(target) ? RedirectFailure.TargetMissing
                : !IsRepo(target) ? RedirectFailure.TargetNotARepo
                : RedirectFailure.None;
            string? chain = null;
            if (failure == RedirectFailure.None && ReadKnitRoot(target) is { } onward && onward != LocalKnitRoot)
            {
                failure = RedirectFailure.Chained;
                chain = ResolveKnitRootPath(target, onward);
            }
            return walked with
            {
                Root = failure == RedirectFailure.None ? target : null,
                Origin = RootOrigin.Redirect,
                OverridePath = target,
                Failure = failure,
                ChainTarget = chain,
            };
        }
    }

    /// <summary>
    /// A repo's own <see cref="KnitRootKey"/> from its <c>config.local.json</c>,
    /// or null when unset. An unreadable file reads as unset here — <c>knit
    /// verify</c> reports it — rather than as a redirect nobody can name.
    /// </summary>
    public static string? ReadKnitRoot(string repoRoot)
    {
        var node = ConfigLoader.ReadLayer(KnitDir(repoRoot), ConfigLoader.LocalFileName).Node;
        return node?[KnitRootKey] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0
            ? s.Trim()
            : null;
    }

    /// <summary>
    /// A <see cref="KnitRootKey"/> value as a full path. <c>knit root</c>
    /// always writes absolute paths; a relative one is a hand edit, taken
    /// relative to the checkout that holds it.
    /// </summary>
    public static string ResolveKnitRootPath(string checkout, string value) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(value, checkout));

    public static bool SamePath(string a, string b) => PathsEqual(Path.GetFullPath(a), Path.GetFullPath(b));

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static string KnitDir(string root) => Path.Combine(root, KnitDirName);
    public static string ItemsDir(string root) => Path.Combine(root, KnitDirName, BucketFolders.Items);
    public static string CacheDir(string root) => Path.Combine(root, KnitDirName, ".cache");

    /// <summary>
    /// Lazily create <c>.knit/.cache/</c> with a <c>.gitignore</c> ignoring
    /// everything inside. Call from the first feature that writes there
    /// (search index, sync markers). No-op if it already exists.
    /// </summary>
    public static string EnsureCacheDir(string root)
    {
        var dir = CacheDir(root);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AtomicFile.WriteAllText(Path.Combine(dir, ".gitignore"), "*\n");
        }
        return dir;
    }

    public static readonly string[] KnitGitignoreEntries =
    {
        "knit-feedback.md",
        Config.ConfigLoader.LocalFileName,
        RepoLock.FileName,
    };

    private const string KnitGitignoreHeader =
        "# Managed by `knit init`. Add lines manually if needed.\n" +
        "# knit-feedback.md — local feedback notes (see the knit skill).\n" +
        "# config.local.json — per-machine overlay (mounts, knit root).\n";

    /// <summary>
    /// Ensure <c>.knit/.gitignore</c> contains the standard knit-managed
    /// entries (local feedback notes + per-machine overlay). Idempotent:
    /// creates the file if missing, appends only missing lines if present,
    /// preserves user-added content. Assumes <c>.knit/</c> exists.
    /// </summary>
    public static void EnsureKnitGitignore(string root)
    {
        var path = Path.Combine(KnitDir(root), ".gitignore");
        if (!File.Exists(path))
        {
            AtomicFile.WriteAllText(path, KnitGitignoreHeader + string.Join("\n", KnitGitignoreEntries) + "\n");
            return;
        }

        var existing = AtomicFile.ReadAllText(path);
        var presentLines = existing.Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var missing = KnitGitignoreEntries.Where(e => !presentLines.Contains(e)).ToList();
        if (missing.Count == 0) return;

        var sb = new System.Text.StringBuilder(existing);
        if (existing.Length > 0 && !existing.EndsWith('\n')) sb.Append('\n');
        foreach (var entry in missing) sb.Append(entry).Append('\n');
        AtomicFile.WriteAllText(path, sb.ToString());
    }
}
