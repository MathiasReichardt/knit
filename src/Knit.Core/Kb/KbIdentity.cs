using Knit.Core.Refs;

namespace Knit.Core.Kb;

/// <summary>
/// The <c>id:</c> a KB artifact must declare, derived from its own filename.
/// Refs resolve by path, so the field duplicates the name — when the two
/// disagree the field is a lie that resolves nowhere, and the file is the
/// only place a reader can see it.
/// <para>
/// Buckets that identify by path carry no <c>id:</c> at all; null means
/// "this bucket has no id field", and nothing is ever added to a file that
/// doesn't already have one.
/// </para>
/// <para>
/// <b>ADRs are in scope.</b> Their id is not the slug but the number
/// <c>knit kb add adr</c> mints into it (slug <c>0007-retry-policy</c>, id
/// <c>0007</c>) — so a rename drifts it exactly like a source's slug, only
/// less visibly. The rule is the same for both: the id is derived from the
/// filename, per bucket.
/// </para>
/// </summary>
public static class KbIdentity
{
    /// <summary>
    /// The id the entry at <paramref name="slug"/> must declare, or null
    /// when the bucket has no id field — or, for an ADR whose slug carries
    /// no number, when there is nothing to derive it from.
    /// </summary>
    public static string? ExpectedId(Bucket bucket, string slug)
    {
        var name = LastSegment(slug);
        return bucket switch
        {
            Bucket.Source => name.Length == 0 ? null : name,
            Bucket.Adr => LeadingDigits(name),
            _ => null,
        };
    }

    /// <summary>Whether the bucket stores an id at all.</summary>
    public static bool CarriesId(Bucket bucket) => bucket is Bucket.Source or Bucket.Adr;

    /// <summary>
    /// Rewrite the entry's own <c>id:</c> to match its (new) filename.
    /// A no-op when the bucket has no id, when the file doesn't declare
    /// one, or when it already agrees. Returns the new id when it wrote.
    /// </summary>
    public static string? SyncId(KbStore store, Ref r)
    {
        var expected = ExpectedId(r.Bucket, r.Slug);
        if (expected is null) return null;
        var read = store.Read(r);
        if (read is null) return null;
        if (!read.Value.Frontmatter.TryGetValue("id", out var raw)) return null;
        if (string.Equals(raw?.ToString(), expected, StringComparison.Ordinal)) return null;
        var fm = new Dictionary<string, object?>(read.Value.Frontmatter, StringComparer.Ordinal)
        {
            ["id"] = expected,
        };
        store.Write(r, fm, read.Value.Body);
        return expected;
    }

    /// <summary>
    /// The leading digit run of an ADR slug (<c>0007-retry-policy</c> →
    /// <c>0007</c>), or null when the slug carries no number.
    /// </summary>
    public static string? LeadingDigits(string name)
    {
        var i = 0;
        while (i < name.Length && char.IsAsciiDigit(name[i])) i++;
        return i == 0 ? null : name[..i];
    }

    private static string LastSegment(string slug)
    {
        var parts = slug.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }
}
