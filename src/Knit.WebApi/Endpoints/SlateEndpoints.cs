using System.Text.Json;
using Knit.Core.Ids;
using Knit.Core.Slates;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// CRUD over the slate registry (<c>.knit/slates.md</c>). The SPA and
/// CLI rewrite the same table file through <see cref="SlateRegistry"/>;
/// rename triggers the same item-walk (<see cref="SlateItems.Rename"/>)
/// the CLI does. No ETag — the registry is a small last-write-wins
/// table, matching the CLI's concurrency posture.
/// </summary>
public static class SlateEndpoints
{
    public static void MapSlates(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/slates", () =>
        {
            var repo = new RepoServices(options);
            var rows = SlateRegistry.Load(repo.Root);
            var counts = CountBySlate(repo.Store);

            var list = new List<SlateDto>(rows.Count);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                known.Add(r.Name);
                list.Add(new SlateDto(r.Name, r.Status.ToWire(), r.Notes,
                    counts.GetValueOrDefault(r.Name), InRegistry: true));
            }
            // Union in item-only values so a stale or
            // hand-typed slate never vanishes from the dropdown.
            foreach (var (name, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (known.Contains(name)) continue;
                list.Add(new SlateDto(name, SlateStatus.Planned.ToWire(), null, count, InRegistry: false));
            }
            return Results.Json(list, ApiJsonContext.Default.IReadOnlyListSlateDto);
        });

        app.MapPost("/api/slates", async (HttpRequest req, CancellationToken ct) =>
        {
            SlateCreateRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.SlateCreateRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
                return Problem(400, "invalid_request", "name is required");

            var status = SlateStatus.Planned;
            if (!string.IsNullOrWhiteSpace(body.Status) && !SlateStatuses.TryParse(body.Status, out status))
                return Problem(400, "invalid_status", $"unknown status '{body.Status}' (expected planned|active|archived)");

            var repo = new RepoServices(options);
            var name = Slug.From(body.Name);
            var rows = SlateRegistry.Load(repo.Root).ToList();
            if (rows.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Problem(409, "exists", $"slate '{name}' already exists");

            var notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes;
            rows.Add(new SlateRow(name, status, notes));
            SlateRegistry.Save(repo.Root, rows);

            return Results.Json(
                new SlateDto(name, status.ToWire(), notes, SlateItems.Count(repo.Store, name), InRegistry: true),
                ApiJsonContext.Default.SlateDto, statusCode: StatusCodes.Status201Created);
        });

        app.MapMethods("/api/slates/{name}", new[] { "PATCH" },
            async (string name, HttpRequest req, CancellationToken ct) =>
        {
            SlatePatchRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.SlatePatchRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");
            if (body.Before is not null && body.After is not null)
                return Problem(400, "invalid_request", "specify at most one of before / after");

            var repo = new RepoServices(options);
            var slug = Slug.From(name);
            var rows = SlateRegistry.Load(repo.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(slug, StringComparison.OrdinalIgnoreCase));

            // Rename tolerates an item-only slate (no registry row), same
            // as the CLI; every other patch needs the row to exist.
            var renameTo = string.IsNullOrWhiteSpace(body.Rename) ? null : Slug.From(body.Rename);
            if (idx < 0 && (renameTo is null || SlateItems.Count(repo.Store, slug) == 0))
                return Problem(404, "not_found", $"no slate '{slug}' in the registry");

            var itemsUpdated = 0;
            var row = idx >= 0 ? rows[idx] : new SlateRow(slug, SlateStatus.Planned, null);
            if (renameTo is not null && !renameTo.Equals(slug, StringComparison.OrdinalIgnoreCase))
            {
                if (rows.Any(r => r.Name.Equals(renameTo, StringComparison.OrdinalIgnoreCase)))
                    return Problem(409, "exists", $"slate '{renameTo}' already exists");
                row = row with { Name = renameTo };
                itemsUpdated = SlateItems.Rename(repo.Store, slug, renameTo);
            }
            if (body.Status is not null)
            {
                if (!SlateStatuses.TryParse(body.Status, out var status))
                    return Problem(400, "invalid_status", $"unknown status '{body.Status}' (expected planned|active|archived)");
                row = row with { Status = status };
            }
            if (body.Notes is not null)
                row = row with { Notes = body.Notes.Length == 0 ? null : body.Notes };

            if (idx >= 0) rows[idx] = row;
            else { rows.Add(row); idx = rows.Count - 1; }

            if (body.Before is not null || body.After is not null)
            {
                var anchor = Slug.From(body.Before ?? body.After!);
                if (anchor.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
                    return Problem(400, "invalid_request", "cannot move a slate relative to itself");
                rows.RemoveAt(idx);
                var anchorIdx = rows.FindIndex(r => r.Name.Equals(anchor, StringComparison.OrdinalIgnoreCase));
                if (anchorIdx < 0) return Problem(404, "not_found", $"no slate '{anchor}' in the registry");
                rows.Insert(body.Before is not null ? anchorIdx : anchorIdx + 1, row);
            }

            SlateRegistry.Save(repo.Root, rows);
            return Results.Json(
                new SlatePatchResultDto(
                    new SlateDto(row.Name, row.Status.ToWire(), row.Notes,
                        SlateItems.Count(repo.Store, row.Name), InRegistry: true),
                    itemsUpdated),
                ApiJsonContext.Default.SlatePatchResultDto);
        });

        app.MapDelete("/api/slates/{name}", (string name) =>
        {
            var repo = new RepoServices(options);
            var slug = Slug.From(name);
            var rows = SlateRegistry.Load(repo.Root).ToList();
            var idx = rows.FindIndex(r => r.Name.Equals(slug, StringComparison.OrdinalIgnoreCase));

            // Tolerates an item-only slate (no registry row), like rename.
            if (idx < 0 && SlateItems.Count(repo.Store, slug) == 0)
                return Problem(404, "not_found", $"no slate '{slug}' in the registry or on any item");

            if (idx >= 0)
            {
                rows.RemoveAt(idx);
                SlateRegistry.Save(repo.Root, rows);
            }
            return Results.Json(
                new SlateDeleteResultDto(slug, SlateItems.Clear(repo.Store, slug)),
                ApiJsonContext.Default.SlateDeleteResultDto);
        });
    }

    private static Dictionary<string, int> CountBySlate(ItemStore store)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in store.AllItems())
        {
            if (string.IsNullOrEmpty(i.Slate)) continue;
            counts[i.Slate!] = counts.GetValueOrDefault(i.Slate!) + 1;
        }
        return counts;
    }
}
