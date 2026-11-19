using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.DocFolders;
using Knit.Core.IO;
using Knit.Core.Projects;
using Knit.Core.Result;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Mapping;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Browse endpoints for the project doc folders (<c>.knit/projects/</c>)
/// and the milestone backlink surface.
/// Doc content rides the existing file endpoints; these list structure.
/// </summary>
public static class ProjectEndpoints
{
    public static void MapProjects(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/projects", () =>
        {
            var repo = new RepoServices(options);
            var list = new List<ProjectFolderDto>();
            foreach (var slug in ProjectsLayout.ProjectSlugs(repo.Root))
            {
                var dir = ProjectsLayout.ProjectDir(repo.Root, slug);
                var milestones = new List<MilestoneDto>();
                foreach (var file in ProjectsLayout.MilestoneFiles(repo.Root, slug))
                {
                    var (doc, _) = MilestoneFile.Read(file, slug, repo.Yaml);
                    milestones.Add(new MilestoneDto(
                        slug, doc.Slug, doc.Title,
                        doc.Target is { } t ? DueDates.ToWire(t) : null,
                        doc.State.ToWire(),
                        MilestoneResolver.LinkedItems(repo.Store, slug, doc.Slug).Count,
                        doc.Rank));
                }
                // Manual order: ranked milestones by rank, then any
                // unranked (legacy) by slug — matches MilestoneEdit.Reorder.
                milestones = milestones
                    .OrderBy(m => Knit.Core.Ids.Rank.IsValid(m.Rank) ? m.Rank! : "￿", StringComparer.Ordinal)
                    .ThenBy(m => m.Slug, StringComparer.Ordinal)
                    .ToList();
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/'))
                    .Where(rel => !rel.Equals(ProjectsLayout.ReadmeFileName, StringComparison.OrdinalIgnoreCase)
                        && !rel.Equals(ProjectsLayout.RoadmapFileName, StringComparison.OrdinalIgnoreCase)
                        && !rel.StartsWith(ProjectsLayout.MilestonesDirName + "/", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(rel => rel, StringComparer.Ordinal)
                    .ToList();
                list.Add(new ProjectFolderDto(
                    slug,
                    File.Exists(ProjectsLayout.ReadmePath(repo.Root, slug)),
                    File.Exists(ProjectsLayout.RoadmapPath(repo.Root, slug)),
                    milestones,
                    files));
            }
            return Results.Json((IReadOnlyList<ProjectFolderDto>)list, ApiJsonContext.Default.IReadOnlyListProjectFolderDto);
        });

        // Landing row. /api/projects yields folders, which carry no
        // title, health or counts — those live on the project *item*. This joins
        // both halves plus a StatusRollup pass and the next unreached milestone,
        // so the SPA's first row is one round-trip.
        //
        // With no projects it synthesizes the repo-wide "all items" row itself
        // (slug null) rather than leaving the client to notice an empty array and
        // fire a second /api/status — one query, no pop-in.
        app.MapGet("/api/projects/landing", (HttpRequest req) =>
        {
            var offset = Paging.Int(req.Query, "offset", 0);
            var limit = Paging.Int(req.Query, "limit", 5);

            var repo = new RepoServices(options);
            var graph = repo.Graph();
            var today = DateOnly.FromDateTime(DateTime.Now);

            var rows = new List<ProjectLandingDto>();
            foreach (var slug in ProjectsLayout.ProjectSlugs(repo.Root))
            {
                var (scoped, err) = StatusRollup.Scope(repo.Store, repo.Root, graph, slug, null, null, null);
                if (err is not null) continue;
                var r = StatusRollup.Compute(scoped, today, slug);
                var item = scoped.FirstOrDefault(i =>
                    i.Type == ItemType.Project && string.Equals(i.Project, slug, StringComparison.Ordinal));
                var next = NextMilestone(repo, slug);
                rows.Add(new ProjectLandingDto(
                    slug,
                    string.IsNullOrWhiteSpace(item?.Title) ? slug : item!.Title,
                    item?.Id,
                    r.Health?.ToWire(),
                    r.Total, r.Done, r.Percent, r.Blocked, r.Overdue,
                    next?.Slug, next?.Title,
                    next?.Target is { } t ? DueDates.ToWire(t) : null));
            }

            // Attention order: off-track, then at-risk, then the rest; nearest
            // milestone target breaks the tie (ISO dates sort chronologically).
            // Recency would carry almost no signal — projects are few and
            // long-lived.
            IReadOnlyList<ProjectLandingDto> ordered = rows
                .OrderBy(HealthRank)
                .ThenBy(p => p.NextMilestoneTarget ?? "￿", StringComparer.Ordinal)
                .ThenBy(p => p.Slug, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
            {
                var all = StatusRollup.Compute(graph.Items.ToList(), today, "all items");
                ordered = [new ProjectLandingDto(
                    null, "All items", null, null,
                    all.Total, all.Done, all.Percent, all.Blocked, all.Overdue,
                    null, null, null)];
            }

            var page = Paging.Page(ordered, offset, limit, req.HttpContext.Response);
            return Results.Json(page, ApiJsonContext.Default.IReadOnlyListProjectLandingDto);
        });

        // Co-create a project: scaffold the doc folder (readme) + mint-or-link a
        // project tracker item in one action (ProjectCreator, backing the SPA
        // "New project" folder scaffold). The
        // folder-only escape hatch (CLI --no-item) is intentionally not exposed —
        // the web flow never leaves an orphan half. force:false so it never
        // overwrites an existing readme.
        app.MapPost("/api/projects", async (HttpRequest req, CancellationToken ct) =>
        {
            ProjectCreateRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.ProjectCreateRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Slug))
                return Problem(400, "invalid_request", "slug is required");
            if (!ValidSegment(body.Slug)) return Problem(400, "invalid_slug", $"invalid project slug '{body.Slug}'");

            var repo = new RepoServices(options);
            var cfg = ConfigLoader.Load(repo.KnitDir);
            if (cfg.Handle is null) return Problem(409, "no_handle", ConfigKeys.NoHandle);
            var title = string.IsNullOrWhiteSpace(body.Title) ? body.Slug : body.Title!;
            var linkItemId = string.IsNullOrWhiteSpace(body.LinkItemId) ? null : body.LinkItemId;
            var result = ProjectCreator.Create(
                repo.Store, options.RootPath, cfg.Handle,
                body.Slug, title, body.Roadmap, force: false, linkItemId: linkItemId);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            return Results.Json(
                new ProjectCreateResultDto(r.Slug, r.Item.Id, Rel(options.RootPath, r.FolderPath), r.CreatedItem, r.CreatedRoadmap),
                ApiJsonContext.Default.ProjectCreateResultDto, statusCode: StatusCodes.Status201Created);
        });

        app.MapGet("/api/projects/{project}/milestones/{slug}", (string project, string slug, HttpResponse res) =>
        {
            var repo = new RepoServices(options);
            var path = ProjectsLayout.MilestonePath(repo.Root, project, slug);
            if (!File.Exists(path)) return Results.NotFound();
            res.Headers.ETag = Etag.OfFile(path);
            return Results.Json(BuildDetail(repo, options, project, slug, path),
                ApiJsonContext.Default.MilestoneDetailDto);
        });

        // Create a milestone. The project doc folder must already exist (errors
        // NotFound otherwise) and must have a roadmap (errors Conflict → 409:
        // milestones are roadmap-gated). State defaults to planned; a non-default
        // state is applied as a follow-up frontmatter edit so the create path
        // stays a single scaffold call.
        app.MapPost("/api/projects/{project}/milestones", async (string project, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(project)) return Problem(400, "invalid_slug", $"invalid project slug '{project}'");
            MilestoneCreateRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.MilestoneCreateRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Slug))
                return Problem(400, "invalid_request", "slug is required");
            if (!ValidSegment(body.Slug)) return Problem(400, "invalid_slug", $"invalid milestone slug '{body.Slug}'");

