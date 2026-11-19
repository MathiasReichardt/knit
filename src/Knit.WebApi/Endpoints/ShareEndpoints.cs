using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Paths;
using Knit.Core.Sharing;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Share links, read-only. The origin and the <c>forges</c> config
/// are read per request, so the copy after a <c>knit config set forges.…</c>
/// yields the forge URL without a restart. The SPA writes no config (SL4).
/// </summary>
public static class ShareEndpoints
{
    public static void MapShare(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/share/origin", () =>
            Results.Json(Dto(Origin(options)), ApiJsonContext.Default.ShareOriginDto));

        app.MapGet("/api/share", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Problem(400, "missing_path", "?path= is required");
            var knitDir = Path.GetFullPath(RepoDiscovery.KnitDir(options.RootPath));
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(options.RootPath, path));
            if (!PathContainment.IsContained(knitDir, full))
                return Problem(400, "outside_knit", "a share link names a file under .knit/");
            var origin = Origin(options);
            var link = ShareLinks.Link(origin, options.RootPath, full);
            return Results.Json(new ShareLinkDto(Dto(origin), link.Url, link.Path), ApiJsonContext.Default.ShareLinkDto);
        });
    }

    private static ShareOrigin Origin(WebApiOptions options) =>
        ShareLinks.Origin(options.RootPath, options.GitRoot, ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath)));

    private static ShareOriginDto Dto(ShareOrigin o) => new(
        o.Kind switch
        {
            ShareOriginKind.Forge => "forge",
            ShareOriginKind.UnknownHost => "unknown_host",
            ShareOriginKind.Invalid => "invalid",
            _ => "none",
        },
        o.Forge is { } f ? Forges.Name(f.Kind) : null,
        o.Host,
        o.Web,
        o.RepoName,
        o.Error);
}
