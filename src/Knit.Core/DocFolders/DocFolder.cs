namespace Knit.Core.DocFolders;

/// <summary>
/// Path + form helpers for a <em>doc folder</em> entry — a markdown doc
/// addressed by slug within a base directory, in one of two on-disk forms:
/// flat <c>&lt;base&gt;/&lt;slug&gt;.md</c> or folder
/// <c>&lt;base&gt;/&lt;slug&gt;/readme.md</c>. Shared by KB buckets
/// and project doc folders. Knows nothing of buckets, refs, or frontmatter —
/// callers layer their own addressing on top.
/// </summary>
public static class DocFolder
{
    public const string ReadmeFileName = "readme.md";

    /// <summary>The generated table-of-contents filename that is part of an
    /// entry (skipped as a blocker on delete, ignored on move).</summary>
    public const string IndexFileName = "index.md";

    public static string FlatPath(string baseDir, string slug) =>
        Path.Combine(baseDir, slug + ".md");

    public static string FolderReadmePath(string baseDir, string slug) =>
        Path.Combine(baseDir, slug, ReadmeFileName);

    /// <summary>True when a resolved path is the folder form's
    /// <c>readme.md</c>.</summary>
    public static bool IsFolderForm(string path) =>
        Path.GetFileName(path).Equals(ReadmeFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The existing on-disk path for a slug under <paramref name="baseDir"/>,
    /// or null when neither form exists. Prefers the flat form if — pathologically —
    /// both are present.</summary>
    public static string? Resolve(string baseDir, string slug)
    {
        var flat = FlatPath(baseDir, slug);
        if (File.Exists(flat)) return flat;
        var folder = FolderReadmePath(baseDir, slug);
        return File.Exists(folder) ? folder : null;
    }
}
