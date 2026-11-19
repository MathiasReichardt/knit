using Knit.Core.Ids;
using Knit.Core.Refs;

namespace Knit.Core.Kb;

// Helpers for creating KB sub-docs (`feature:foo/wiring` →
// `features/foo/wiring.md`). A nested slug is only valid when the
// parent slug is already in folder form (`features/foo/readme.md`
// exists). Buckets without grow-to-folder semantics reject `/` in
// the slug. Every segment is slugified via Slug.From, so a typed
// "Wiring Diagram" sub-doc name becomes "wiring-diagram".
public static class SubdocSlug
{
    /// <summary><paramref name="Mint"/> carries what slugification had to
    /// change across all segments, so the caller can report it.</summary>
    public readonly record struct Result(string? Slug, string? Error, SlugChange Mint = SlugChange.None);

    public static Result NormalizeDocPath(KbStore store, string raw, string? title = null)
        => Normalize(store, Bucket.Doc, raw, title);

    public static Result NormalizeFeaturePath(KbStore store, string raw, string? title = null)
        => Normalize(store, Bucket.Feature, raw, title);

    public static Result Normalize(KbStore store, Bucket bucket, string raw, string? title = null)
    {
        var cleaned = raw.Replace('\\', '/');

        // Sub-doc shorthand: "<parent>/" (a parent prefix with an empty leaf)
        // means "create a child of <parent>, deriving its slug from the
        // title". Lets the web UI offer title-only sub-doc creation while
        // still letting the user type an explicit leaf to override.
        if (cleaned.EndsWith('/'))
        {
            var parent = cleaned.Trim('/');
            var leaf = string.IsNullOrWhiteSpace(title) ? "" : Slug.From(title);
            // The title-derived leaf is a convenience, not something the
            // caller typed — its normalization is not worth reporting.
            if (parent.Length == 0 || leaf.Length == 0)
                return new Result(null, "slug or title is required");
            cleaned = parent + "/" + leaf;
        }

        var slug = cleaned.Trim('/');
        if (slug.Length == 0) return new Result(null, "slug is required");

        if (!slug.Contains('/'))
        {
            var one = Slug.Describe(slug);
            return new Result(one.Value, null, one.Change);
        }

        if (!bucket.AllowsGrowToFolder())
            return new Result(null,
                $"nested slug is only valid for grow-to-folder buckets ({BucketFolders.GrowToFolderBuckets}); got {bucket.ToWire()}");

        var described = slug.Split('/').Select(Slug.Describe).ToArray();
        var parts = described.Select(d => d.Value).ToArray();
        var mint = described.Aggregate(SlugChange.None, (acc, d) => acc | d.Change);
        var normalized = string.Join('/', parts);
        var parentSlug = string.Join('/', parts[..^1]);
        var parentReadme = KbLayout.FolderReadmePath(store.RepoRoot, new Ref(bucket, parentSlug));
        if (!File.Exists(parentReadme))
            return new Result(null,
                $"parent {bucket.ToWire()}:{parentSlug} must be in folder form — run `knit kb split` first");

        return new Result(normalized, null, mint);
    }
}
