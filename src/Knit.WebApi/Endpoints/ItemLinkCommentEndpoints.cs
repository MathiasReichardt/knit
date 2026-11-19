using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Projects;
using Knit.Core.Refs;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Mapping;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using static Knit.WebApi.ApiProblem;

namespace Knit.WebApi.Endpoints;

/// <summary>
/// Link + comment mutators for an item. Both surfaces match the CLI
/// semantics (<see cref="LinkRel"/> rows in the body <c>## Links</c>
/// table; comments appended to the body) so SPA and CLI mutations
/// produce identical on-disk results.
/// </summary>
public static class ItemLinkCommentEndpoints
{
    private static readonly HashSet<LinkRel> RetargetableRels = [LinkRel.Blocks, LinkRel.BlockedBy, LinkRel.Related];

    // Re-adding a link: no note keeps the current one, empty clears it (as `knit link --note`).
    private static string? Note(string? requested, string? current) =>
        requested is null ? current : requested.Length == 0 ? null : requested;

    public static void MapItemLinkComment(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        // ---- Links ---------------------------------------------------------

        app.MapPost("/api/items/{id}/links", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            LinkRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.LinkRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");
            if (string.IsNullOrWhiteSpace(body.Rel)) return Problem(400, "invalid_rel", "rel is required");
            if (string.IsNullOrWhiteSpace(body.Target)) return Problem(400, "invalid_target", "target is required");

            if (!LinkRels.TryParse(body.Rel, out var rel))
                return Problem(400, "invalid_rel",
                    $"unknown rel '{body.Rel}' (expected parent|blocks|blocked-by|related|kb-ref|milestone|branch|repo|pr|commit)");

            var sourceFile = repo.Store.FindItemFile(current.Id);
            var sourceDir = sourceFile is not null
                ? Path.GetDirectoryName(sourceFile)!
                : repo.Store.ItemsDir;

            Item updated;
            if (ExternalRefs.IsExternal(rel))
            {
                // Branch / Repo / PR / Commit: the target is outside the repo, so it is
                // validated (mount, directory, URL) rather than resolved to a file.
                var cfg = ConfigLoader.Load(repo.KnitDir);
                if (!ExternalRefs.TryResolve(rel, body.Target, options.RootPath, cfg, out var external, out var extErr))
                    return Problem(400, "invalid_target", extErr!);
                var kept = current.LinkRows.FirstOrDefault(r => r.Rel == rel
                    && string.Equals(r.TargetText, external.Text, StringComparison.Ordinal))?.Notes;
                var rows = current.LinkRows.Where(r => !(r.Rel == rel
                    && string.Equals(r.TargetText, external.Text, StringComparison.Ordinal))).ToList();
                rows.Add(new LinkRow(rel, external.Text, external.Path, Note(body.Notes, kept)));
                updated = current with { LinkRows = rows };
            }
            else if (rel == LinkRel.KbRef)
            {
                var parsed = Ref.Parse(body.Target);
                if (parsed.IsError)
                    return Problem(400, "invalid_target", parsed.GetErrorOrDefault()!.Message);
                var kbRef = parsed.GetValueOrThrow();
                if (kbRef.Bucket == Bucket.Item)
                    return Problem(400, "invalid_target",
                        "kb-ref expects a KB bucket (adr, doc, feature, log, idea, source)");
                var resolved = RefResolver.Resolve(kbRef, options.RootPath).GetValueOrThrow();
                if (!resolved.Exists)
                    return Problem(404, "kb_target_not_found",
                        $"unresolved kb ref '{kbRef}': no file at {resolved.Path}");

                var path = LinkPath.Relative(sourceDir, resolved.Path);
                var text = kbRef.ToString();
                var rows = current.LinkRows.ToList();
                var existing = -1;
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Rel == LinkRel.KbRef &&
                        string.Equals(rows[i].TargetPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }
                if (existing >= 0)
                    rows[existing] = new LinkRow(LinkRel.KbRef, text, path, Note(body.Notes, rows[existing].Notes));
                else
                    rows.Add(new LinkRow(LinkRel.KbRef, text, path, Note(body.Notes, null)));
                updated = current with { LinkRows = rows };
            }
            else if (rel == LinkRel.Milestone)
            {
                // Target is a milestone ref (<slug> or <project>/<slug>), not an
                // item — resolve it to the sub-doc file and mirror the CLI's
                // WithMilestone: one milestone per item, so replace any existing.
                var (target, mErr) = Knit.Data.MilestoneResolver.Resolve(
                    repo.Store, options.RootPath, current, body.Target);
                if (target is null) return Problem(404, "milestone_not_found", mErr!);
                var (doc, _) = MilestoneFile.Read(target.Path, target.ProjectSlug, repo.Yaml);
                var path = LinkPath.Relative(sourceDir, target.Path);
                var kept = current.LinkRows.FirstOrDefault(r => r.Rel == LinkRel.Milestone
                    && string.Equals(r.TargetPath, path, StringComparison.OrdinalIgnoreCase))?.Notes;
                var rows = current.LinkRows.Where(r => r.Rel != LinkRel.Milestone).ToList();
                rows.Add(new LinkRow(LinkRel.Milestone, doc.Title, path, Note(body.Notes, kept)));
                updated = current with { LinkRows = rows };
            }
            else
            {
                var targetSlug = Ref.StripItemPrefix(body.Target) ?? body.Target;
                var target = repo.Store.Read(targetSlug);
                if (target is null)
                    return Problem(404, "target_not_found", $"target not found: {body.Target}");
                if (string.Equals(target.Id, current.Id, StringComparison.Ordinal) && rel != LinkRel.Related)
                    return Problem(400, "self_link", "an item cannot link to itself");

                if (rel == LinkRel.Parent && !Hierarchy.ParentAllowed(current.Type, target.Type))
                    return Problem(400, "invalid_parent",
                        Hierarchy.ExplainRefusal(current.Type, target.Type));

                var targetFile = repo.Store.FindItemFile(target.Id)!;
                var path = LinkPath.Relative(sourceDir, targetFile);

                if (rel == LinkRel.Parent)
                {
                    var kept = current.Parent == target.Id ? current.LinkRows.FirstOrDefault(r => r.Rel == LinkRel.Parent)?.Notes : null;
                    var rows = current.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList();
                    rows.Insert(0, new LinkRow(LinkRel.Parent, target.Title, path, Note(body.Notes, kept)));
                    updated = current with { Parent = target.Id, LinkRows = rows };
                }
                else
                {
                    var rows = current.LinkRows.ToList();
                    var existing = rows.FindIndex(r => r.Rel == rel
                        && LinkPath.ItemIdFromTarget(r.TargetPath) == target.Id);
                    if (existing >= 0)
                        rows[existing] = rows[existing] with { Notes = Note(body.Notes, rows[existing].Notes) };
                    else
                        rows.Add(new LinkRow(rel, target.Title, path, Note(body.Notes, null)));
                    updated = current with { LinkRows = rows };
                }
            }

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapMethods("/api/items/{id}/links", ["PATCH"], async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            LinkEditRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.LinkEditRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");
            if (!LinkRels.TryParse(body.Rel, out var rel))
                return Problem(400, "invalid_rel", $"unknown rel '{body.Rel}'");

            var rows = current.LinkRows.ToList();
            var index = rows.FindIndex(r => r.Rel == rel
                && string.Equals(r.TargetPath, body.TargetPath, StringComparison.Ordinal)
                && string.Equals(r.TargetText, body.TargetText, StringComparison.Ordinal));
            if (index < 0)
                return Problem(404, "link_not_found", $"no {LinkRels.ToWire(rel)} row targets '{body.TargetText}'");
            var row = rows[index];

            var newRel = rel;
            if (!string.IsNullOrWhiteSpace(body.NewRel))
            {
                if (!LinkRels.TryParse(body.NewRel, out newRel))
                    return Problem(400, "invalid_rel", $"unknown rel '{body.NewRel}'");
                // Other rels carry their own rules (one Parent with a hierarchy
                // check, one Milestone, KB / external targets) — remove and re-add.
                if (newRel != rel && (!RetargetableRels.Contains(rel) || !RetargetableRels.Contains(newRel)))
                    return Problem(400, "invalid_rel",
                        "only blocks, blocked-by and related can be changed into each other; remove and re-add the link instead");
                var targetId = LinkPath.ItemIdFromTarget(row.TargetPath);
                if (newRel != rel && targetId is not null && rows.Any(r => r.Rel == newRel && LinkPath.ItemIdFromTarget(r.TargetPath) == targetId))
                    return Problem(409, "duplicate_link", $"a {LinkRels.ToWire(newRel)} link to that item already exists");
                if (newRel != rel && targetId == current.Id && newRel != LinkRel.Related)
                    return Problem(400, "self_link", "an item cannot link to itself");
            }

            rows[index] = row with
            {
                Rel = newRel,
                Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim(),
            };
            var updated = current with { LinkRows = rows };
            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        // The target is a catch-all so KB refs (`kb-ref/doc:architecture`)
        // and bare item ids both fit a single route. Rel still occupies a
        // single segment.
        app.MapDelete("/api/items/{id}/links/{rel}/{*target}",
            (string id, string rel, string target, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            if (!LinkRels.TryParse(rel, out var parsedRel))
                return Problem(400, "invalid_rel", $"unknown rel '{rel}'");
            if (string.IsNullOrWhiteSpace(target))
                return Problem(400, "invalid_target", "target is required");

            Item updated;
            if (ExternalRefs.IsExternal(parsedRel))
            {
                var rows = current.LinkRows.Where(r => !(r.Rel == parsedRel
                    && (string.Equals(r.TargetText, target, StringComparison.Ordinal)
                        || string.Equals(r.TargetPath, target, StringComparison.Ordinal)))).ToList();
                if (rows.Count == current.LinkRows.Count)
                    return Problem(404, "target_not_found", $"no {LinkRels.ToWire(parsedRel)} row targets '{target}'");
                updated = current with { LinkRows = rows };
            }
            else if (parsedRel == LinkRel.Parent && (target == "parent" || string.IsNullOrEmpty(target)))
            {
                updated = current with
                {
                    Parent = null,
                    LinkRows = current.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList(),
                };
            }
            else if (parsedRel == LinkRel.KbRef)
            {
                var parsedRef = Ref.Parse(target);
                if (parsedRef.IsError)
                    return Problem(400, "invalid_target", parsedRef.GetErrorOrDefault()!.Message);
                var resolved = RefResolver.Resolve(parsedRef.GetValueOrThrow(), options.RootPath).GetValueOrThrow();
                var sourceFile = repo.Store.FindItemFile(current.Id);
                var sourceDir = sourceFile is not null
                    ? Path.GetDirectoryName(sourceFile)!
                    : repo.Store.ItemsDir;
                var path = LinkPath.Relative(sourceDir, resolved.Path);
                var rows = current.LinkRows
                    .Where(r => !(r.Rel == LinkRel.KbRef
                        && string.Equals(r.TargetPath, path, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                updated = current with { LinkRows = rows };
            }
            else if (parsedRel == LinkRel.Milestone)
            {
                // Target is a milestone ref (<slug> or <project>/<slug>) — match
                // the item's own Milestone rows by slug (+ project when given),
                // mirroring `knit unlink`.
                var slash = target.IndexOf('/');
                var msProject = slash >= 0 ? target[..slash] : null;
                var msSlug = slash >= 0 ? target[(slash + 1)..] : target;
                var rows = current.LinkRows
                    .Where(r => !(r.Rel == LinkRel.Milestone
                        && LinkPath.MilestoneFromTarget(r.TargetPath) is { } msId
                        && string.Equals(msId.Slug, msSlug, StringComparison.Ordinal)
                        && (msProject is null || string.Equals(msId.Project, msProject, StringComparison.Ordinal))))
                    .ToList();
                updated = current with { LinkRows = rows };
            }
            else
            {
                var targetSlug = Ref.StripItemPrefix(target) ?? target;
                var targetItem = repo.Store.Read(targetSlug);
                if (targetItem is null)
                    return Problem(404, "target_not_found", $"target not found: {target}");
                var rows = current.LinkRows
                    .Where(r => !(r.Rel == parsedRel
                        && LinkPath.ItemIdFromTarget(r.TargetPath) == targetItem.Id))
                    .ToList();
                var clearedParent = parsedRel == LinkRel.Parent
                    && string.Equals(current.Parent, targetItem.Id, StringComparison.Ordinal);
                updated = current with
                {
                    LinkRows = rows,
                    Parent = clearedParent ? null : current.Parent,
                };
            }

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        // ---- Comments ------------------------------------------------------

        app.MapPost("/api/items/{id}/comments", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            CommentRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.CommentRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Body))
                return Problem(400, "invalid_body", "comment body is required");

            var cfg = ConfigLoader.Load(repo.KnitDir);
            if (cfg.Handle is null) return Problem(409, "no_handle", ConfigKeys.NoHandle);
            var comment = new Comment(
                IdGenerator.New(),
                cfg.Handle,
                DateTimeOffset.UtcNow,
                body.Body);

            var updated = current with { Comments = current.Comments.Concat([comment]).ToList() };
            repo.Store.Write(updated);

            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var itemDto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            var commentDto = new CommentDto(comment.Id, comment.Author, comment.Created, comment.Body);
            return Results.Json(
                new CommentMutationResultDto(itemDto, commentDto),
                ApiJsonContext.Default.CommentMutationResultDto,
                statusCode: StatusCodes.Status201Created)
                .WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapMethods("/api/items/{id}/comments/{commentId}", new[] { "PATCH" },
            async (string id, string commentId, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            CommentPatchDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.CommentPatchDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Body))
                return Problem(400, "invalid_body", "comment body is required");

            var existing = current.Comments.FirstOrDefault(c =>
                string.Equals(c.Id, commentId, StringComparison.Ordinal));
            if (existing is null)
                return Problem(404, "comment_not_found", $"comment not found: {commentId}");

            var edited = existing with { Body = body.Body };
            var comments = current.Comments.Select(c =>
                string.Equals(c.Id, commentId, StringComparison.Ordinal) ? edited : c).ToList();
            var updated = current with { Comments = comments };

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapDelete("/api/items/{id}/comments/{commentId}",
            (string id, string commentId, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var precondition)) return precondition!;

            var existing = current.Comments.FirstOrDefault(c =>
                string.Equals(c.Id, commentId, StringComparison.Ordinal));
            if (existing is null)
                return Problem(404, "comment_not_found", $"comment not found: {commentId}");

            var comments = current.Comments.Where(c =>
                !string.Equals(c.Id, commentId, StringComparison.Ordinal)).ToList();
            var updated = current with { Comments = comments };

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });
    }

    private static bool RequireIfMatch(HttpRequest req, ItemStore store, Item current, out IResult? failure)
    {
        var currentEtag = Etag.OfItem(store, current.Id);
        var ifMatch = req.Headers[HeaderNames.IfMatch].ToString();
        if (string.IsNullOrEmpty(ifMatch))
        {
            failure = Problem(428, "precondition_required", "If-Match header is required");
            return false;
        }
        if (!EtagMatches(ifMatch, currentEtag))
        {
            failure = Results.Json(
                new ProblemDto("etag_mismatch", 412,
                    $"current etag {currentEtag} does not match If-Match {ifMatch}",
                    "etag_mismatch"),
                ApiJsonContext.Default.ProblemDto,
                statusCode: StatusCodes.Status412PreconditionFailed);
            return false;
        }
        failure = null;
        return true;
    }

    private static bool EtagMatches(string headerValue, string current)
    {
        if (string.IsNullOrEmpty(headerValue)) return false;
        if (headerValue.Trim() == "*") return true;
        foreach (var part in headerValue.Split(','))
        {
            var v = part.Trim();
            if (v.StartsWith("W/", StringComparison.Ordinal)) v = v[2..];
            if (v == current) return true;
        }
        return false;
    }
}
