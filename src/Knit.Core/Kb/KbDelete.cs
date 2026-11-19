using Knit.Core.DocFolders;
using Knit.Core.Refs;
using Knit.Core.Result;

namespace Knit.Core.Kb;

/// <summary>
/// Read-only look at what a delete would touch — drives the CLI prompt and
/// the SPA confirmation dialog. <see cref="BlockingFiles"/> is the list of
/// sibling files (sub-docs / attachments) that force <c>deleteFolder</c>;
/// <see cref="ItemsAffected"/> is the items whose <c>KB ref</c> rows would
/// be stripped.
/// </summary>
public sealed record KbDeletePreview(
    Ref Ref,
    bool IsFolderForm,
    string TargetPath,
    IReadOnlyList<string> BlockingFiles,
    IReadOnlyList<string> ItemsAffected,
    IReadOnlyList<string> KbEntriesAffected);

/// <summary>
/// Outcome of a delete. When <see cref="BlockingFiles"/> is non-empty the
/// entry was <b>not</b> touched — it's a folder-form entry with sibling
/// files and the caller didn't pass <c>deleteFolder</c>. Otherwise the
/// entry was removed and <see cref="ItemsAffected"/> lists the items whose
/// <c>KB ref</c> rows were stripped.
/// </summary>
public sealed record KbDeleteResult(
    Ref Ref,
    string? DeletedPath,
    bool DeletedFolder,
    IReadOnlyList<string> ItemsAffected,
    IReadOnlyList<string> BlockingFiles,
    IReadOnlyList<string> KbEntriesAffected);

/// <summary>
/// Delete a KB artifact (file or folder-form entry) and strip every
/// <c>KB ref</c> row in items that pointed at it. The only deletable shape
/// is an entry addressed by its ref — folders aren't first-class, so the
/// only recursive case is "the folder belonging to a folder-form entry".
/// Symmetric with <see cref="KbRename"/>: derived item state stays
/// consistent with the authored files. The containment-guarded FS mechanics
/// live in <see cref="DocFolderDelete"/> (shared with projects);
/// KB layers its bucket / ref addressing on top.
/// </summary>
public static class KbDelete
{
    public static OperationResult<KbDeletePreview> Preview(string repoRoot, Ref r, ItemRefRemover remover)
    {
        var plan = Resolve(repoRoot, r);
        if (plan.IsError)
            return OperationResult<KbDeletePreview>.Error(plan.GetErrorOrDefault()!);
        var p = plan.GetValueOrThrow();
        var affected = remover.FindItemsReferencing(p.Doc.MarkdownTargets);
        var kbAffected = new KbFrontmatterRefRewriter(new KbStore(repoRoot)).FindEntriesReferencing(p.RemovedRefs);
        var target = p.Doc.IsFolderForm ? p.Doc.Folder! : p.Doc.TargetPath;
        return OperationResult<KbDeletePreview>.Ok(
            new KbDeletePreview(p.CanonicalRef, p.Doc.IsFolderForm, target, p.Doc.Blockers, affected, kbAffected));
    }

    public static OperationResult<KbDeleteResult> Delete(
        string repoRoot, Ref r, bool deleteFolder, ItemRefRemover remover)
    {
        var plan = Resolve(repoRoot, r);
        if (plan.IsError)
            return OperationResult<KbDeleteResult>.Error(plan.GetErrorOrDefault()!);
        var p = plan.GetValueOrThrow();

        // Folder-form entry with sibling files and no deleteFolder — report the
        // blockers without touching anything (nothing stripped, nothing gone).
        if (p.Doc.IsFolderForm && p.Doc.Blockers.Count > 0 && !deleteFolder)
            return OperationResult<KbDeleteResult>.Ok(
                new KbDeleteResult(p.CanonicalRef, null, false, [], p.Doc.Blockers, []));

        // Strip both the item KB-ref rows and the KB-to-KB frontmatter refs
        // BEFORE the unlink, so an FS fault can't leave a ref pointing at an
        // entry that's already gone.
        var kbRewriter = new KbFrontmatterRefRewriter(new KbStore(repoRoot));
        var affected = remover.RemoveKbRefs(p.Doc.MarkdownTargets);
        var kbAffected = kbRewriter.RemoveFrontmatterRefs(p.RemovedRefs);

        var exec = DocFolderDelete.Execute(KbLayout.KbDir(repoRoot), p.Doc, deleteFolder);
        if (exec.IsError)
            return OperationResult<KbDeleteResult>.Error(exec.GetErrorOrDefault()!);

        var deletedPath = p.Doc.IsFolderForm ? p.Doc.Folder : p.Doc.TargetPath;
        return OperationResult<KbDeleteResult>.Ok(
            new KbDeleteResult(p.CanonicalRef, deletedPath, p.Doc.IsFolderForm, affected, [], kbAffected));
    }

    private sealed record Plan(Ref CanonicalRef, DocFolderDeletePlan Doc, IReadOnlyList<Ref> RemovedRefs);

    private static OperationResult<Plan> Resolve(string repoRoot, Ref r)
    {
        if (r.Bucket == Bucket.Item)
            return OperationResult<Plan>.Error(
                KnitError.BadArgument("kb delete does not operate on item refs"));

        var resolved = RefResolver.Resolve(r, repoRoot).GetValueOrThrow();
        if (!resolved.Exists)
            return OperationResult<Plan>.Error(KnitError.NotFound($"unresolved ref {r}"));

        // Containment + reparse guard + blocker partition live in the shared core.
        var docPlan = DocFolderDelete.Plan(KbLayout.KbDir(repoRoot), resolved.Path);
        if (docPlan.IsError)
            return OperationResult<Plan>.Error(docPlan.GetErrorOrDefault()!);
        var dp = docPlan.GetValueOrThrow();

        if (!dp.IsFolderForm)
            return OperationResult<Plan>.Ok(new Plan(resolved.Ref, dp, [resolved.Ref]));

        // KB-to-KB frontmatter refs are addressed by ref, not path: the parent
        // plus each sub-doc (bucket:parentSlug/suffix) may be another entry's
        // supersedes / superseded_by / feature target.
        var entryReadme = Path.GetFullPath(dp.TargetPath);
        var removedRefs = new List<Ref> { resolved.Ref };
        foreach (var t in dp.MarkdownTargets)
        {
            if (string.Equals(Path.GetFullPath(t), entryReadme, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(dp.Folder!, t).Replace('\\', '/');
            var suffix = rel.EndsWith("/readme.md", StringComparison.OrdinalIgnoreCase)
                ? rel[..^"/readme.md".Length]
                : rel[..^3]; // strip ".md"
            removedRefs.Add(new Ref(resolved.Ref.Bucket, $"{resolved.Ref.Slug}/{suffix}"));
        }
        return OperationResult<Plan>.Ok(new Plan(resolved.Ref, dp, removedRefs));
    }
}

/// <summary>
/// Strategy for removing (or counting) <c>KB ref</c> rows in items when
/// their target entry is deleted. Sibling to <see cref="ItemRefRewriter"/>;
/// kept narrow so <see cref="KbDelete"/> doesn't depend on ItemStore.
/// </summary>
public interface ItemRefRemover
{
    /// <summary>Strip every KB ref row whose resolved target is in the set;
    /// returns the ids of the items that changed.</summary>
    IReadOnlyList<string> RemoveKbRefs(IReadOnlyCollection<string> oldTargetPaths);

    /// <summary>Read-only: ids of items that reference any of the targets.
    /// Drives the pre-delete count without mutating anything.</summary>
    IReadOnlyList<string> FindItemsReferencing(IReadOnlyCollection<string> oldTargetPaths);
}
