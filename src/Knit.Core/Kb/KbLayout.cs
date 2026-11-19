using Knit.Core.Discovery;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// Path constants for <c>.knit/kb/</c>. All paths are computed from a knit
/// root — no static state. Folders are created lazily on first write
/// (per design Decision 9: nothing is pre-created on init).
/// </summary>
public static class KbLayout
{
    public const string KbDirName = "kb";
    public const string MasterIndexFile = "kb-index.md";
    public const string PerFolderIndexFile = "index.md";

    public static string KbDir(string repoRoot) =>
        Path.Combine(RepoDiscovery.KnitDir(repoRoot), KbDirName);

    public static string BucketDir(string repoRoot, Bucket bucket) =>
        Path.Combine(KbDir(repoRoot), bucket.Folder());

    public static string MasterIndexPath(string repoRoot) =>
        Path.Combine(KbDir(repoRoot), MasterIndexFile);

    public static string PerFolderIndexPath(string repoRoot, Bucket bucket) =>
        Path.Combine(BucketDir(repoRoot, bucket), PerFolderIndexFile);

    public static string FilePath(string repoRoot, Ref r) =>
        Path.Combine(BucketDir(repoRoot, r.Bucket), r.Slug + ".md");

    public static string FolderReadmePath(string repoRoot, Ref r) =>
        Path.Combine(BucketDir(repoRoot, r.Bucket), r.Slug, "readme.md");

    /// <summary>
    /// Create a bucket's folder on demand. Idempotent.
    /// </summary>
    public static string EnsureBucketDir(string repoRoot, Bucket bucket)
    {
        var dir = BucketDir(repoRoot, bucket);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Enumerate every KB artifact file across all buckets. Yields each
    /// entry's authoritative <c>.md</c> path — flat <c>&lt;slug&gt;.md</c>
    /// or folder <c>&lt;slug&gt;/readme.md</c>. Bucket / nested
    /// <c>index.md</c> files and per-folder <c>readme.md</c> indices are
    /// included as entries when they exist; the bucket-level
    /// <c>index.md</c> is the only file deliberately skipped (it's a
    /// generated table-of-contents). The master <c>kb-index.md</c> at
    /// the KB root is also skipped.
    /// </summary>
    public static IEnumerable<string> EnumerateArtifactFiles(string repoRoot)
    {
        var kbDir = KbDir(repoRoot);
        if (!Directory.Exists(kbDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(kbDir, "*.md", SearchOption.AllDirectories))
        {
            if (!IsArtifactFile(repoRoot, file)) continue;
            yield return file;
        }
    }

    /// <summary>
    /// True when the file is an authored KB entry (vs a generated
    /// bucket-level or master index file).
    /// </summary>
    public static bool IsArtifactFile(string repoRoot, string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (string.Equals(name, MasterIndexFile, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(name, PerFolderIndexFile, StringComparison.OrdinalIgnoreCase))
        {
            // Skip the bucket-level index (sibling of bucket folders).
            // A nested <slug>/index.md would be unusual — folder form uses
            // readme.md — so treat any index.md as the generated TOC.
            return false;
        }
        return true;
    }

    /// <summary>
    /// The directory under which entry-relative paths in a <c>## Links</c>
    /// table on this KB entry resolve from. For flat: the file's parent
    /// dir. For folder form: same — <c>readme.md</c> sits alongside
    /// siblings.
    /// </summary>
    public static string ArtifactSourceDir(string filePath) =>
        Path.GetDirectoryName(filePath)!;
}
