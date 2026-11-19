using Knit.Core.Config;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Read-only config for the settings view. Read per request on
/// purpose: the host runs for hours while <c>config.json</c> can be edited by
/// hand or by <c>knit config set</c> in another terminal, so a startup
/// snapshot would serve a value the server booted with.
///
/// No write endpoint, for the reason <see cref="MountEndpoints"/> gives:
/// committed repo policy does not get edited from an unauthenticated loopback
/// form. The one exception, <c>repo_name</c>, lives in <see cref="RepoEndpoints"/>.
/// </summary>
public static class ConfigEndpoints
{
    public static void MapConfig(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/config", () =>
        {
            var snapshot = ConfigReport.Build(options.RootPath);
            return Results.Json(
                new ConfigDto(
                    [.. snapshot.Keys.Select(e => new ConfigKeyDto(
                        e.Name, e.Value, e.SourceLabel, e.Default,
                        e.IsSet, e.Known, e.Settable, e.Description))],
                    snapshot.Unreadable,
                    [.. snapshot.Layers.Select(l => new ConfigLayerDto(l.Label, l.Path))],
                    [.. snapshot.IgnoredGlobal.Select(i => new IgnoredGlobalKeyDto(i.Name, i.Reason))]),
                ApiJsonContext.Default.ConfigDto);
        });
    }
}
