using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Read-only view of the configured mounts — what the doctor screen
/// lists, and what a KB source panel checks before offering to open a file.
///
/// There is deliberately no write endpoint. Editing a mount means writing
/// either <c>config.json</c> (committed, shared with everyone) or
/// <c>config.local.json</c> (this machine only); a web form silently picking
/// one is a footgun, and the unauthenticated loopback API has no business
/// writing per-machine config for someone who may be looking at another
/// checkout. The SPA shows the state and the exact <c>knit mount</c> command.
/// </summary>
public static class MountEndpoints
{
    public static void MapMounts(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/mounts", () =>
        {
            var repo = new RepoServices(options);
            var knitDir = RepoDiscovery.KnitDir(repo.Root);
            var rows = RepoVerifier.ScanMounts(
                repo.Root,
                ConfigLoader.ReadGlobal().Node?.Deserialize(KnitConfigJsonContext.Default.KnitConfig),
                ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.FileName),
                ConfigLoader.TryLoadLayer(knitDir, ConfigLoader.LocalFileName),
                repo.Yaml);

            return Results.Json(
                new MountListDto(rows.Select(m => new MountRowDto(
                    m.Name, m.DeclaredPath, m.EffectivePath, m.Source,
                    m.Resolves, m.Portable, m.SourceCount, m.Reason)).ToList()),
                ApiJsonContext.Default.MountListDto);
        });
    }
}
