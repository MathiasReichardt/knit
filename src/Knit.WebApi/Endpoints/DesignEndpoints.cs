using System.Text.Json;
using Knit.Core.Designs;
using Knit.Core.Result;
using Knit.WebApi.Contracts;
using Knit.WebApi.Mapping;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Read + write endpoints for design docs under <c>.knit/designs/</c>.
/// Active docs live at the root; finished cycles archive under
/// <c>completed/&lt;slug&gt;/</c>. Designs are markdown only — no index,
/// no link graph — so these are thinner than the KB endpoints.
/// </summary>
public static class DesignEndpoints
{
    public static void MapDesigns(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/designs", (HttpRequest req) =>
        {
            // Active designs are already "open" (not under completed/), so the
            // landing's `open=1` is the default; archived=true flips to completed.
            var archived = IsTrue(req.Query["archived"].ToString());
            var store = new DesignStore(options.RootPath);
            var list = new List<DesignSummaryDto>();
            foreach (var (slug, path, completed) in EnumerateDesigns(options.RootPath, archived))
            {
                var read = store.ReadPath(path);
                if (read is null) continue;
                list.Add(new DesignSummaryDto(
                    Slug: slug,
                    Title: DesignMapping.TitleOf(slug, read.Value.Frontmatter, read.Value.Body),
                    FilePath: Abs(path),
                    Completed: completed,
                    Modified: DesignMapping.ModifiedIso(path)));
            }

            // sort=mtime[:asc|:desc] orders by file mtime for the landing row.
            // The ISO Modified string sorts chronologically as text.
            var sort = req.Query["sort"].ToString();
            if (sort.StartsWith("mtime", StringComparison.OrdinalIgnoreCase))
            {
                list = sort.EndsWith(":asc", StringComparison.OrdinalIgnoreCase)
                    ? list.OrderBy(d => d.Modified, StringComparer.Ordinal).ToList()
                    : list.OrderByDescending(d => d.Modified, StringComparer.Ordinal).ToList();
            }

            var offset = Paging.Int(req.Query, "offset", 0);
            var limit = Paging.Int(req.Query, "limit", 0);
            var page = Paging.Page(list, offset, limit, req.HttpContext.Response);
            return Results.Json(page, ApiJsonContext.Default.IReadOnlyListDesignSummaryDto);
        });

        app.MapGet("/api/designs/{slug}", (string slug, HttpResponse res) =>
        {
            var path = ResolveDesignPath(options.RootPath, slug, out var completed);
            if (path is null) return Problem(404, "not_found", $"no design '{slug}'");
            var store = new DesignStore(options.RootPath);
            var read = store.ReadPath(path);
            if (read is null) return Problem(500, "unreadable", "failed to read design doc");
            res.Headers.ETag = Etag.OfFile(path);
            return Results.Json(ToDto(options.RootPath, slug, path, completed, read.Value),
                ApiJsonContext.Default.DesignDto);
        });

        app.MapPost("/api/designs", async (HttpRequest req, CancellationToken ct) =>
        {
            DesignCreateRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.DesignCreateRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Slug))
                return Problem(400, "invalid_request", "slug is required");

