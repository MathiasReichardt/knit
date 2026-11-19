using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Knit.WebApi;

/// <summary>
/// Offset/limit paging shared by the landing-row feeds (designs / KB / items).
/// The page is a plain array body; the total count and a
/// "more exist" flag ride in response headers so the existing bare-array
/// contract of the list endpoints is preserved (the carousel in 11.6 reads
/// <c>X-Has-More</c> to decide whether to show the → arrow).
/// </summary>
internal static class Paging
{
    public static int Int(IQueryCollection q, string key, int fallback) =>
        int.TryParse(q[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;

    public static bool Flag(IQueryCollection q, string key)
    {
        var v = q[key].ToString().Trim().ToLowerInvariant();
        return v is "1" or "true" or "yes";
    }

    /// <summary>
    /// Slice an already-ordered list by offset/limit and stamp the
    /// <c>X-Total-Count</c> / <c>X-Has-More</c> headers. <paramref name="limit"/>
    /// &lt;= 0 means "no limit" (the whole tail from <paramref name="offset"/>),
    /// which is the default for the un-paged list callers.
    /// </summary>
    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> ordered, int offset, int limit, HttpResponse res)
    {
        var total = ordered.Count;
        IEnumerable<T> slice = ordered.Skip(offset);
        if (limit > 0) slice = slice.Take(limit);
        var page = slice.ToList();
        res.Headers["X-Total-Count"] = total.ToString(CultureInfo.InvariantCulture);
        res.Headers["X-Has-More"] = limit > 0 && offset + limit < total ? "true" : "false";
        return page;
    }
}
