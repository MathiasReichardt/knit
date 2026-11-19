using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
namespace Knit.WebApi;

/// <summary>
/// Keeps other websites out of the unauthenticated API. A loopback bind only
/// answers to a loopback <c>Host</c>, which stops DNS rebinding. A write must
/// carry <c>Content-Type: application/json</c>, even without a body: a web page
/// can only send that cross-site after a CORS preflight, which this API never
/// grants — so it holds even when a browser omits <c>Origin</c>. A write whose
/// <c>Origin</c> is not the API's own is refused too. Clients that send no
/// <c>Origin</c> (curl, scripts) pass once they set the content type.
/// </summary>
public static class RequestOriginGuard
{
    public sealed record Refusal(int Status, string Code, string Reason);

    /// <summary>Why the request is refused, or null to let it through.</summary>
    public static Refusal? Reject(
        BindAddress bind, string scheme, string method, HostString host, string? origin, string? contentType)
    {
        if (bind.IsLoopback && !IsLoopbackHost(host.Host))
            return Forbidden($"Host '{host.Value}' is not a loopback name; this server only answers on localhost.");

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return null;

        var own = $"{scheme}://{host.Value}";
        if (origin is not null && !string.Equals(origin, own, StringComparison.OrdinalIgnoreCase))
            return Forbidden($"Origin '{origin}' may not write to {own}.");

        return IsJson(contentType)
            ? null
            : new Refusal(
                StatusCodes.Status415UnsupportedMediaType,
                "json_required",
                "A write needs 'Content-Type: application/json', also when it has no body.");
    }

    private static Refusal Forbidden(string reason) =>
        new(StatusCodes.Status403Forbidden, "forbidden_origin", reason);

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && parsed.MediaType.Equals(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
    }
}
