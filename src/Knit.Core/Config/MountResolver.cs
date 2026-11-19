using Knit.Core.Paths;

namespace Knit.Core.Config;

/// <summary>Which config layer a mount's effective value came from.</summary>
public enum MountSource
{
    Global,
    Repo,
    Local,
}

/// <param name="DeclaredPath">A value from a lower layer that this mount overrides.</param>
public sealed record MountShadow(MountSource Source, string DeclaredPath);

/// <param name="DeclaredPath">The value as written in the config it came from.</param>
/// <param name="EffectivePath">Absolute, canonicalised.</param>
/// <param name="Portable">
/// False when the declared value is an absolute path: it still resolves here,
/// but a committed config carrying it breaks on every other machine.
/// </param>
public sealed record MountInfo(
    string Name,
    string DeclaredPath,
    string EffectivePath,
    MountSource Source,
    bool Resolves,
    bool Portable)
{
    /// <summary>
    /// Same-named declarations this one wins over, lowest layer first.
    /// Shadowing is legitimate — "in this repo, `docs` means the vendored
    /// copy" — so it is reported where mounts are listed and never warned
    /// about per command.
    /// </summary>
    public IReadOnlyList<MountShadow> Shadowed { get; init; } = [];
}

/// <summary>
/// Named read-only directories outside the repo root that KB sources may point
/// into. A <b>mount</b> is deliberately not a <b>root</b>: there is
/// exactly one root (read + write, the authoritative <c>.knit/</c>) and any
/// number of mounts (read-only, viewing / previewing / opening only). No
/// absolute path from a request ever reaches the filesystem — a caller names a
/// mount and supplies a path inside it, and <see cref="PathContainment"/> is
/// applied against the resolved mount exactly as it is against the repo root.
/// </summary>
public static class MountResolver
{
    /// <summary>
    /// Stated in error messages, so keep it in step with <see cref="IsValidName"/>.
    /// The ≥2-char floor is what makes the prose form (<c>docs:specs/plan.md</c>)
    /// unambiguous: a drive letter can never parse as a mount name.
    /// </summary>
    public const string NameRule = "lowercase letters, digits and dashes, at least 2 characters";

    /// <summary>
    /// A path the global config may declare: absolute, or <c>~</c>-anchored.
    /// A repo-relative one (the portable form) resolves against whichever
    /// repo is being read, so the same global line would point at a different
    /// directory in every one of them.
    /// </summary>
    public static bool IsGlobalMountPath(string path) =>
        Path.IsPathRooted(path) || IsHomeAnchored(path);

