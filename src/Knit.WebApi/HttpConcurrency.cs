using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi;

/// <summary>
/// Shared optimistic-concurrency guard for the raw-doc write endpoints
/// (KB entries, design docs). Mirrors the inline item-mutator guard so a
/// stale write fails the same way everywhere: <c>428</c> when the
/// <c>If-Match</c> header is absent, <c>412</c> when it no longer matches
/// the current etag. The SPA keys its conflict UI off the <c>412</c>
/// status, so the problem shape matches the item endpoints exactly.
/// </summary>
public static class HttpConcurrency
{
    /// <summary>
    /// Returns <c>null</c> when the request's <c>If-Match</c> satisfies
    /// <paramref name="currentEtag"/>; otherwise the precondition-failure result.
    /// </summary>
    public static IResult? RequireIfMatch(HttpRequest req, string currentEtag)
    {
        var ifMatch = req.Headers[HeaderNames.IfMatch].ToString();
        if (string.IsNullOrEmpty(ifMatch))
            return Problem(428, "precondition_required", "If-Match header is required");
        if (!Matches(ifMatch, currentEtag))
            return Problem(412, "etag_mismatch",
                $"current etag {currentEtag} does not match If-Match {ifMatch}");
        return null;
    }

    public static bool Matches(string headerValue, string current)
    {
        if (string.IsNullOrEmpty(headerValue)) return false;
        if (headerValue.Trim() == "*") return true;
        foreach (var part in headerValue.Split(','))
        {
            var v = part.Trim();
            if (v.StartsWith("W/", StringComparison.Ordinal)) v = v[2..];
            if (v == current) return true;
        }
        return false;
    }
}
