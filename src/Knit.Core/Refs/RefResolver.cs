using Knit.Core.Designs;
using Knit.Core.Discovery;
using Knit.Core.Result;

namespace Knit.Core.Refs;

public sealed record ResolvedRef(Ref Ref, string Path, bool Exists);

/// <summary>
/// Resolves a <see cref="Ref"/> to a filesystem path under a knit repo
/// root. For grow-to-folder KB buckets, falls back to
/// <c>&lt;slug&gt;/readme.md</c> when the single-file form is missing.
/// Items (<c>&lt;id-slug&gt;.md</c>) resolve through <see cref="ItemLayout"/>.
/// </summary>
public static class RefResolver
{
    public static OperationResult<ResolvedRef> Resolve(Ref r, string repoRoot)
    {
        if (r.Bucket == Bucket.Item)
        {
            var itemsDir = RepoDiscovery.ItemsDir(repoRoot);
            var match = ItemLayout.FindFile(itemsDir, r.Slug);
            if (match is not null)
                return OperationResult.Ok(new ResolvedRef(r, match, true));
            // Not present — return the canonical flat-form path so error
            // messages cite a sensible "expected" location.
            var nominal = System.IO.Path.Combine(itemsDir, $"{r.Slug}.md");
            return OperationResult.Ok(new ResolvedRef(r, nominal, false));
        }

        if (r.Bucket == Bucket.Design)
        {
            var active = DesignsLayout.DesignDocPath(repoRoot, r.Slug);
            if (File.Exists(active))
                return OperationResult.Ok(new ResolvedRef(r, active, true));
            // Archive fallback: completed/<slug>/<cycle>-design.md — the
            // cycle stem may differ from the slug, so match any design doc
            // under the slug's completed subtree.
            var archived = DesignsLayout.CompletedDesignDocs(repoRoot, r.Slug);
            if (archived.Count > 0)
                return OperationResult.Ok(new ResolvedRef(r, archived[0], true));
            return OperationResult.Ok(new ResolvedRef(r, active, false));
        }

        if (r.Bucket == Bucket.Project)
        {
            var readme = Knit.Core.Projects.ProjectsLayout.ReadmePath(repoRoot, r.Slug);
            return OperationResult.Ok(new ResolvedRef(r, readme, File.Exists(readme)));
        }

        var kbDir = System.IO.Path.Combine(RepoDiscovery.KnitDir(repoRoot), "kb");
        var bucketDir = System.IO.Path.Combine(kbDir, r.Bucket.Folder());

        // ADR special case: slug may be just the NNNN prefix. We resolve
        // it to the on-disk full slug and return a canonicalised Ref so
        // downstream writers don't accidentally create a `NNNN.md`
        // duplicate next to the existing `NNNN-real-slug.md` file.
        if (r.Bucket == Bucket.Adr && Directory.Exists(bucketDir))
        {
            var matches = Directory.EnumerateFiles(bucketDir, $"{r.Slug}*.md").ToList();
            if (matches.Count > 0)
            {
                var fullSlug = System.IO.Path.GetFileNameWithoutExtension(matches[0]);
                var canonical = new Ref(Bucket.Adr, fullSlug);
                return OperationResult.Ok(new ResolvedRef(canonical, matches[0], true));
            }
        }

        var filePath = System.IO.Path.Combine(bucketDir, r.Slug + ".md");
        if (File.Exists(filePath))
            return OperationResult.Ok(new ResolvedRef(r, filePath, true));

        if (r.Bucket.AllowsGrowToFolder())
        {
            var folderPath = System.IO.Path.Combine(bucketDir, r.Slug, "readme.md");
            if (File.Exists(folderPath))
                return OperationResult.Ok(new ResolvedRef(r, folderPath, true));
        }

        return OperationResult.Ok(new ResolvedRef(r, filePath, false));
    }
}
