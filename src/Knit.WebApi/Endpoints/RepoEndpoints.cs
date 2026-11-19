using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.WebApi.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// What the header says about the repo: its name and the branch its checkout
/// is on.
///
/// The name is the one config write the web API makes. Unlike the mounts
/// <see cref="ConfigEndpoints"/> refuses to edit, it has one unambiguous home —
/// <c>config.json</c>, committed, the same for everyone — and a wrong value is
/// cosmetic.
/// </summary>
public static class RepoEndpoints
{
    private const string RepoNameKey = "repo_name";

    public static void MapRepo(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        // Which checkout and whether this is a worktree cannot change while
        // the host runs; HEAD can, so only HEAD is read per request.
        var checkout = GitHead.CheckoutFor(options.RootPath, options.GitRoot);
        var worktree = options.GitRoot is { } g && (GitWorktree.MainCheckout(g) is not null || GitWorktree.Bare(g) is not null) ? g : null;
        var defaultName = GitHead.DefaultRepoName(options.RootPath, options.GitRoot);

        app.MapGet("/api/root/git", () =>
        {
            var head = checkout is null ? null : GitHead.Read(checkout);
            return Results.Json(
                new GitHeadDto(head is null ? null : checkout, head?.Branch, head?.ShortSha, head?.Rebasing ?? false, worktree),
                ApiJsonContext.Default.GitHeadDto);
        });

        app.MapGet("/api/root/name", () => Results.Json(Name(), ApiJsonContext.Default.RepoNameDto));

        app.MapPut("/api/root/name", async (HttpRequest req, CancellationToken ct) =>
        {
            RepoNameRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.RepoNameRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }

            var knitDir = RepoDiscovery.KnitDir(options.RootPath);
            var local = ConfigLoader.ReadLayer(knitDir, ConfigLoader.LocalFileName);
            if (local.Unreadable)
                return Problem(409, "config_unreadable", $"{ConfigLoader.LocalFileName} does not parse — fix it first");
            // Writing config.json under a local override would report success
            // and change nothing on screen.
            if (local.Node?[RepoNameKey] is not null)
                return Problem(409, "overridden",
                    $"{RepoNameKey} is set in {ConfigLoader.LocalFileName}, which wins — " +
                    $"change it with `knit config set --local {RepoNameKey} <name>` or `knit config unset --local {RepoNameKey}`");

            // The same write `knit config set` / `unset` make.
            var path = Path.Combine(knitDir, ConfigLoader.FileName);
            var result = string.IsNullOrWhiteSpace(body?.Name)
                ? ConfigWriter.Unset(path, ConfigLoader.FileName, RepoNameKey)
                : ConfigWriter.Set(path, ConfigLoader.FileName, global: false, RepoNameKey, body.Name);
            return result.Failure switch
            {
                ConfigWriteFailure.None or ConfigWriteFailure.NotSet => Results.Json(Name(), ApiJsonContext.Default.RepoNameDto),
                ConfigWriteFailure.Unreadable => Problem(409, "config_unreadable", result.Error!),
                _ => Problem(400, "invalid_name", result.Error!),
            };
        });

        // Read per request, as ConfigEndpoints does: `knit config set` in
        // another terminal must show without a restart.
        RepoNameDto Name()
        {
            var entry = ConfigReport.Build(options.RootPath).Keys.First(e => e.Name == RepoNameKey);
            return string.IsNullOrWhiteSpace(entry.Value)
                ? new RepoNameDto(defaultName, defaultName, "default")
                : new RepoNameDto(entry.Value, defaultName, entry.SourceLabel);
        }
    }
}
