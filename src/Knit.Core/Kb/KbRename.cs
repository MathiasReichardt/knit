using Knit.Core.DocFolders;
using Knit.Core.Frontmatter;
using Knit.Core.Refs;
using Knit.Core.Result;

namespace Knit.Core.Kb;

public sealed record KbRenameResult(
    Ref OldRef,
    Ref NewRef,
    string OldPath,
    string NewPath,
    IReadOnlyList<string> ItemsRewritten,
    IReadOnlyList<string> KbEntriesAffected,
    IReadOnlyList<string> FilesRelinked);

/// <summary>
/// Rename a KB artifact (file or folder) and rewrite every <c>KB ref</c>
/// row in items pointing at the old path, KB-to-KB frontmatter refs, and
/// body-prose refs and links. The bucket cannot change.
/// </summary>
public static class KbRename
{
    public static OperationResult<KbRenameResult> Rename(
        string repoRoot,
        Ref oldRef,
        string newSlug,
        ItemRefRewriter rewriter,
        ProseRewriter proseRewriter)
    {
        if (string.IsNullOrWhiteSpace(newSlug))
            return OperationResult<KbRenameResult>.Error(KnitError.BadArgument("new slug cannot be empty"));
        // Slugify each segment so the rename target matches create semantics.
        var normSlug = string.Join('/',
            newSlug.Replace('\\', '/').Trim('/').Split('/').Select(Ids.Slug.From));
        if (normSlug.Contains('/'))
        {
            if (!oldRef.Bucket.AllowsGrowToFolder())
                return OperationResult<KbRenameResult>.Error(KnitError.BadArgument(
                    $"nested target slug is only valid for grow-to-folder buckets ({BucketFolders.GrowToFolderBuckets}); got {oldRef.Bucket.ToWire()}"));
            var lastSep = normSlug.LastIndexOf('/');
            var parentSlug = normSlug[..lastSep];
            var parentReadme = KbLayout.FolderReadmePath(repoRoot, new Ref(oldRef.Bucket, parentSlug));
            if (!File.Exists(parentReadme))
                return OperationResult<KbRenameResult>.Error(KnitError.BadArgument(
                    $"parent {oldRef.Bucket.ToWire()}:{parentSlug} must be in folder form — run `knit kb split` first"));
        }
        var newRef = new Ref(oldRef.Bucket, normSlug);

        // An ADR's id is the number in its slug, not the slug — a target
        // without one leaves the id underivable, so it is refused rather
        // than silently left stale.
        if (oldRef.Bucket == Bucket.Adr && KbIdentity.ExpectedId(Bucket.Adr, normSlug) is null)
            return OperationResult<KbRenameResult>.Error(KnitError.BadArgument(
                "an ADR's id is the number in its slug — the rename target must start with one " +
                "(e.g. `0007-retry-policy`)"));

        var oldResolved = RefResolver.Resolve(oldRef, repoRoot).GetValueOrThrow();
        if (!oldResolved.Exists)
            return OperationResult<KbRenameResult>.Error(KnitError.NotFound($"unresolved ref {oldRef}"));

        var newResolved = RefResolver.Resolve(newRef, repoRoot).GetValueOrThrow();
        if (newResolved.Exists)
            return OperationResult<KbRenameResult>.Error(KnitError.Conflict($"target {newRef} already exists at {newResolved.Path}"));

        var oldPath = oldResolved.Path;
        var changedItems = new List<string>();
        // old→new ref pairs (parent + every sub-doc) for the frontmatter-ref
        // rewrite — KB entries reference each other by ref, not by path.
        var renamePairs = new List<(Ref Old, Ref New)> { (oldRef, newRef) };

        var isFolderForm = oldRef.Bucket.AllowsGrowToFolder() && DocFolder.IsFolderForm(oldPath);
        var newPath = isFolderForm
            ? Path.Combine(KbLayout.BucketDir(repoRoot, oldRef.Bucket), normSlug, DocFolder.ReadmeFileName)
            : KbLayout.FilePath(repoRoot, newRef);

        // The shared core moves the flat file / whole folder and returns each
        // sub-doc's old→new path + slug-relative suffix; KB layers its ref
        // rewriting (item `## Links` rows + KB-to-KB frontmatter refs) on top.
        var move = DocFolderMove.Move(oldPath, newPath);
        foreach (var sub in move.SubDocs)
        {
            var subText = $"{BucketStrings.ToWire(newRef.Bucket)}:{newRef.Slug}/{sub.RelativeSuffix}";
            changedItems.AddRange(rewriter.RewriteKbRefs(sub.OldPath, sub.NewPath, subText));
            renamePairs.Add((
                new Ref(oldRef.Bucket, $"{oldRef.Slug}/{sub.RelativeSuffix}"),
                new Ref(newRef.Bucket, $"{newRef.Slug}/{sub.RelativeSuffix}")));
        }

        var newText = $"{BucketStrings.ToWire(newRef.Bucket)}:{newRef.Slug}";
        changedItems.AddRange(rewriter.RewriteKbRefs(oldPath, newPath, newText));

        // The entry's own `id:` duplicates its filename, so the move made it
        // stale — rewrite it for the parent and every sub-doc before anything
        // reads the files again. Buckets without an id field are untouched.
        var store = new KbStore(repoRoot);
        foreach (var (_, moved) in renamePairs)
            KbIdentity.SyncId(store, moved);

        // Rewrite KB-to-KB frontmatter refs (supersedes / superseded_by /
        // feature) now that the files are at their new home — the rewriter
        // enumerates current on-disk state.
        var kbAffected = new KbFrontmatterRefRewriter(store)
            .RewriteFrontmatterRefs(renamePairs);

        // Prose last, so it reads the files every earlier rewrite left. A
        // folder move repoints links into any file under it; the sub-doc refs
        // follow the parent's via the `/sub` prefix rule.
        var proseMove = isFolderForm
            ? (Path.GetDirectoryName(oldPath)!, Path.GetDirectoryName(newPath)!)
            : (oldPath, newPath);
        var proseRenames = new List<(Ref Old, Ref New)> { (oldRef, newRef) };
        if (oldResolved.Ref != oldRef) proseRenames.Add((oldResolved.Ref, newRef));
        var relinked = proseRewriter.RewriteProse([proseMove, .. move.Assets], proseRenames);

        // An item may reference both the parent and a sub-doc — dedup ids.
        var rewritten = changedItems.Distinct().ToList();
        return OperationResult<KbRenameResult>.Ok(
            new KbRenameResult(oldRef, newRef, oldPath, newPath, rewritten, kbAffected, relinked));
    }
}

/// <summary>
/// Strategy for rewriting <c>KB ref</c> rows in items. Kept as a
/// dependency so the Core layer doesn't need to know about ItemStore /
/// ItemFile directly — the CLI wires this up.
/// </summary>
public interface ItemRefRewriter
{
    IReadOnlyList<string> RewriteKbRefs(string oldTargetPath, string newTargetPath, string? newTargetText = null);
}
