using System.Text.Json;
using Knit.Core.Config;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// The repo's and the user's conventions for agents, read and
/// written as <c>knit conventions</c> does. Unlike config, the text is content
/// — the same kind of markdown an item body is — so the settings view edits it.
/// </summary>
public static class ConventionsEndpoints
{
    public static void MapConventions(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/conventions", () => Results.Json(
            new ConventionsDto([.. Conventions.ReadAll(options.RootPath).Select(f => ToDto(f, options))]),
            ApiJsonContext.Default.ConventionsDto));

        app.MapPut("/api/conventions/{scope}", async (string scope, HttpRequest req, CancellationToken ct) =>
        {
            if (Guard(scope, req, options, out var s) is { } refused) return refused;

            ConventionsWriteDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.ConventionsWriteDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }

            var file = Conventions.Write(s, options.RootPath, body?.Text ?? "");
            return Results.Json(ToDto(file, options), ApiJsonContext.Default.ConventionsScopeDto);
        });

        app.MapDelete("/api/conventions/{scope}", IResult (string scope, HttpRequest req) =>
        {
            if (Guard(scope, req, options, out var s) is { } refused) return refused;
            var file = Conventions.Remove(s, options.RootPath);
            return Results.Json(ToDto(file, options), ApiJsonContext.Default.ConventionsScopeDto);
        });
    }

    /// <summary>The refusal for a write to <paramref name="scope"/>, or null when it may go ahead.</summary>
    private static IResult? Guard(string scope, HttpRequest req, WebApiOptions options, out ConventionsScope s)
    {
        if (!Conventions.TryParse(scope, out s))
            return Problem(404, "unknown_scope", $"unknown scope '{scope}' — use repo or user");
        if (!Writable(s, options))
            return Problem(403, "not_writable",
                $"the user conventions belong to this machine's user; edit {Conventions.PathFor(s, options.RootPath)} there");
        return HttpConcurrency.RequireIfMatch(req, Etag.OfFile(Conventions.PathFor(s, options.RootPath)));
    }

    private static bool Writable(ConventionsScope scope, WebApiOptions options) =>
        scope == ConventionsScope.Repo || options.Bind.IsLoopback;

    private static ConventionsScopeDto ToDto(ConventionsFile f, WebApiOptions options) =>
        new(f.Scope.ToWire(), f.Path, f.Exists, f.Text ?? "", Writable(f.Scope, options), Etag.OfFile(f.Path));
}
