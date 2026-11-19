using Knit.Core.Ids;

namespace Knit.Core.Frontmatter;

/// <summary>
/// Helpers for relative-path round-tripping used by the <c>## Links</c>
/// table. The table's <c>Target</c> column is always a relative markdown
/// path from the source file. Item targets are flat siblings,
/// <c>&lt;id-slug&gt;.md</c>. KB targets sit under
/// <c>kb/&lt;bucket&gt;/...</c>.
/// </summary>
public static class LinkPath
{
    /// <summary>
    /// Extract the bare id from an item folder / flat-file name
    /// (<c>&lt;id&gt;-&lt;slug&gt;</c>). NanoIDs have no internal hyphens,
    /// so the id is everything before the first <c>-</c>.
    /// </summary>
    public static string? IdFromFolderName(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        // Strip a trailing .md if present so flat-form filenames work too.
        if (folder.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            folder = folder[..^3];
        var dash = folder.IndexOf('-');
        return dash < 0 ? folder : folder[..dash];
    }

    /// <summary>
    /// Extract an item id from a relative target path
    /// (<c>../&lt;id-slug&gt;.md</c>). Returns null when
    /// the path doesn't look like an item link (e.g. KB references).
    /// </summary>
    public static string? ItemIdFromTarget(string targetPath)
    {
        var candidate = CandidateIdFromTarget(targetPath);
        // Item ids are NanoIDs. A target that doesn't extract to a valid
        // NanoID is not an item link (e.g. a KB ref like ../kb/features/foo.md,
        // whose leaf "foo" would otherwise be mistaken for an id). Enforcing
        // this keeps the SPA from rendering KB-ref rows as clickable items.
        return candidate is not null && NanoId.IsValid(candidate) ? candidate : null;
    }

    /// <summary>
    /// True when <paramref name="targetPath"/> is shaped like a sibling item
    /// target (a <c>.md</c> path, not under
    /// <c>kb/</c>) — regardless of whether the extracted id is valid. Lets the
    /// verifier distinguish "meant to be an item link but malformed id" from
    /// "not an item link at all".
    /// </summary>
    public static bool LooksLikeItemTarget(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return false;
        var norm = targetPath.Replace('\\', '/');
        if (!norm.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var seg in norm.Split('/'))
            if (string.Equals(seg, "kb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(seg, "projects", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Extract the <c>(project, slug)</c> identity from a relative target
    /// path pointing at a milestone sub-doc
    /// (<c>.../projects/&lt;project&gt;/milestones/&lt;slug&gt;.md</c>).
    /// Returns null when the path isn't milestone-shaped.
    /// </summary>
    public static (string Project, string Slug)? MilestoneFromTarget(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return null;
        var norm = targetPath.Replace('\\', '/');
        if (!norm.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;
        var segs = norm.Split('/');
        for (var i = 0; i + 3 < segs.Length; i++)
        {
            if (string.Equals(segs[i], "projects", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segs[i + 2], "milestones", StringComparison.OrdinalIgnoreCase)
                && i + 3 == segs.Length - 1)
            {
                return (segs[i + 1], segs[i + 3][..^3]);
            }
        }
        return null;
    }

    /// <summary>
    /// The raw id candidate extracted from a target's leaf segment, without
    /// the NanoID validity gate. Null for non-<c>.md</c> paths.
    /// </summary>
    public static string? CandidateIdFromTarget(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return null;
        var norm = targetPath.Replace('\\', '/');
        return norm.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? IdFromFolderName(norm.Split('/')[^1]) : null;
    }

    /// <summary>
    /// Build the relative path from a source file's directory to a
    /// target file. The result uses forward slashes regardless of host
    /// OS — markdown viewers expect that.
    /// </summary>
    public static string Relative(string fromDir, string toFile)
        => Path.GetRelativePath(fromDir, toFile).Replace('\\', '/');
}
