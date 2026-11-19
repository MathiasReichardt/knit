using Knit.Core.DocFolders;
using Knit.Core.Refs;
using Knit.Core.Result;

namespace Knit.Core.Kb;

public sealed record KbSplitResult(
    Ref Ref,
    string OldPath,
    string NewPath,
    bool NoOp,
    IReadOnlyList<string> ItemsRewritten,
    IReadOnlyList<string> FilesRelinked);

/// <summary>
/// Strategy for repairing body prose after a move or rename: markdown links
/// into a moved file or folder, and <c>`bucket:slug`</c> code-spans naming a
/// renamed ref or one of its <c>/sub</c> docs. Returns the files changed.
/// Kept as a dependency for the same reason as <see cref="ItemRefRewriter"/> —
/// Core doesn't know about <c>ItemStore</c>.
/// </summary>
public interface ProseRewriter
{
    IReadOnlyList<string> RewriteProse(
        IReadOnlyList<(string OldPath, string NewPath)> moves,
        IReadOnlyList<(Ref Old, Ref New)> renames);
}

/// <summary>
/// Promote a single-file KB entry to folder form (<c>slug.md</c> →
/// <c>slug/readme.md</c>). The canonical ref is unchanged — the resolver
/// falls back to the folder form — but every <em>path</em> pointing at the
/// old file goes stale, so the move repairs them itself rather than leaving
/// a repo that only `knit relink` can fix.
/// </summary>
public static class KbSplit
{
    public static OperationResult<KbSplitResult> Split(
        string repoRoot,
        Ref r,
        KbStore store,
        ItemRefRewriter refRewriter,
        ProseRewriter proseRewriter)
    {
        if (r.Bucket == Bucket.Item)
            return OperationResult<KbSplitResult>.Error(
                KnitError.BadArgument("split does not operate on item refs"));
        if (!r.Bucket.AllowsGrowToFolder())
            return OperationResult<KbSplitResult>.Error(KnitError.BadArgument(
                $"split is only valid for grow-to-folder buckets ({BucketFolders.GrowToFolderBuckets}); got {r.Bucket.ToWire()}"));

        var bucketDir = KbLayout.BucketDir(repoRoot, r.Bucket);
        var fileForm = Path.Combine(bucketDir, r.Slug + ".md");
        var folderForm = Path.Combine(bucketDir, r.Slug);
        var readme = Path.Combine(folderForm, "readme.md");

        if (File.Exists(readme))
            return OperationResult<KbSplitResult>.Ok(
                new KbSplitResult(r, fileForm, readme, NoOp: true, [], []));
        if (!File.Exists(fileForm))
            return OperationResult<KbSplitResult>.Error(
                KnitError.NotFound($"unresolved {r}: no file at {fileForm}"));

        Directory.CreateDirectory(folderForm);
        File.Move(fileForm, readme);
        var assets = DocAssets.Carry(fileForm, readme);

        // The ref text is untouched (`bucket:slug` still resolves) — only the
        // relative path half of each reference moved one level deeper.
        var items = refRewriter.RewriteKbRefs(fileForm, readme);
        var files = proseRewriter.RewriteProse([(fileForm, readme), .. assets], []);

        new KbIndexer(store).RebuildBucketAndMaster(r.Bucket);
        return OperationResult<KbSplitResult>.Ok(
            new KbSplitResult(r, fileForm, readme, NoOp: false, items, files));
    }
}
