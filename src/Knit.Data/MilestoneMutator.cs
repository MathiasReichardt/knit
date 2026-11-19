using Knit.Core.DocFolders;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;
using Knit.Core.Result;

namespace Knit.Data;

/// <summary>
/// Rename, move and delete of milestone sub-docs, orchestrating the pure-FS core
/// (<see cref="MilestoneEdit"/>) with the item store so backlinks stay
/// consistent. Rename and move <em>re-point</em> the <c>Milestone</c> rows in items
/// that target the milestone (the milestone still exists, so dangling would
/// be wrong); delete <em>leaves them dangling</em> by default (matching
/// <c>knit delete</c> / <see cref="ProjectRemover"/> and the "nothing
/// vanishes" verify/doctor invariant), scrubbing only when asked.
/// Frontmatter/body edits need no item work — call <see cref="MilestoneEdit.Apply"/>
/// directly.
/// </summary>
public static class MilestoneMutator
{
    public sealed record RenameResult(
        string Project,
        string OldSlug,
        string NewSlug,
        string OldPath,
        string NewPath,
        IReadOnlyList<string> RepointedItemIds,
        IReadOnlyList<string> FilesRelinked);

    public sealed record MoveResult(
        string FromProject,
        string ToProject,
        string OldSlug,
        string NewSlug,
        string OldPath,
        string NewPath,
        IReadOnlyList<string> RepointedItemIds,
        IReadOnlyList<string> CrossProjectItemIds,
        IReadOnlyList<string> FilesRelinked);

    public sealed record DeleteResult(
        string Project,
        string Slug,
        string DeletedPath,
        IReadOnlyList<string> DanglingItemIds,
        IReadOnlyList<string> ScrubbedItemIds);

    public static OperationResult<RenameResult> Rename(ItemStore store, string root, string project, string oldSlug,
        string newSlug)
    {
        var moved = MilestoneEdit.Rename(root, project, oldSlug, newSlug);
        if (moved.IsError) return OperationResult<RenameResult>.Error(moved.GetErrorOrDefault()!);
        var (oldPath, newPath) = moved.GetValueOrThrow();
        var normSlug = ProjectsLayout.SlugFromMilestonePath(newPath);

        var repointed = RepointMilestoneRows(store, oldPath, newPath);
        var relinked = new ProseRefRewriter(store, root).RewriteProse([(oldPath, newPath)], []);

        return OperationResult<RenameResult>.Ok(
            new RenameResult(project, oldSlug, normSlug, oldPath, newPath,
                repointed.OrderBy(id => id, StringComparer.Ordinal).ToList(), relinked));
    }

    /// <summary>
    /// Move a milestone into another project, optionally renaming it on the
    /// way (a slug collision in the destination without a new slug is an
    /// error). Items keep their <c>Milestone</c> link — the rows are
    /// re-pointed, never dropped — so an item may legitimately end up
    /// serving another project's milestone;
    /// <see cref="MoveResult.CrossProjectItemIds"/> reports which, and
    /// <c>knit verify</c> lists them as informational.
    /// </summary>
    public static OperationResult<MoveResult> Move(ItemStore store, string root,
        string fromProject, string slug, string toProject, string? newSlug)
    {
        var moved = MilestoneEdit.Move(root, fromProject, slug, toProject, newSlug ?? slug);
        if (moved.IsError) return OperationResult<MoveResult>.Error(moved.GetErrorOrDefault()!);
        var (oldPath, newPath) = moved.GetValueOrThrow();
        var normSlug = ProjectsLayout.SlugFromMilestonePath(newPath);

        var repointed = RepointMilestoneRows(store, oldPath, newPath);
        var relinked = new ProseRefRewriter(store, root)
            .RewriteProse([(oldPath, newPath), .. DocAssets.Carry(oldPath, newPath)], []);

        var crossProject = repointed
            .Select(store.Read)
            .OfType<Item>()
            .Where(i => !string.Equals(MilestoneResolver.ProjectScopeOf(store, i), toProject, StringComparison.Ordinal))
            .Select(i => i.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return OperationResult<MoveResult>.Ok(
            new MoveResult(fromProject, toProject, slug, normSlug, oldPath, newPath,
                repointed.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                crossProject, relinked));
    }

    public static OperationResult<DeleteResult> Delete(ItemStore store, string root, string project, string slug,
        bool scrubLinks)
    {
        var backlinks = MilestoneResolver.LinkedItems(store, project, slug);

        var scrubbed = new List<string>();
        if (scrubLinks)
        {
            foreach (var item in backlinks)
            {
                var kept = item.LinkRows.Where(r => !IsMilestoneRow(r, project, slug)).ToList();
                store.Write(item with { LinkRows = kept });
                scrubbed.Add(item.Id);
            }
        }

        var del = MilestoneEdit.Delete(root, project, slug);
        if (del.IsError) return OperationResult<DeleteResult>.Error(del.GetErrorOrDefault()!);

        var danglingIds = backlinks.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        return OperationResult<DeleteResult>.Ok(
            new DeleteResult(project, slug, del.GetValueOrThrow(),
                scrubLinks ? [] : danglingIds,
                scrubbed.OrderBy(id => id, StringComparer.Ordinal).ToList()));
    }

    // Re-point every Milestone row whose resolved target is the old milestone
    // file to a freshly computed source-relative path to the new file. The
    // TargetText (the milestone title captured at link time) is unchanged — a
    // slug rename doesn't touch the title.
    private static IReadOnlyList<string> RepointMilestoneRows(ItemStore store, string oldAbsPath, string newAbsPath)
    {
        var oldNorm = Path.GetFullPath(oldAbsPath).Replace('\\', '/');
        var changed = new List<string>();
        foreach (var item in store.AllItems())
        {
            if (!item.MilestoneRows.Any()) continue;
            var file = store.FindItemFile(item.Id);
            if (file is null) continue;
            var sourceDir = Path.GetDirectoryName(file)!;

            var rows = item.LinkRows.ToList();
            var didChange = false;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Rel != LinkRel.Milestone) continue;
                var abs = Path.GetFullPath(Path.Combine(sourceDir, rows[i].TargetPath)).Replace('\\', '/');
                if (!string.Equals(abs, oldNorm, StringComparison.OrdinalIgnoreCase)) continue;
                rows[i] = rows[i] with { TargetPath = LinkPath.Relative(sourceDir, newAbsPath) };
                didChange = true;
            }
            if (!didChange) continue;
            store.Write(item with { LinkRows = rows });
            changed.Add(item.Id);
        }
        return changed;
    }

    private static bool IsMilestoneRow(LinkRow r, string project, string slug) =>
        r.Rel == LinkRel.Milestone
        && LinkPath.MilestoneFromTarget(r.TargetPath) is { } id
        && string.Equals(id.Project, project, StringComparison.Ordinal)
        && string.Equals(id.Slug, slug, StringComparison.Ordinal);
}