            DateOnly? target = null;
            if (!string.IsNullOrWhiteSpace(body.Target))
            {
                if (!DueDates.TryParse(body.Target, out var t))
                    return Problem(400, "invalid_target", $"invalid target '{body.Target}' — expected YYYY-MM-DD");
                target = t;
            }
            MilestoneState? state = null;
            if (!string.IsNullOrWhiteSpace(body.State))
            {
                if (!MilestoneStates.TryParse(body.State, out var st))
                    return Problem(400, "invalid_state", $"unknown state '{body.State}' — expected planned|reached|dropped");
                state = st;
            }

            var repo = new RepoServices(options);
            var title = string.IsNullOrWhiteSpace(body.Title) ? body.Slug : body.Title!;
            var created = ProjectScaffold.NewMilestone(options.RootPath, project, body.Slug, title, target, force: false, repo.Yaml);
            if (created.IsError) return FromError(created.GetErrorOrDefault()!);
            if (state is { } s && s != MilestoneState.Planned)
            {
                var applied = MilestoneEdit.Apply(options.RootPath, project, body.Slug, new MilestoneEdits(State: s), repo.Yaml);
                if (applied.IsError) return FromError(applied.GetErrorOrDefault()!);
            }

            var path = ProjectsLayout.MilestonePath(options.RootPath, project, body.Slug);
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(path);
            return Results.Json(BuildDetail(repo, options, project, body.Slug, path),
                ApiJsonContext.Default.MilestoneDetailDto, statusCode: StatusCodes.Status201Created);
        });

        // Patch frontmatter (state/target/title) and/or body. If-Match guarded
        // to feed the SPA BodyEditorPort. Title↔H1 sync lives in
        // MilestoneEdit.Apply.
        app.MapMethods("/api/projects/{project}/milestones/{slug}", new[] { "PATCH" },
            async (string project, string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(project) || !ValidSegment(slug))
                return Problem(400, "invalid_slug", "invalid project or milestone slug");
            var repo = new RepoServices(options);
            var path = ProjectsLayout.MilestonePath(options.RootPath, project, slug);
            if (!File.Exists(path)) return Problem(404, "not_found", $"no milestone '{project}/{slug}'");
            MilestonePatchRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.MilestonePatchRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");

            var precondition = HttpConcurrency.RequireIfMatch(req, Etag.OfFile(path));
            if (precondition is not null) return precondition;

            MilestoneState? state = null;
            DateOnly? target = null;
            var clearTarget = false;
            string? title = null;
            if (body.Frontmatter is { } fm)
            {
                if (fm.State is not null)
                {
                    if (!MilestoneStates.TryParse(fm.State, out var st))
                        return Problem(400, "invalid_state", $"unknown state '{fm.State}' — expected planned|reached|dropped");
                    state = st;
                }
                if (fm.Title is not null)
                {
                    if (string.IsNullOrWhiteSpace(fm.Title)) return Problem(400, "invalid_title", "title cannot be empty");
                    title = fm.Title;
                }
                if (fm.Target is not null)
                {
                    if (fm.Target.Length == 0) clearTarget = true;
                    else if (!DueDates.TryParse(fm.Target, out var t))
                        return Problem(400, "invalid_target", $"invalid target '{fm.Target}' — expected YYYY-MM-DD");
                    else target = t;
                }
            }

            var edits = new MilestoneEdits(Title: title, State: state, Target: target, ClearTarget: clearTarget, Body: body.Body);
            var result = MilestoneEdit.Apply(options.RootPath, project, slug, edits, repo.Yaml);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);

            req.HttpContext.Response.Headers.ETag = Etag.OfFile(path);
            return Results.Json(BuildDetail(repo, options, project, slug, path),
                ApiJsonContext.Default.MilestoneDetailDto);
        });

        // Rename (slug change): re-points item back-links via MilestoneMutator.
        app.MapPost("/api/projects/{project}/milestones/{slug}/rename",
            async (string project, string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(project) || !ValidSegment(slug))
                return Problem(400, "invalid_slug", "invalid project or milestone slug");
            MilestoneRenameRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.MilestoneRenameRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.NewSlug))
                return Problem(400, "invalid_request", "newSlug is required");

            var repo = new RepoServices(options);
            var result = MilestoneMutator.Rename(repo.Store, options.RootPath, project, slug, body.NewSlug);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            return Results.Json(new MilestoneRenameResultDto(
                r.Project, r.OldSlug, r.NewSlug, Rel(options.RootPath, r.OldPath), Rel(options.RootPath, r.NewPath), r.RepointedItemIds, r.FilesRelinked),
                ApiJsonContext.Default.MilestoneRenameResultDto);
        });

        // Move into another project: re-points item back-links and
        // reports which of them now cross a project boundary.
        app.MapPost("/api/projects/{project}/milestones/{slug}/move",
            async (string project, string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(project) || !ValidSegment(slug))
                return Problem(400, "invalid_slug", "invalid project or milestone slug");
            MilestoneMoveRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.MilestoneMoveRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.ToProject))
                return Problem(400, "invalid_request", "toProject is required");
            if (!ValidSegment(body.ToProject)) return Problem(400, "invalid_slug", $"invalid project slug '{body.ToProject}'");
            var newSlug = string.IsNullOrWhiteSpace(body.NewSlug) ? null : body.NewSlug;
            if (newSlug is not null && !ValidSegment(newSlug))
                return Problem(400, "invalid_slug", $"invalid milestone slug '{newSlug}'");

            var repo = new RepoServices(options);
            var result = MilestoneMutator.Move(repo.Store, options.RootPath, project, slug, body.ToProject, newSlug);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            return Results.Json(new MilestoneMoveResultDto(
                r.FromProject, r.ToProject, r.OldSlug, r.NewSlug,
                Rel(options.RootPath, r.OldPath), Rel(options.RootPath, r.NewPath),
                r.RepointedItemIds, r.CrossProjectItemIds, r.FilesRelinked),
                ApiJsonContext.Default.MilestoneMoveResultDto);
        });

        // Reorder milestones: rewrite fractional ranks to match the
        // client's desired order. Body carries the full slug order (a
        // permutation of the project's milestones); the server assigns ranks
        // so clients never construct a rank string. Powers the roadmap-spine
        // drag-and-drop.
        app.MapPost("/api/projects/{project}/milestones/reorder",
            async (string project, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(project)) return Problem(400, "invalid_slug", $"invalid project slug '{project}'");
            MilestoneReorderRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.MilestoneReorderRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || body.Order is null || body.Order.Count == 0)
                return Problem(400, "invalid_request", "order is required");

            var repo = new RepoServices(options);
            var result = MilestoneEdit.Reorder(options.RootPath, project, body.Order, repo.Yaml);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            return Results.Json(new MilestoneReorderResultDto(project, result.GetValueOrThrow()),
                ApiJsonContext.Default.MilestoneReorderResultDto);
        });

        // Delete. Back-links left dangling by default; ?scrubLinks=true removes
        // them; ?dryRun=true previews the impact without writing.
        app.MapDelete("/api/projects/{project}/milestones/{slug}", (string project, string slug, HttpRequest req) =>
        {
            if (!ValidSegment(project) || !ValidSegment(slug))
                return Problem(400, "invalid_slug", "invalid project or milestone slug");
            var repo = new RepoServices(options);
            var path = ProjectsLayout.MilestonePath(options.RootPath, project, slug);
            if (!File.Exists(path)) return Problem(404, "not_found", $"no milestone '{project}/{slug}'");

            var scrub = Flag(req.Query, "scrubLinks");
            if (Flag(req.Query, "dryRun"))
            {
                var dangling = MilestoneResolver.LinkedItems(repo.Store, project, slug)
                    .Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
                return Results.Json(new MilestoneDeleteResultDto(project, slug, true, Rel(options.RootPath, path), dangling, []),
                    ApiJsonContext.Default.MilestoneDeleteResultDto);
            }

            var result = MilestoneMutator.Delete(repo.Store, options.RootPath, project, slug, scrub);
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            return Results.Json(new MilestoneDeleteResultDto(
                project, slug, false, Rel(options.RootPath, r.DeletedPath), r.DanglingItemIds, r.ScrubbedItemIds),
                ApiJsonContext.Default.MilestoneDeleteResultDto);
        });

        // DELETE /api/projects/{slug}?dryRun=&scrubLinks= — remove a project:
        // deletes the project item + doc folder but RE-ROOTS (does not cascade)
        // direct children. Back-links pointing here are left dangling unless
        // scrubLinks=true. dryRun=true returns the impact without writing. The
        // doc-folder deletion is PathContainment-guarded inside ProjectRemover
        // (never touches anything outside .knit/projects/).
        app.MapDelete("/api/projects/{slug}", (string slug, HttpRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(slug) || slug.Contains('/') || slug.Contains('\\')
                || slug == "." || slug == "..")
                return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");

            var repo = new RepoServices(options);
            var folderExists = Directory.Exists(ProjectsLayout.ProjectDir(repo.Root, slug));
            var hasItem = repo.Store.AllItems().Any(i => string.Equals(i.Project, slug, StringComparison.Ordinal));
            if (!folderExists && !hasItem)
                return Problem(404, "project_not_found",
                    $"no project '{slug}' — neither a doc folder nor a project item points at it");

            if (Flag(req.Query, "dryRun"))
            {
                var impact = ProjectRemover.Preview(repo.Store, repo.Root, slug);
                return Results.Json(new ProjectDeleteResultDto(
                    slug, true, impact.ItemId, impact.DirectChildIds, [],
                    impact.DanglingItemIds, impact.MilestoneCount, impact.FileCount, false),
                    ApiJsonContext.Default.ProjectDeleteResultDto);
            }

            var result = ProjectRemover.Remove(repo.Store, repo.Root, slug, Flag(req.Query, "scrubLinks"));
            return Results.Json(new ProjectDeleteResultDto(
                slug, false, result.RemovedItemId, result.ReRootedChildIds, result.ScrubbedItemIds,
                result.DanglingItemIds, result.DeletedMilestoneCount, result.DeletedFileCount, result.FolderDeleted),
                ApiJsonContext.Default.ProjectDeleteResultDto);
        });
        // ---- project doc CRUD ----
        // readme/roadmap/free-form docs share the "entries + folder entries"
        // KB shape. Milestones have their own routes above and are rejected
        // here; readme/roadmap are recognized entries (body-editable, not
        // create/delete/rename-able as free-form). Every path is
        // containment-guarded via ProjectDoc.Resolve.

        // Scaffold roadmap.md standalone — errors if it exists, never touches
        // the readme (backs the SPA "Add roadmap" action).
        app.MapPost("/api/projects/{slug}/roadmap", (string slug) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            var title = ReadmeTitle(options.RootPath, slug) ?? slug;
            var created = ProjectScaffold.NewRoadmap(options.RootPath, slug, title, force: false);
            if (created.IsError) return FromError(created.GetErrorOrDefault()!);
            return Results.Json(
                new ProjectRoadmapResultDto(slug, Rel(options.RootPath, created.GetValueOrThrow())),
                ApiJsonContext.Default.ProjectRoadmapResultDto, statusCode: StatusCodes.Status201Created);
        });

        // Create a free-form doc (flat or folder form).
        app.MapPost("/api/projects/{slug}/docs", async (string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            ProjectDocCreateRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.ProjectDocCreateRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");
            var docSlug = string.IsNullOrWhiteSpace(body.Slug) ? body.Title : body.Slug;
            if (string.IsNullOrWhiteSpace(docSlug))
                return Problem(400, "invalid_request", "slug or title is required");

            var repo = new RepoServices(options);
            var created = ProjectDoc.CreateFreeForm(options.RootPath, slug, docSlug!, body.Title, body.AsFolder);
            if (created.IsError) return FromError(created.GetErrorOrDefault()!);
            var path = created.GetValueOrThrow().Path;
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(path);
            return Results.Json(BuildDoc(repo, options, slug, path),
                ApiJsonContext.Default.ProjectDocDto, statusCode: StatusCodes.Status201Created);
        });

        // Rename / move a free-form doc (POST, not on the catch-all so the verb
        // + literal segment disambiguates from the {*path} routes below).
        app.MapPost("/api/projects/{slug}/docs/rename", async (string slug, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            ProjectDocRenameRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.ProjectDocRenameRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.OldPath) || string.IsNullOrWhiteSpace(body.NewPath))
                return Problem(400, "invalid_request", "oldPath and newPath are required");
            if (ProjectDoc.IsReserved(body.OldPath))
                return Problem(400, "reserved", $"'{body.OldPath}' is reserved — readme/roadmap and milestones have their own commands");

            var repo = new RepoServices(options);
            var result = ProjectDoc.Move(
                options.RootPath, slug, body.OldPath, body.NewPath, new Knit.Data.ProseRefRewriter(repo.Store, options.RootPath));
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            return Results.Json(
                new ProjectDocRenameResultDto(slug, body.OldPath, body.NewPath, r.WasFolderForm, r.FilesRelinked),
                ApiJsonContext.Default.ProjectDocRenameResultDto);
        });

        // Read a doc body + ETag (readme/roadmap/free-form; milestones rejected).
        app.MapGet("/api/projects/{slug}/docs/{*path}", (string slug, string path, HttpResponse res) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            if (ProjectDoc.IsReserved(path) && IsMilestonePath(path))
                return Problem(400, "milestone_path", "milestones have their own endpoint (/api/projects/{slug}/milestones/{slug})");
            var repo = new RepoServices(options);
            var resolved = ProjectDoc.Resolve(options.RootPath, slug, path);
            if (resolved.IsError) return FromError(resolved.GetErrorOrDefault()!);
            var full = resolved.GetValueOrThrow();
            if (!File.Exists(full)) return Problem(404, "not_found", $"no doc at {path}");
            res.Headers.ETag = Etag.OfFile(full);
            return Results.Json(BuildDoc(repo, options, slug, full), ApiJsonContext.Default.ProjectDocDto);
        });

        // Patch a doc body, If-Match guarded (readme/roadmap/free-form).
        app.MapMethods("/api/projects/{slug}/docs/{*path}", new[] { "PATCH" },
            async (string slug, string path, HttpRequest req, CancellationToken ct) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            if (IsMilestonePath(path))
                return Problem(400, "milestone_path", "milestones have their own endpoint");
            var repo = new RepoServices(options);
            var resolved = ProjectDoc.Resolve(options.RootPath, slug, path);
            if (resolved.IsError) return FromError(resolved.GetErrorOrDefault()!);
            var full = resolved.GetValueOrThrow();
            var read = DocBody.ReadRaw(full, repo.Yaml);
            if (read is null) return Problem(404, "not_found", $"no doc at {path}");
            ProjectDocPatchRequestDto? body;
            try { body = await JsonSerializer.DeserializeAsync(req.Body, ApiJsonContext.Default.ProjectDocPatchRequestDto, ct); }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");

            var precondition = HttpConcurrency.RequireIfMatch(req, Etag.OfFile(full));
            if (precondition is not null) return precondition;

            var newBody = body.Body ?? read.Value.Body;
            DocBody.WriteRaw(full, read.Value.Frontmatter, newBody, repo.Yaml);
            req.HttpContext.Response.Headers.ETag = Etag.OfFile(full);
            return Results.Json(BuildDoc(repo, options, slug, full), ApiJsonContext.Default.ProjectDocDto);
        });

        // Delete a free-form doc/folder (readme/roadmap/milestones rejected).
        app.MapDelete("/api/projects/{slug}/docs/{*path}", (string slug, string path, HttpRequest req) =>
        {
            if (!ValidSegment(slug)) return Problem(400, "invalid_slug", $"invalid project slug '{slug}'");
            if (ProjectDoc.IsReserved(path))
                return Problem(400, "reserved", $"'{path}' is reserved — delete the project itself, not readme/roadmap/milestones");
            var result = ProjectDoc.Delete(options.RootPath, slug, path, Flag(req.Query, "deleteFolder"));
            if (result.IsError) return FromError(result.GetErrorOrDefault()!);
            var r = result.GetValueOrThrow();
            var status = !r.Deleted ? StatusCodes.Status409Conflict : StatusCodes.Status200OK;
            return Results.Json(
                new ProjectDocDeleteResultDto(slug, path, r.Deleted, r.IsFolderForm, r.BlockingFiles.Select(b => Rel(options.RootPath, b)).ToList()),
                ApiJsonContext.Default.ProjectDocDeleteResultDto, statusCode: status);
        });
    }

    private static int HealthRank(ProjectLandingDto p) => p.Health switch
    {
        "off-track" => 0,
        "at-risk" => 1,
        _ => 2,
    };

    /// <summary>
    /// The next unreached milestone: nearest target first, undated ones last in
    /// roadmap (rank) order. Null when the project has no planned milestone.
    /// </summary>
    private static MilestoneDoc? NextMilestone(RepoServices repo, string slug) =>
        ProjectsLayout.MilestoneFiles(repo.Root, slug)
            .Select(f => MilestoneFile.Read(f, slug, repo.Yaml).Doc)
            .Where(m => m.State == MilestoneState.Planned)
            .OrderBy(m => m.Target ?? DateOnly.MaxValue)
            .ThenBy(m => Knit.Core.Ids.Rank.IsValid(m.Rank) ? m.Rank! : "￿", StringComparer.Ordinal)
            .ThenBy(m => m.Slug, StringComparer.Ordinal)
            .FirstOrDefault();

    private static ProjectDocDto BuildDoc(RepoServices repo, WebApiOptions options, string slug, string fullPath)
    {
        var read = DocBody.ReadRaw(fullPath, repo.Yaml);
        var body = read?.Body ?? string.Empty;
        var rel = Path.GetRelativePath(ProjectsLayout.ProjectDir(options.RootPath, slug), fullPath).Replace('\\', '/');
        return new ProjectDocDto(slug, rel, body, Path.GetFullPath(fullPath));
    }

    private static bool IsMilestonePath(string relPath)
    {
        var rel = relPath.Replace('\\', '/').Trim('/');
        return rel.StartsWith(ProjectsLayout.MilestonesDirName + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadmeTitle(string root, string slug)
    {
        var path = ProjectsLayout.ReadmePath(root, slug);
        if (!File.Exists(path)) return null;
        var h1 = Knit.Core.Frontmatter.LeadingH1.Extract(AtomicFile.ReadAllText(path));
        return string.IsNullOrWhiteSpace(h1) ? null : h1;
    }

    private static MilestoneDetailDto BuildDetail(
        RepoServices repo, WebApiOptions options, string project, string slug, string path)
    {
        var (doc, _) = MilestoneFile.Read(path, project, repo.Yaml);
        var body = MilestoneFile.ReadBody(path) ?? string.Empty;
        var graph = repo.Graph();
        var shortener = repo.Shortener();
        var pathMap = ItemMapping.BuildPathMap(options.RootPath);
        var backlinks = MilestoneResolver.LinkedItems(repo.Store, project, slug)
            .Select(i => ItemMapping.ToSummary(i, shortener, graph, pathMap))
            .ToList();
        return new MilestoneDetailDto(
            project, doc.Slug, doc.Title,
            doc.Target is { } t ? DueDates.ToWire(t) : null,
            doc.State.ToWire(),
            body,
            backlinks,
            Path.GetFullPath(path),
            doc.Rank);
    }

    // A path segment must be a single non-empty name — no separators or dot
    // segments — so it can't escape .knit/projects/ via the route.
    private static bool ValidSegment(string s) =>
        !string.IsNullOrWhiteSpace(s) && !s.Contains('/') && !s.Contains('\\') && s != "." && s != "..";

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IResult FromError(KnitError e)
    {
        var (status, code) = e.Kind switch
        {
            ErrorKind.NotFound => (404, "not_found"),
            ErrorKind.Conflict => (409, "exists"),
            _ => (400, "bad_request"),
        };
        return Problem(status, code, e.Message);
    }

    private static bool Flag(IQueryCollection query, string name)
    {
        var v = query[name].ToString().Trim().ToLowerInvariant();
        return v is "1" or "true" or "yes";
    }
}
