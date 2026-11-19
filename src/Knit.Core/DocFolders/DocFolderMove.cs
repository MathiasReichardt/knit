namespace Knit.Core.DocFolders;

/// <summary>One sub-doc that travelled with a folder-form move.
/// <see cref="RelativeSuffix"/> is the slug-relative path (separators
/// normalized to <c>/</c>, <c>.md</c> / <c>/readme.md</c> stripped) the caller
/// appends to the new parent slug to rebuild the sub-doc's ref.</summary>
public sealed record DocSubMove(string OldPath, string NewPath, string RelativeSuffix);

/// <summary>
/// Outcome of a doc-folder move. For a flat entry <see cref="SubDocs"/> is
/// empty. For a folder-form move it lists every sub-doc under the moved folder
/// (excluding the parent <c>readme.md</c> and the generated index) so the
/// caller can rewrite inbound refs to each. <see cref="Assets"/> are the files a
/// flat entry carried to its new folder (<see cref="DocAssets"/>); a folder
/// move carries everything in it.
/// </summary>
public sealed record DocFolderMoveResult(
    string OldPath, string NewPath, bool WasFolderForm, IReadOnlyList<DocSubMove> SubDocs,
    IReadOnlyList<(string OldPath, string NewPath)> Assets);

/// <summary>
/// Plain file / folder move for a doc-folder entry, extracted from
/// <c>KbRename</c>. Moves the flat file or the whole folder,
/// creates parent directories, and returns sub-doc old→new pairs. It does
/// <b>no</b> ref repair and no conflict / policy checks — the caller resolves
/// the source, computes the destination, guards against clobbering, and rewrites
/// whatever refs its domain tracks.
/// </summary>
public static class DocFolderMove
{
    /// <summary>
    /// Move <paramref name="oldPath"/> to <paramref name="newPath"/>. Folder
    /// form is keyed on the source being a <c>readme.md</c>; the caller must
    /// pass a <c>readme.md</c> destination in that case (the whole folder
    /// travels). Assumes the destination does not already exist.
    /// </summary>
    public static DocFolderMoveResult Move(
        string oldPath, string newPath, string indexFileName = DocFolder.IndexFileName)
    {
        if (DocFolder.IsFolderForm(oldPath))
        {
            var oldFolder = Path.GetDirectoryName(oldPath)!;
            var newFolder = Path.GetDirectoryName(newPath)!;
            // Capture sub-docs before the move — afterwards the old folder is gone.
            var subs = CollectSubDocs(oldFolder, newFolder, indexFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(newFolder)!);
            Directory.Move(oldFolder, newFolder);
            return new DocFolderMoveResult(oldPath, newPath, true, subs, []);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
        return new DocFolderMoveResult(oldPath, newPath, false, [], DocAssets.Carry(oldPath, newPath));
    }

    private static List<DocSubMove> CollectSubDocs(string oldFolder, string newFolder, string indexFileName)
    {
        var list = new List<DocSubMove>();
        var rootReadme = Path.GetFullPath(Path.Combine(oldFolder, DocFolder.ReadmeFileName));
        foreach (var file in Directory.EnumerateFiles(oldFolder, "*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, indexFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(Path.GetFullPath(file), rootReadme, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(oldFolder, file);
            var relSlash = rel.Replace('\\', '/');
            string suffix;
            if (relSlash.EndsWith("/readme.md", StringComparison.OrdinalIgnoreCase))
                suffix = relSlash[..^"/readme.md".Length];
            else if (relSlash.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                suffix = relSlash[..^3];
            else continue;

            list.Add(new DocSubMove(file, Path.Combine(newFolder, rel), suffix));
        }
        return list;
    }
}
