using Knit.Core.Result;

namespace Knit.Core.DocFolders;

/// <summary>
/// The result of partitioning a doc-folder delete. For a flat entry
/// <see cref="MarkdownTargets"/> is just the file itself. For a folder-form
/// entry, <see cref="Blockers"/> are the sibling files (sub-docs / attachments)
/// that force <c>deleteFolder</c>, and <see cref="MarkdownTargets"/> is every
/// <c>.md</c> under the folder except the generated index — the set of paths a
/// caller maps back to its own ref system to strip inbound links.
/// </summary>
public sealed record DocFolderDeletePlan(
    string TargetPath,
    bool IsFolderForm,
    string? Folder,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> MarkdownTargets);

/// <summary>
/// Containment-guarded, reparse-safe delete of a doc-folder entry (flat file or
/// folder-form <c>readme.md</c>), extracted from <c>KbDelete</c>.
/// <see cref="Plan"/> validates + partitions without touching the filesystem;
/// the caller strips its own inbound refs; then <see cref="Execute"/> performs
/// the guarded unlink. Splitting the two preserves the "strip refs before the
/// unlink" ordering so an FS fault can't leave a ref pointing at a gone entry.
/// </summary>
public static class DocFolderDelete
{
    /// <summary>
    /// Validate containment and partition the entry. Refuses (error) when the
    /// resolved path escapes <paramref name="containmentRoot"/> or a folder-form
    /// entry contains a symlink / junction — a reparse point must never be
    /// traversed by the following <c>Directory.Delete</c>.
    /// </summary>
    public static OperationResult<DocFolderDeletePlan> Plan(
        string containmentRoot, string targetPath, string indexFileName = DocFolder.IndexFileName)
    {
        var root = Path.GetFullPath(containmentRoot);
        if (!IsContained(root, ResolveFinal(targetPath)))
            return OperationResult<DocFolderDeletePlan>.Error(KnitError.BadArgument(
                $"refusing to delete: resolved path escapes {root}"));

        if (!DocFolder.IsFolderForm(targetPath))
            return OperationResult<DocFolderDeletePlan>.Ok(
                new DocFolderDeletePlan(targetPath, false, null, [], [targetPath]));

        var folder = Path.GetDirectoryName(targetPath)!;
        if (!IsContained(root, ResolveFinal(folder)))
            return OperationResult<DocFolderDeletePlan>.Error(KnitError.BadArgument(
                $"refusing to delete: folder escapes {root}"));

        var entryReadme = Path.GetFullPath(targetPath);
        var blockers = new List<string>();
        var mdTargets = new List<string>();
        var reparse = WalkFolder(folder, entryReadme, indexFileName, blockers, mdTargets);
        if (reparse is not null)
            return OperationResult<DocFolderDeletePlan>.Error(KnitError.BadArgument(
                $"refusing to delete: folder contains a symlink/junction ({Path.GetRelativePath(folder, reparse).Replace('\\', '/')})"));

        return OperationResult<DocFolderDeletePlan>.Ok(
            new DocFolderDeletePlan(targetPath, true, folder, blockers, mdTargets));
    }

    /// <summary>
    /// Perform the unlink for a planned entry. Returns <c>true</c> when the
    /// entry was removed, <c>false</c> when it's a folder-form entry with
    /// blockers and <paramref name="deleteFolder"/> is false (caller must
    /// re-prompt). Re-checks containment defensively before the FS op.
    /// </summary>
    public static OperationResult<bool> Execute(
        string containmentRoot, DocFolderDeletePlan plan, bool deleteFolder)
    {
        var root = Path.GetFullPath(containmentRoot);
        if (!plan.IsFolderForm)
        {
            if (!IsContained(root, ResolveFinal(plan.TargetPath)))
                return OperationResult<bool>.Error(KnitError.BadArgument(
                    $"refusing to delete: resolved path escapes {root}"));
            File.Delete(plan.TargetPath);
            return OperationResult<bool>.Ok(true);
        }

        if (plan.Blockers.Count > 0 && !deleteFolder)
            return OperationResult<bool>.Ok(false);

        if (!IsContained(root, ResolveFinal(plan.Folder!)))
            return OperationResult<bool>.Error(KnitError.BadArgument(
                $"refusing to delete: folder escapes {root}"));
        Directory.Delete(plan.Folder!, recursive: true);
        return OperationResult<bool>.Ok(true);
    }

    /// <summary>
    /// Recursively partition a folder-form entry into blockers + <c>.md</c> ref
    /// targets, WITHOUT following reparse points. Returns the path of the first
    /// symlink / junction encountered (caller refuses the delete), or null when
    /// the tree is clean.
    /// </summary>
    private static string? WalkFolder(
        string folder, string entryReadme, string indexFileName,
        List<string> blockers, List<string> mdTargets)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attrs = File.GetAttributes(entry);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return entry;
            if ((attrs & FileAttributes.Directory) != 0)
            {
                var found = WalkFolder(entry, entryReadme, indexFileName, blockers, mdTargets);
                if (found is not null) return found;
                continue;
            }
            var name = Path.GetFileName(entry);
            var isEntryReadme = string.Equals(Path.GetFullPath(entry), entryReadme, StringComparison.OrdinalIgnoreCase);
            var isGeneratedIndex = string.Equals(name, indexFileName, StringComparison.OrdinalIgnoreCase);
            if (!isEntryReadme && !isGeneratedIndex)
                blockers.Add(entry);
            if (!isGeneratedIndex && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                mdTargets.Add(entry);
        }
        return null;
    }

    /// <summary>Resolve a symlink chain to its final target; plain paths pass
    /// through. Best-effort — a stat failure falls back to the literal path,
    /// which the containment check then rejects if it's outside the root.</summary>
    private static string ResolveFinal(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null) return Path.GetFullPath(target.FullName);
            }
        }
        catch { /* fall through to the literal path */ }
        return full;
    }

    // OS-aware case sensitivity, prefix match on a separator-suffixed root so a
    // sibling like `kb-other/` can't pass as `kb/`.
    private static bool IsContained(string root, string candidate)
    {
        var sep = Path.DirectorySeparatorChar.ToString();
        var rootWithSep = root.EndsWith(sep, StringComparison.Ordinal) ? root : root + sep;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(rootWithSep, cmp) || string.Equals(candidate, root, cmp);
    }
}