    public static bool IsHomeAnchored(string path) =>
        path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal);

    private static string ExpandHome(string path)
    {
        if (!IsHomeAnchored(path)) return path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Length <= 1 ? home : Path.Combine(home, path[2..]);
    }

    public static bool IsValidName(string? name)
    {
        if (name is null || name.Length < 2) return false;
        if (name[0] == '-' || name[^1] == '-') return false;
        var prevDash = false;
        foreach (var c in name)
        {
            var ok = c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-';
            if (!ok) return false;
            if (c == '-' && prevDash) return false;
            prevDash = c == '-';
        }
        return true;
    }

    /// <summary>
    /// Every declared mount, local merged over repo per name, ordered by name.
    /// Takes the two layers separately because the report has to say which file
    /// a value came from — the merged config can no longer tell.
    /// </summary>
    public static IReadOnlyList<MountInfo> Describe(string root, KnitConfig? repo, KnitConfig? local)
        => Describe(root, null, repo, local);

    public static IReadOnlyList<MountInfo> Describe(
        string root, KnitConfig? global, KnitConfig? repo, KnitConfig? local)
    {
        var declared = new Dictionary<string, (string Path, MountSource Source)>(StringComparer.Ordinal);
        var shadowed = new Dictionary<string, List<MountShadow>>(StringComparer.Ordinal);
        // Lowest layer first, each overriding the one before and recording what
        // it displaced — local always wins when present, with no disk probing,
        // so the effective path never depends on whether a folder happens to
        // exist.
        foreach (var (layer, source) in new[]
                 {
                     (global, MountSource.Global), (repo, MountSource.Repo), (local, MountSource.Local),
                 })
        {
            foreach (var (name, path) in layer?.Mounts ?? [])
            {
                if (declared.TryGetValue(name, out var prev))
                {
                    shadowed.TryAdd(name, []);
                    shadowed[name].Add(new MountShadow(prev.Source, prev.Path));
                }
                declared[name] = (path, source);
            }
        }

        return declared
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => Describe(root, kv.Key, kv.Value.Path, kv.Value.Source) with
            {
                Shadowed = shadowed.TryGetValue(kv.Key, out var list) ? list : [],
            })
            .ToList();
    }

    public static MountInfo Describe(string root, string name, string declaredPath, MountSource source)
    {
        var rooted = IsGlobalMountPath(declaredPath);
        string effective;
        try
        {
            effective = rooted
                ? Path.GetFullPath(ExpandHome(declaredPath))
                : Path.GetFullPath(Path.Combine(Path.GetFullPath(root), declaredPath));
        }
        catch
        {
            return new MountInfo(name, declaredPath, declaredPath, source, Resolves: false, Portable: !rooted);
        }
        return new MountInfo(name, declaredPath, effective, source, Directory.Exists(effective), Portable: !rooted);
    }

    /// <summary>
    /// Resolve a mount's directory from the effective (merged) config. Returns
    /// false with a reason for an unknown or unresolved mount — the caller
    /// surfaces it; an unresolved mount is a config problem, never a broken
    /// KB entry.
    /// </summary>
    public static bool TryResolveDir(
        string root, KnitConfig? effective, string name, out string dir, out string? error)
    {
        dir = "";
        if (!IsValidName(name))
        {
            error = $"invalid mount name '{name}' — {NameRule}";
            return false;
        }
        if (effective?.Mounts is null || !effective.Mounts.TryGetValue(name, out var declared))
        {
            error = $"unknown mount '{name}' — declare it with `knit mount add {name} <path>`";
            return false;
        }
        var info = Describe(root, name, declared, MountSource.Repo);
        if (!info.Resolves)
        {
            error = $"mount '{name}' unresolved";
            return false;
        }
        dir = info.EffectivePath;
        error = null;
        return true;
    }

    /// <summary>
    /// Resolve <paramref name="relPath"/> inside <paramref name="mount"/>.
    /// The symlink guard is kept as for the repo root: a mount is read-only,
    /// but a link inside one would still redirect a read to anywhere on the
    /// machine, and the API is unauthenticated.
    /// </summary>
    public static bool TryResolveFile(
        string root, KnitConfig? effective, string mount, string relPath,
        out string fullPath, out string? error)
    {
        fullPath = "";
        if (!TryResolveDir(root, effective, mount, out var dir, out error)) return false;

        // A leading `/` is absolute on Unix; trimming it would quietly turn
        // `/etc/x` into a mount-relative path.
        var normalized = relPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            error = "absolute paths are not allowed";
            return false;
        }
        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(dir, normalized)); }
        catch (Exception ex) { error = ex.Message; return false; }

        if (!PathContainment.IsContained(dir, candidate) || PathContainment.HasSymlinkBelow(dir, candidate))
        {
            error = $"path escapes mount '{mount}'";
            return false;
        }
        fullPath = candidate;
        error = null;
        return true;
    }

    /// <summary>
    /// Where a KB source's <c>path</c> actually lives. <see cref="Reason"/> is
    /// null when it resolved; otherwise it carries the config problem in the
    /// same wording the API and the SPA use, and <see cref="FullPath"/> is empty.
    /// </summary>
    public sealed record SourceTarget(string FullPath, string? Mount, string? Reason);

    /// <summary>
    /// Resolve a source's stored locator. Without a mount the path is taken
    /// relative to the repo root and <b>not</b> containment-checked: the CLI is
    /// not a trust boundary (the caller already has a shell), and a legacy
    /// <c>../</c> path has to stay resolvable or there is no way to see where it
    /// points while adopting it into a mount. The web API keeps its own guard.
    /// </summary>
    public static SourceTarget ResolveSource(
        string root, KnitConfig? effective, string? mount, string path)
    {
        if (string.IsNullOrEmpty(mount))
        {
            try { return new SourceTarget(Absolute(root, path), null, null); }
            catch (Exception ex) { return new SourceTarget("", null, ex.Message); }
        }
        return TryResolveFile(root, effective, mount, path, out var full, out var error)
            ? new SourceTarget(full, mount, null)
            : new SourceTarget("", mount, error);
    }

    /// <summary>
    /// Declared mounts whose directory lexically contains
    /// <paramref name="fullPath"/>. Lexical on purpose — the answer must not
    /// depend on which folders happen to exist, so an unresolved mount still
    /// claims the paths under it.
    /// </summary>
    public static IReadOnlyList<MountInfo> FindCovering(
        string root, KnitConfig? effective, string fullPath)
    {
        if (effective?.Mounts is null) return [];
        return effective.Mounts
            .Where(kv => IsValidName(kv.Key))
            .Select(kv => Describe(root, kv.Key, kv.Value, MountSource.Repo))
            .Where(m => PathContainment.IsContained(m.EffectivePath, fullPath))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The stored, mount-relative form of an absolute path inside a mount.</summary>
    public static string RelativeToMount(MountInfo mount, string fullPath)
        => Path.GetRelativePath(mount.EffectivePath, fullPath).Replace('\\', '/');

    /// <summary>
    /// True when a locator is meant as a literal filesystem path rather than a
    /// repo- or mount-relative one: rooted, or climbing out with <c>..</c>.
    /// That is the form auto-detection rebases onto a mount; a plain relative
    /// path is left exactly as the caller wrote it.
    /// </summary>
    public static bool IsLiteralPath(string root, string path)
    {
        if (Path.IsPathRooted(path)) return true;
        try { return !PathContainment.IsContained(Path.GetFullPath(root), Absolute(root, path)); }
        catch { return false; }
    }

    /// <param name="Ambiguous">Several mounts cover the path — the caller has
    /// to name one; separated out because it is a conflict, not bad input.</param>
    public sealed record MountRebase(string? Path, string? Mount, string? Error, bool Ambiguous = false);

    /// <summary>
    /// Turn a literal filesystem path into the stored mount-relative form:
    /// the caller passes the path they can actually see and the mount
    /// it rides on is found, so the entry stays repointable without anyone
    /// having to split the path by hand. Shared by the CLI and the web API so
    /// the wording can't drift. Only for paths <see cref="IsLiteralPath"/>
    /// accepts — a plain repo-relative path is stored verbatim.
    /// </summary>
    public static MountRebase RebaseLiteral(
        string root, KnitConfig? effective, string declared, string? mount)
    {
        var target = ResolveSource(root, effective, null, declared);
        if (target.Reason is not null) return new MountRebase(null, null, target.Reason);

        var covering = FindCovering(root, effective, target.FullPath);
        if (!string.IsNullOrEmpty(mount))
        {
            var named = covering.FirstOrDefault(m => m.Name == mount);
            return named is null
                ? new MountRebase(null, null,
                    $"'{declared}' is outside mount '{mount}' — pass a path inside it, or omit the mount to have it found")
                : new MountRebase(RelativeToMount(named, target.FullPath), mount, null);
        }
        if (covering.Count == 0)
            return new MountRebase(null, null,
                $"'{declared}' leaves the repo root and no declared mount covers it"
                + " — declare one with `knit mount add <name> <path>`");
        if (covering.Count > 1)
            return new MountRebase(null, null,
                $"'{declared}' is covered by {string.Join(" and ", covering.Select(m => $"mount '{m.Name}'"))}"
                + " — name the one you mean",
                Ambiguous: true);
        return new MountRebase(RelativeToMount(covering[0], target.FullPath), covering[0].Name, null);
    }

    private static string Absolute(string root, string path)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Path.GetFullPath(root), path));
}
