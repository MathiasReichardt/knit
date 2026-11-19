using Knit.Core.Result;

namespace Knit.Core.Refs;

/// <summary>
/// A reference to a Knit artifact: <c>bucket:slug</c>. Slug may contain
/// '/' to address nested files within a bucket (e.g. <c>feature:user-mgmt/auth</c>).
/// </summary>
public sealed record Ref(Bucket Bucket, string Slug)
{
    public override string ToString() => $"{Bucket.ToWire()}:{Slug}";

    public static OperationResult<Ref> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return OperationResult.Error<Ref>(KnitError.BadArgument("empty ref"));
        var s = text.Trim();
        var colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1)
            return OperationResult.Error<Ref>(KnitError.BadArgument(
                $"malformed ref '{text}': expected '<bucket>:<slug>'"));
        var bucketStr = s[..colon];
        var slug = s[(colon + 1)..];
        if (!BucketStrings.TryParse(bucketStr, out var bucket))
            return OperationResult.Error<Ref>(KnitError.BadArgument($"unknown bucket '{bucketStr}' in ref '{text}'"));
        if (string.IsNullOrWhiteSpace(slug) || slug.StartsWith('/') || slug.EndsWith('/'))
            return OperationResult.Error<Ref>(KnitError.BadArgument($"malformed slug in ref '{text}'"));
        return OperationResult.Ok(new Ref(bucket, slug));
    }

    public static bool TryParse(string? text, out Ref result)
    {
        var r = Parse(text);
        if (r.IsError) { result = new Ref(Bucket.Item, ""); return false; }
        result = r.GetValueOrThrow();
        return true;
    }

    /// <summary>
    /// Accepts either a fully-qualified <c>item:&lt;uuid&gt;</c> ref or a bare
    /// uuid / short prefix and returns the item slug (uuid/prefix). Returns
    /// null when text is null/empty.
    /// </summary>
    public static string? StripItemPrefix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
            return s[5..];
        return s;
    }
}
