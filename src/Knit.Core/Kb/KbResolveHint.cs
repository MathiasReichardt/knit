using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// The recovery hint for a KB ref that resolves to nothing. The
/// default advice — "create it" — is wrong in the one case that actually
/// happens: the slug came out of a file whose own <c>id:</c> is stale, so
/// following the hint would add a <b>second</b> entry for the thing that
/// already exists. When an existing entry explains the miss, the hint names
/// that entry instead.
/// </summary>
public static class KbResolveHint
{
    public static string For(KbStore store, Ref r)
    {
        // `design:` / `project:` are ref address spaces, not KB buckets —
        // nothing to enumerate, and asking would throw.
        if (!BucketStrings.All.Contains(r.Bucket) || r.Bucket == Bucket.Item)
            return $"Nothing resolves `{r}`.";

        var claim = Claimant(store, r);
        if (claim is not null)
        {
            var (path, actual) = claim.Value;
            return $"'{r.Slug}' is the id declared by {Rel(store, path)}, whose filename says " +
                   $"'{actual}' — the id is stale, so nothing resolves it. Address the entry as " +
                   $"`{r.Bucket.ToWire()}:{actual}` and repair the file with `knit relink --ids`.";
        }

        var near = Nearest(store, r);
        return near is not null
            ? $"did you mean `{r.Bucket.ToWire()}:{near}`?"
            : $"Try `knit kb add {r.Bucket.ToWire()} {r.Slug}` to create it.";
    }

    /// <summary>
    /// The error for a KB ref given without its bucket. Slugs are unique per
    /// bucket, not repo-wide, so a bare slug is never resolved — only looked
    /// up to name the full ref(s) it could mean.
    /// </summary>
    public static string ForBareSlug(KbStore store, string text)
    {
        var slug = text.Trim();
        var matches = BucketStrings.All
            .Where(b => b != Bucket.Item)
            .SelectMany(store.ListBucket)
            .Where(e => string.Equals(e.Ref.Slug, slug, StringComparison.Ordinal))
            .Select(e => $"`{e.Ref}`")
            .Distinct()
            .ToList();
        var baseMsg = $"'{slug}' has no bucket — a KB ref is `<bucket>:<slug>`";
        return matches.Count switch
        {
            0 => baseMsg + ".",
            1 => $"{baseMsg}; did you mean {matches[0]}?",
            _ => $"{baseMsg}; '{slug}' exists as {string.Join(", ", matches)}.",
        };
    }

    /// <summary>An existing entry in the bucket that declares the missing
    /// slug as its own <c>id:</c>, with the slug its filename actually
    /// carries.</summary>
    private static (string Path, string Actual)? Claimant(KbStore store, Ref r)
    {
        if (!KbIdentity.CarriesId(r.Bucket)) return null;
        foreach (var (entry, path) in store.ListBucket(r.Bucket))
        {
            var read = store.ReadPath(path);
            if (read is null) continue;
            if (!read.Value.Frontmatter.TryGetValue("id", out var raw)) continue;
            if (string.Equals(raw?.ToString()?.Trim(), r.Slug, StringComparison.Ordinal))
                return (path, entry.Slug);
        }
        return null;
    }

    /// <summary>
    /// An entry whose slug is a prefix of the missing one, or vice versa —
    /// which is what a truncated or hand-shortened slug looks like. Nothing
    /// fuzzier: a wrong suggestion is worse than none, because the reader
    /// cannot tell it apart from a right one.
    /// </summary>
    private static string? Nearest(KbStore store, Ref r)
    {
        if (r.Slug.Length == 0) return null;
        foreach (var (entry, _) in store.ListBucket(r.Bucket))
        {
            if (string.Equals(entry.Slug, r.Slug, StringComparison.Ordinal)) continue;
            if (entry.Slug.StartsWith(r.Slug, StringComparison.Ordinal)
                || r.Slug.StartsWith(entry.Slug, StringComparison.Ordinal))
                return entry.Slug;
        }
        return null;
    }

    private static string Rel(KbStore store, string path) =>
        Path.GetRelativePath(store.RepoRoot, path).Replace('\\', '/');
}
