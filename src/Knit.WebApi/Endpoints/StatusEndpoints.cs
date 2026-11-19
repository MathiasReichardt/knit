using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Data;
using Knit.Data.StatusPage;
using Knit.WebApi.Contracts;
using Knit.WebApi.Queries;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatus(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        // Mirrors `knit status`, sharing StatusRollup so the strip and the CLI
        // can't disagree. Scope params only (project / milestone / slate /
        // under) — state filters are deliberately not accepted: a completion
        // percentage over a set that already dropped done items is always 0%.
        app.MapGet("/api/status", (HttpRequest req) =>
        {
            var project = Trim(req.Query["project"]);
            var slate = Trim(req.Query["slate"]);
            var milestone = Trim(req.Query["milestone"]);
            var under = Trim(req.Query["under"]);

            var repo = new RepoServices(options);
            var graph = repo.Graph();

            string? underId = null, underLabel = null;
            if (under is not null)
            {
                var item = ItemQueryRunner.TryResolveById(graph, under);
                if (item is null) return Results.NotFound($"no item matches '{under}'");
                underId = item.Id;
                underLabel = repo.Shortener().Short(item.Id);
            }

            var (scoped, err) = StatusRollup.Scope(repo.Store, repo.Root, graph, project, slate, milestone, underId);
            if (err is not null) return Results.BadRequest(err);

            var label = StatusRollup.Label(project, slate, milestone, underLabel);
            var r = StatusRollup.Compute(scoped, DateOnly.FromDateTime(DateTime.Now), label);

            return Results.Json(
                new StatusDto(
                    r.Scope, r.Total, r.Done, r.InProgress, r.Review, r.Todo, r.Dismissed,
                    r.Blocked, r.Overdue, r.Unassigned, r.Percent, r.Health?.ToWire()),
                ApiJsonContext.Default.StatusDto);
        });

        // Whether there is a page knit wrote, and how old it is. Read per
        // request: a run from the terminal or a deleted file applies at once.
        app.MapGet("/api/status/report", () =>
        {
            var repo = new RepoServices(options);
            var config = ConfigLoader.Load(repo.KnitDir);
            var age = StatusPageFile.Age(repo.Root, config, DateOnly.FromDateTime(DateTime.Now));
            var path = age?.Path is { } p
                ? Path.GetRelativePath(repo.Root, p).Replace('\\', '/')
                : config.StatusReportPath.Replace('\\', '/');
            return Results.Json(
                new StatusPageDto(path, age is not null, age?.Generated is { } g ? DueDates.ToWire(g) : null, age?.Stale ?? false),
                ApiJsonContext.Default.StatusPageDto);
        });

        // `knit status report` without flags: the path comes from
        // status_report.path, read per request so a `config set` in another
        // terminal applies. Never forces — a hand-written file is the user's
        // call, made on the command line.
        app.MapPost("/api/status/report", () =>
        {
            var repo = new RepoServices(options);
            var config = ConfigLoader.Load(repo.KnitDir);
            var (target, err) = StatusPageFile.Resolve(repo.Root, config.StatusReportPath);
            if (target is null) return Problem(400, "invalid_path", $"status_report.path: {err}");

            var page = StatusPageFile.Render(
                repo.Root, repo.Store, repo.Yaml, config, options.GitRoot,
                DateOnly.FromDateTime(DateTime.Now), target, repo.Shortener().Short);
            var rel = Path.GetRelativePath(repo.Root, target).Replace('\\', '/');
            if (StatusPageFile.Write(target, page, force: false) == StatusPageWrite.NotGenerated)
                return Problem(409, "not_generated",
                    $"{rel} exists and was not written by knit — replace it with `knit status report --force`");
            return Results.Json(new StatusReportDto(rel, true), ApiJsonContext.Default.StatusReportDto);
        });
    }

    private static string? Trim(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