            var store = new DesignStore(options.RootPath);
            var title = string.IsNullOrWhiteSpace(body.Title) ? body.Slug : body.Title!;
            var result = store.New(body.Slug, title, force: false);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);

            var path = result.GetValueOrThrow();
            var read = store.ReadPath(path)!.Value;
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(path);
            return Results.Json(ToDto(options.RootPath, body.Slug, path, false, read),
                ApiJsonContext.Default.DesignDto, statusCode: StatusCodes.Status201Created);
        });

        app.MapMethods("/api/designs/{slug}", new[] { "PATCH" },
            async (string slug, HttpRequest req, CancellationToken ct) =>
        {
            // Resolve active or archived — archived docs stay editable so a
            // finished design can still be amended in place.
            var path = ResolveDesignPath(options.RootPath, slug, out var completed);
            if (path is null) return Problem(404, "not_found", $"no design '{slug}'");
            DesignPatchRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.DesignPatchRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");

            var store = new DesignStore(options.RootPath);
            var read = store.ReadPath(path);
            if (read is null) return Problem(500, "unreadable", "failed to read design doc");
            var precondition = HttpConcurrency.RequireIfMatch(
                req, Etag.OfFile(path));
            if (precondition is not null) return precondition;
            // The body editor owns the leading H1 — mirror it into the
            // frontmatter title so the header + tree label track the edit.
            var fm = new Dictionary<string, object?>(read.Value.Frontmatter, StringComparer.Ordinal);
            var h1 = Knit.Core.Frontmatter.LeadingH1.Extract(body.Body);
            if (h1 is not null) fm["title"] = h1;
            var written = store.WritePath(path, fm, body.Body);
            var refreshed = store.ReadPath(written)!.Value;
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(written);
            return Results.Json(ToDto(options.RootPath, slug, written, completed, refreshed),
                ApiJsonContext.Default.DesignDto);
        });

        app.MapPost("/api/designs/{slug}/archive", async (string slug, HttpRequest req, CancellationToken ct) =>
        {
            DesignArchiveRequestDto? body = null;
            if (req.ContentLength is > 0)
            {
                try
                {
                    body = await JsonSerializer.DeserializeAsync(
                        req.Body, ApiJsonContext.Default.DesignArchiveRequestDto, ct);
                }
                catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            }
            var cycle = string.IsNullOrWhiteSpace(body?.Cycle) ? slug : body!.Cycle!;
            var oldPath = DesignsLayout.DesignDocPath(options.RootPath, slug);
            var repo = new RepoServices(options);
            var result = DesignArchive.MoveToCompleted(
                options.RootPath, slug, cycle, new Knit.Data.ProseRefRewriter(repo.Store, options.RootPath));
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var v = result.GetValueOrThrow();
            return Results.Json(new DesignArchiveResultDto(
                Slug: slug,
                OldPath: Rel(options.RootPath, oldPath),
                NewPath: Rel(options.RootPath, v.Path),
                FilesRelinked: v.FilesRelinked),
                ApiJsonContext.Default.DesignArchiveResultDto);
        });
    }

    private static IEnumerable<(string Slug, string Path, bool Completed)> EnumerateDesigns(
        string root, bool archived)
    {
        if (!archived)
        {
            foreach (var p in DesignsLayout.ActiveDesignDocs(root))
                yield return (DesignsLayout.SlugFromPath(p), p, false);
            yield break;
        }
        // One row per archived slug (the completed/<slug>/ folder). If a slug
        // has multiple cycles, the last (ordinal) stem represents it.
        var completedDir = DesignsLayout.CompletedDir(root);
        if (!Directory.Exists(completedDir)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(completedDir)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var slug = Path.GetFileName(dir);
            var docs = DesignsLayout.CompletedDesignDocs(root, slug);
            if (docs.Count == 0) continue;
            yield return (slug, docs[^1], true);
        }
    }

    private static string? ResolveDesignPath(string root, string slug, out bool completed)
    {
        completed = false;
        var active = DesignsLayout.DesignDocPath(root, slug);
        if (File.Exists(active)) return active;
        var arch = DesignsLayout.CompletedDesignDocs(root, slug);
        if (arch.Count > 0) { completed = true; return arch[^1]; }
        return null;
    }

    private static DesignDto ToDto(string root, string slug, string path, bool completed,
        (IReadOnlyDictionary<string, object?> Frontmatter, string Body) read) =>
        new(
            Slug: slug,
            Title: DesignMapping.TitleOf(slug, read.Frontmatter, read.Body),
            FilePath: Abs(path),
            Completed: completed,
            Body: read.Body,
            Modified: DesignMapping.ModifiedIso(path));

    // Copy-path surface: emit an absolute, OS-native path — same as items
    // (ItemMapping uses Path.GetFullPath). Designs were the odd one out,
    // emitting a repo-relative path.
    private static string Abs(string path) => Path.GetFullPath(path);

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsTrue(string v) =>
        v is "1" or "true" or "True" or "yes";

    private static IResult FromError(KnitError e)
    {
        var status = e.Kind switch
        {
            ErrorKind.NotFound => 404,
            ErrorKind.Conflict => 409,
            _ => 400,
        };
        var code = e.Kind switch
        {
            ErrorKind.NotFound => "not_found",
            ErrorKind.Conflict => "exists",
            _ => "bad_request",
        };
        return Problem(status, code, e.Message);
    }
}
