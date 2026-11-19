namespace Knit.Core.Paths;

/// <summary>
/// Confirms a canonicalised candidate path stays within an allowed root.
/// Used by the web endpoints that map a request-supplied path/subpath onto
/// the filesystem (file view, KB folder) so a <c>..</c> escape can't read or
/// probe outside the repo. Both inputs must already be absolute and
/// normalised (e.g. via <see cref="System.IO.Path.GetFullPath(string)"/>).
/// </summary>
public static class PathContainment
{
    public static bool IsContained(string root, string candidate)
    {
        var sep = Path.DirectorySeparatorChar.ToString();
        var rootWithSep = root.EndsWith(sep, StringComparison.Ordinal) ? root : root + sep;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(rootWithSep, cmp)
            || string.Equals(candidate, root, cmp);
    }

    /// <summary>
    /// True if any path segment strictly below <paramref name="root"/> down to
    /// <paramref name="candidate"/> is a symlink/junction (reparse point).
    /// <see cref="IsContained"/> only guards the path lexically, but a symlink
    /// committed inside the repo could still redirect a read outside the root;
    /// the endpoints reject such paths outright since a symlink under the repo
    /// is never a legitimate artifact. <paramref name="root"/> itself is trusted
    /// and not inspected (the configured root may legitimately sit under a
    /// symlinked home dir). Both inputs must be absolute and canonicalised, and
    /// <paramref name="candidate"/> is assumed already contained in the root.
    /// </summary>
    public static bool HasSymlinkBelow(string root, string candidate)
    {
        var rel = Path.GetRelativePath(root, candidate);
        if (rel.Length == 0 || rel == ".") return false;
        var current = root;
        foreach (var segment in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            // A missing segment means the rest can't exist either — stop walking.
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
        return false;
    }
}
