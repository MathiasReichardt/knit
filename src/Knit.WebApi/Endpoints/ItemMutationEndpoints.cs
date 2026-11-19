using System.Net.Mime;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.Templates;
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
/// Item mutators: <c>POST /api/items</c>, <c>PATCH /api/items/{id}</c>.
/// Both write through the same <see cref="ItemStore"/> the CLI uses, so SPA
/// and CLI mutations are observationally identical.
/// </summary>
public static class ItemMutationEndpoints
{
    public static void MapItemMutations(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapPost("/api/items", async (HttpRequest req, CancellationToken ct) =>
        {
            NewItemRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.NewItemRequestDto, ct);
            }
            catch (JsonException ex)
            {
                return Problem(400, "invalid_json", ex.Message);
            }
            if (body is null) return Problem(400, "missing_body", "request body required");
            if (string.IsNullOrWhiteSpace(body.Title))
                return Problem(400, "invalid_title", "title is required");
            if (!EnumStrings.TryParseItemType(body.Type, out var type))
                return Problem(400, "invalid_type", $"unknown type '{body.Type}' (expected project|epic|feature|bug|task|todo)");

            var repo = new RepoServices(options);
            Item? parent = null;
            if (!string.IsNullOrEmpty(body.Parent))
            {
                parent = repo.Store.Read(body.Parent!);
                if (parent is null) return Problem(404, "parent_not_found", $"parent not found: {body.Parent}");
            }
            if (!Hierarchy.ParentAllowed(type, parent?.Type))
                return Problem(400, "invalid_parent",
                    Hierarchy.ExplainRefusal(type, parent?.Type));

            var cfg = ConfigLoader.Load(repo.KnitDir);
            if (cfg.Handle is null) return Problem(409, "no_handle", ConfigKeys.NoHandle);
            var id = repo.Store.NewItemId();
            string template;
            try
            {
                template = TemplateEngine.Load(options.RootPath, type, body.Template);
            }
            catch (ArgumentException ex) { return Problem(400, "invalid_template", ex.Message); }

            var rendered = TemplateEngine.Render(
                template, TemplateEngine.StandardValues(id, body.Title, cfg.Handle, type));

            var nextRank = ComputeNextRank(repo.Store);

            var linkRows = new List<LinkRow>();
            if (parent is not null)
            {
                var parentFile = repo.Store.FindItemFile(parent.Id);
                if (parentFile is not null)
                {
                    var path = LinkPath.Relative(repo.Store.ItemsDir, parentFile);
                    linkRows.Add(new LinkRow(LinkRel.Parent, parent.Title, path, null));
                }
            }

            var item = new Item
            {
                Id = id,
                Title = body.Title,
                Type = type,
                State = ItemState.Todo,
                Parent = parent?.Id,
                LinkRows = linkRows,
                Rank = nextRank,
                Created = DateTimeOffset.UtcNow,
                Body = rendered,
            };

            repo.Store.Write(item);

            var dto = ItemMapping.ToDto(item, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(
                dto, ApiJsonContext.Default.ItemDto,
                statusCode: StatusCodes.Status201Created)
                .WithETag(Etag.OfItem(repo.Store, item.Id))
                .WithLocation($"/api/items/{item.Id}");
        });

        app.MapMethods("/api/items/{id}", new[] { "PATCH" }, async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");

            var currentEtag = Etag.OfItem(repo.Store, current.Id);
            var ifMatch = req.Headers[HeaderNames.IfMatch].ToString();
            if (string.IsNullOrEmpty(ifMatch))
                return Problem(428, "precondition_required", "If-Match header is required for PATCH");
            if (!EtagMatches(ifMatch, currentEtag))
                return Results.Json(
                    new ProblemDto("etag_mismatch", 412,
                        $"current etag {currentEtag} does not match If-Match {ifMatch}",
                        "etag_mismatch"),
                    ApiJsonContext.Default.ProblemDto,
                    statusCode: StatusCodes.Status412PreconditionFailed);

            ItemPatchRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.ItemPatchRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null) return Problem(400, "missing_body", "request body required");

            var (updated, error) = ApplyPatch(current, body, repo);
            if (error is not null) return error;

            repo.Store.Write(updated!);
            var dependents = CloseEffects.BlockedDependents(repo.Store, [(current, updated!)]);

            var fresh = repo.Store.Read(updated!.Id) ?? updated;
            var shortener = repo.Shortener();
            var dto = ItemMapping.ToDto(fresh, shortener, options.RootPath, repo.Store.Read) with
            {
                BlockedDependents = DependentDtos(dependents.GetValueOrDefault(fresh.Id), shortener),
            };
            return Results.Json(dto, ApiJsonContext.Default.ItemDto)
                .WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapPost("/api/items/{id}/block", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            BlockRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.BlockRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            body ??= new BlockRequestDto(null, null);
            if (string.IsNullOrWhiteSpace(body.By) && string.IsNullOrWhiteSpace(body.Reason))
                return Problem(400, "missing_block_args", "at least one of `by` or `reason` is required");

            Item? blocker = null;
            if (!string.IsNullOrWhiteSpace(body.By))
            {
                blocker = repo.Store.Read(body.By!);
                if (blocker is null) return Problem(404, "blocker_not_found", $"blocker not found: {body.By}");
                if (blocker.Id == current.Id) return Problem(400, "self_block", "an item cannot block itself");
            }

            // With a blocker the reason is that link's note, so it goes when the
            // link does; without one it is the free-text reason.
            var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
            var updated = reason is not null && blocker is null
                ? current with { BlockedReason = reason }
                : current;
            if (blocker is not null)
            {
                var blockerFile = repo.Store.FindItemFile(blocker.Id);
                var currentFile = repo.Store.FindItemFile(current.Id);
                if (blockerFile is not null && currentFile is not null)
                {
                    var path = LinkPath.Relative(Path.GetDirectoryName(currentFile)!, blockerFile);
                    if (!updated.LinkRows.Any(r => r.Rel == LinkRel.BlockedBy
                        && LinkPath.ItemIdFromTarget(r.TargetPath) == blocker.Id))
                    {
                        var rows = updated.LinkRows.Concat([new LinkRow(LinkRel.BlockedBy, blocker.Title, path, null)]).ToList();
                        updated = updated with { LinkRows = rows };
                    }
                    if (reason is not null) updated = updated.WithBlockerNote(blocker.Id, reason, out _);
                }
            }

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapPost("/api/items/{id}/unblock", (string id, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            // Resolves every impediment. Partial resolution goes through the
            // link-delete endpoint (one blocker) or a `blocked: ""` patch (the reason).
            var rows = current.LinkRows.Where(r => r.Rel != LinkRel.BlockedBy).ToList();
            var updated = current with { BlockedReason = null, LinkRows = rows };
            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapPost("/api/items/{id}/dismiss", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            DismissRequestDto? body = null;
            if (req.ContentLength > 0)
            {
                try
                {
                    body = await JsonSerializer.DeserializeAsync(
                        req.Body, ApiJsonContext.Default.DismissRequestDto, ct);
                }
                catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            }
            var cascade = body?.Cascade == true;

            var dismissed = current with { State = ItemState.Dismissed };
            repo.Store.Write(dismissed);

            var changes = new List<(Item Before, Item After)> { (current, dismissed) };
            if (cascade)
            {
                var items = repo.Store.AllItems().ToList();
                var graph = new LinkGraph(items);
                var openDescendants = graph.Subtree(current.Id)
                    .Where(i => i.Id != current.Id)
                    .Where(i => i.State != ItemState.Done && i.State != ItemState.Dismissed)
                    .ToList();
                foreach (var d in openDescendants)
                {
                    var c = d with { State = ItemState.Dismissed };
                    repo.Store.Write(c);
                    changes.Add((d, c));
                }
            }
            var dependents = CloseEffects.BlockedDependents(repo.Store, changes);
            var shortener = repo.Shortener();
            ItemDto Dto(Item written)
            {
                var freshItem = repo.Store.Read(written.Id) ?? written;
                return ItemMapping.ToDto(freshItem, shortener, options.RootPath, repo.Store.Read) with
                {
                    BlockedDependents = DependentDtos(dependents.GetValueOrDefault(freshItem.Id), shortener),
                };
            }
            var cascadedDtos = changes.Skip(1).Select(c => Dto(c.After)).ToList();

            var fresh = repo.Store.Read(dismissed.Id) ?? dismissed;
            var freshDto = Dto(dismissed);

            if (cascade)
            {
                var result = new DismissResultDto(freshDto, cascadedDtos);
                return Results.Json(result, ApiJsonContext.Default.DismissResultDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
            }
            return Results.Json(freshDto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapPost("/api/items/{id}/retype", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            RetypeRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.RetypeRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.To))
                return Problem(400, "invalid_type", "`to` is required");
            if (!EnumStrings.TryParseItemType(body.To, out var toType))
                return Problem(400, Retype.ErrInvalidType,
                    $"unknown type '{body.To}' (expected project|epic|feature|bug|task|todo)");

            // Parent: null = keep current, "" = detach to root, non-empty = resolve.
            Item? newParent;
            var parentChanging = body.Parent is not null;
            if (!parentChanging)
            {
                newParent = string.IsNullOrEmpty(current.Parent) ? null : repo.Store.Read(current.Parent!);
            }
            else if (body.Parent!.Length == 0)
            {
                newParent = null;
            }
            else
            {
                newParent = repo.Store.Read(body.Parent);
                if (newParent is null)
                    return Problem(404, "parent_not_found", $"parent not found: {body.Parent}");
            }

            var allItems = repo.Store.AllItems().ToList();
            var validation = Retype.Validate(current, toType, newParent, allItems);
            if (!validation.IsOk)
            {
                var status = validation.ErrorCode switch
                {
                    Retype.ErrInvalidType => 400,
                    Retype.ErrInvalidParent => 400,
                    Retype.ErrWouldOrphanChildren => 409,
                    Retype.ErrCycle => 409,
                    _ => 500,
                };
                var problem = new RetypeProblemDto(
                    Title: validation.ErrorCode!,
                    Status: status,
                    Detail: validation.Message,
                    Code: validation.ErrorCode,
                    OffendingChildIds: validation.OffendingChildIds);
                return Results.Json(problem, ApiJsonContext.Default.RetypeProblemDto,
                    statusCode: status, contentType: MediaTypeNames.Application.ProblemJson);
            }

            var updated = current with { Type = toType };
            if (parentChanging)
            {
                if (newParent is null)
                {
                    updated = updated with
                    {
                        Parent = null,
                        LinkRows = updated.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList(),
                    };
                }
                else
                {
                    var sourceFile = repo.Store.FindItemFile(updated.Id);
                    var targetFile = repo.Store.FindItemFile(newParent.Id);
                    if (targetFile is null)
                        return Problem(404, "parent_not_found", $"parent file not found for {newParent.Id}");
                    var sourceDir = sourceFile is not null
                        ? Path.GetDirectoryName(sourceFile)!
                        : repo.Store.ItemsDir;
                    var path = LinkPath.Relative(sourceDir, targetFile);
                    var rows = updated.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList();
                    rows.Insert(0, new LinkRow(LinkRel.Parent, newParent.Title, path, null));
                    updated = updated with { Parent = newParent.Id, LinkRows = rows };
                }
            }

            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));
        });

        app.MapPost("/api/items/{id}/reorder", async (string id, HttpRequest req, CancellationToken ct) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            ReorderRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.ReorderRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || string.IsNullOrWhiteSpace(body.Position))
                return Problem(400, "missing_position", "position is required (top|bottom|up|down)");
            var pos = body.Position.Trim().ToLowerInvariant();
            if (pos is not ("top" or "bottom" or "up" or "down"))
                return Problem(400, "invalid_position", $"unknown position '{body.Position}' (expected top|bottom|up|down)");

            // Same-parent siblings only. Top-level items (Parent == null)
            // share a single root group. Exclude the moving item itself;
            // we compute a new rank from the neighbours' ranks.
            //
            // Older ranks can leave a slot no value fits, so
            // respace the group first — the same rule `knit reorder` applies,
            // and the caller already asked to move inside it.
            var group = RankRebalancer.GroupOf(repo.Store, current.Parent);
            if (group is { Blocked: true } && RankRebalancer.Rebalance(repo.Store, group) > 0)
                current = repo.Store.Read(current.Id) ?? current;

            var siblings = repo.Store.AllItems()
                .Where(i => i.Id != current.Id && i.Parent == current.Parent && Rank.IsValid(i.Rank))
                .OrderBy(i => i.Rank, StringComparer.Ordinal)
                .ToList();

            // Where does the current item logically sit among siblings?
            // Items without a rank or with a rank > all neighbours go at
            // the end. This index points to the sibling AFTER the current
            // item (or siblings.Count if at the tail).
            int after;
            if (!Rank.IsValid(current.Rank)) after = siblings.Count;
            else
            {
                after = 0;
                while (after < siblings.Count && StringComparer.Ordinal.Compare(siblings[after].Rank, current.Rank) < 0)
                    after++;
            }
            var before = after - 1; // sibling immediately BEFORE current

            string lo, hi;
            switch (pos)
            {
                case "top":
                    if (siblings.Count == 0 || before < 0) return NoOp(current);
                    lo = "";
                    hi = siblings[0].Rank!;
                    break;
                case "bottom":
                    if (siblings.Count == 0 || after >= siblings.Count) return NoOp(current);
                    lo = siblings[^1].Rank!;
                    hi = "";
                    break;
                case "up":
                    if (before < 0) return NoOp(current);
                    lo = before - 1 >= 0 ? siblings[before - 1].Rank! : "";
                    hi = siblings[before].Rank!;
                    break;
                case "down":
                    if (after >= siblings.Count) return NoOp(current);
                    lo = siblings[after].Rank!;
                    hi = after + 1 < siblings.Count ? siblings[after + 1].Rank! : "";
                    break;
                default:
                    return Problem(400, "invalid_position", $"unknown position '{pos}'");
            }

            string newRank;
            try { newRank = Rank.Between(lo, hi); }
            catch (Exception ex) { return Problem(500, "rank_compute_failed", ex.Message); }

            var updated = current with { Rank = newRank };
            repo.Store.Write(updated);
            var fresh = repo.Store.Read(updated.Id) ?? updated;
            var dto = ItemMapping.ToDto(fresh, repo.Shortener(), options.RootPath, repo.Store.Read);
            return Results.Json(dto, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, fresh.Id));

            IResult NoOp(Item it)
            {
                var d = ItemMapping.ToDto(it, repo.Shortener(), options.RootPath, repo.Store.Read);
                return Results.Json(d, ApiJsonContext.Default.ItemDto).WithETag(Etag.OfItem(repo.Store, it.Id));
            }
        });

        // POST /api/items/reorder — batch placement. The per-id sibling nudge
        // above serves keyboard/button moves; this one serves a drag, which
        // can carry a multi-select and change the parent at the same time.
        // A batch rather than one call per row: chaining per-row placements
        // would resolve every row against the same neighbour, so a drop at
        // the tail comes out reversed.
        app.MapPost("/api/items/reorder", async (HttpRequest req, CancellationToken ct) =>
        {
            ReorderItemsRequestDto? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    req.Body, ApiJsonContext.Default.ReorderItemsRequestDto, ct);
            }
            catch (JsonException ex) { return Problem(400, "invalid_json", ex.Message); }
            if (body is null || body.Items.Count == 0)
                return Problem(400, "missing_body", "items array is required");
            if (!string.IsNullOrEmpty(body.Before) && !string.IsNullOrEmpty(body.After))
                return Problem(400, "ambiguous_neighbour",
                    "pass before or after, not both — the server reads the other side from the group");

            var repo = new RepoServices(options);
            var parentId = string.IsNullOrEmpty(body.Parent) ? null : body.Parent;

            // Read and precondition-check every row before writing anything: a
            // drag is one gesture, so a half-applied placement is worse than
            // none. Any stale etag fails the whole call; the SPA refetches and
            // retries once.
            var moving = new List<Item>(body.Items.Count);
            var movingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in body.Items)
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                    return Problem(400, "invalid_row", "id is required on every row");
                if (!movingIds.Add(row.Id))
                    return Problem(400, "duplicate_row", $"item {row.Id} appears twice");
                var it = repo.Store.Read(row.Id);
                if (it is null) return Problem(404, "item_not_found", $"item not found: {row.Id}");
                var itEtag = Etag.OfItem(repo.Store, it.Id);
                if (string.IsNullOrEmpty(row.IfMatch) || !EtagMatches(row.IfMatch, itEtag))
                    return Results.Json(
                        new ProblemDto("etag_mismatch", 412,
                            $"current etag {itEtag} does not match If-Match {row.IfMatch} for {row.Id}",
                            "etag_mismatch"),
                        ApiJsonContext.Default.ProblemDto,
                        statusCode: StatusCodes.Status412PreconditionFailed);
                moving.Add(it);
            }
            if (parentId is not null && movingIds.Contains(parentId))
                return Problem(400, "cycle", "an item cannot be its own parent");

            // Re-parent first — hierarchy and cycle checks belong to the move,
            // and the ranks must be measured against the group the items
            // actually land in.
            for (var i = 0; i < moving.Count; i++)
            {
                if (moving[i].Parent == parentId) continue;
                var (reparented, err) = Reparent(moving[i], parentId ?? "", repo);
                if (err is not null) return err;
                moving[i] = reparented!;
            }

            // Older ranks can leave a slot no value fits, and a
            // hand-written item can have no rank at all — neither leaves the
            // drop anywhere to land. Respacing the group fixes both (it
            // backfills the rankless) and is the rule `knit reorder` applies.
            var group = RankRebalancer.GroupOf(repo.Store, parentId);
            if (group is not null
                && (group.Blocked || group.Children.Any(c => !movingIds.Contains(c.Id) && !Rank.IsValid(c.Rank))))
                RankRebalancer.Rebalance(repo.Store, group);

            var siblings = repo.Store.AllItems()
                .Where(i => !movingIds.Contains(i.Id) && i.Parent == parentId && Rank.IsValid(i.Rank))
                .OrderBy(i => i.Rank, StringComparer.Ordinal)
                .ToList();

            string lo = "", hi = "";
            if (!string.IsNullOrEmpty(body.After))
            {
                var idx = siblings.FindIndex(x => x.Id == body.After);
                if (idx < 0) return Problem(400, "unknown_neighbour",
                    $"after '{body.After}' is not a sibling in the target group");
                lo = siblings[idx].Rank!;
                hi = idx + 1 < siblings.Count ? siblings[idx + 1].Rank! : "";
            }
            else if (!string.IsNullOrEmpty(body.Before))
            {
                var idx = siblings.FindIndex(x => x.Id == body.Before);
                if (idx < 0) return Problem(400, "unknown_neighbour",
                    $"before '{body.Before}' is not a sibling in the target group");
                hi = siblings[idx].Rank!;
                lo = idx > 0 ? siblings[idx - 1].Rank! : "";
            }
            else if (siblings.Count > 0)
            {
                lo = siblings[^1].Rank!;
            }

            var placed = new List<Item>(moving.Count);
            var cursor = lo;
            foreach (var it in moving)
            {
                string rank;
                try { rank = Rank.Between(cursor, hi); }
                catch (Exception ex) { return Problem(500, "rank_compute_failed", ex.Message); }
                placed.Add(it with { Rank = rank });
                cursor = rank;
            }

            foreach (var it in placed) repo.Store.Write(it);

            var placeShortener = repo.Shortener();
            var placedDtos = placed
                .Select(it => ItemMapping.ToDto(
                    repo.Store.Read(it.Id) ?? it, placeShortener, options.RootPath, repo.Store.Read))
                .ToList();
            return Results.Json(
                new ReorderItemsResultDto(placedDtos), ApiJsonContext.Default.ReorderItemsResultDto);
        });

        // POST /api/items/{id}/apply-sort?by=state[&shallow=1][&dryRun=1] — bake
        // the sort into the ranks of every sibling group below the item (or
        // only its children); /api/items/apply-sort covers the whole tree.
        // `knit reorder --by` over the same plan, so the counts agree.
        app.MapPost("/api/items/{id}/apply-sort", (string id, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var root = repo.Store.Read(id);
            if (root is null) return Problem(404, "item_not_found", $"item not found: {id}");
            return ApplySort(repo, req, root.Id, Flag(req, "shallow"));
        });

        app.MapPost("/api/items/apply-sort", (HttpRequest req) =>
            ApplySort(new RepoServices(options), req, null, false));

        // DELETE /api/items/{id}?cascade=true — hard-delete; counterpart of
        // POST /dismiss (soft-delete). Refuses when descendants exist unless
        // cascade=true. Back-references in other items are NOT auto-cleaned
        // — clients should surface a "run knit verify" hint.
        app.MapDelete("/api/items/{id}", (string id, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var current = repo.Store.Read(id);
            if (current is null) return Problem(404, "item_not_found", $"item not found: {id}");
            if (!RequireIfMatch(req, repo.Store, current, out var preconditionFailure)) return preconditionFailure!;

            var cascadeParam = req.Query["cascade"].ToString();
            var cascade = string.Equals(cascadeParam, "true", StringComparison.OrdinalIgnoreCase) || cascadeParam == "1";

            var items = repo.Store.AllItems().ToList();
            var graph = new LinkGraph(items);
            var descendants = graph.Subtree(current.Id)
                .Where(i => i.Id != current.Id)
                .ToList();

            if (descendants.Count > 0 && !cascade)
                return Problem(409, "has_children",
                    $"item has {descendants.Count} descendant(s); pass ?cascade=true to delete the whole subtree");

            var cascadedIds = new List<string>();
            if (cascade)
            {
                foreach (var d in descendants)
                {
                    if (repo.Store.Delete(d.Id)) cascadedIds.Add(d.Id);
                }
            }
            if (!repo.Store.Delete(current.Id))
                return Problem(404, "item_not_found", $"item file vanished mid-request: {current.Id}");

            return Results.Json(
                new DeleteResultDto(current.Id, cascadedIds),
                ApiJsonContext.Default.DeleteResultDto);
        });
    }

    /// <summary>
    /// Re-parent one item: validates the hierarchy and cycles, then rewrites
    /// its <c>## Links</c> parent row. <paramref name="parentId"/> empty clears
    /// the parent. Shared by PATCH and <c>POST /api/items/reorder</c> so a
    /// re-parent means the same thing however it was asked for.
    /// </summary>
    private static (Item? updated, IResult? error) Reparent(
        Item current, string parentId, RepoServices repo)
    {
        var updated = current;
        if (parentId.Length == 0)
        {
            updated = updated with
            {
                Parent = null,
                LinkRows = updated.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList(),
            };
        }
        else
        {
            var newParent = repo.Store.Read(parentId);
            if (newParent is null)
                return (null, Problem(404, "parent_not_found", $"parent not found: {parentId}"));
            if (string.Equals(newParent.Id, updated.Id, StringComparison.Ordinal))
                return (null, Problem(400, "cycle", "an item cannot be its own parent"));
            if (!Hierarchy.ParentAllowed(updated.Type, newParent.Type))
                return (null, Problem(400, "invalid_parent",
                    Hierarchy.ExplainRefusal(updated.Type, newParent.Type)));

            var allItems = repo.Store.AllItems().ToList();
            var graph = new LinkGraph(allItems);
            if (graph.Subtree(updated.Id).Any(d => d.Id == newParent.Id && d.Id != updated.Id))
                return (null, Problem(400, "cycle",
                    $"new parent {newParent.Id} is inside the subtree of {updated.Id}"));

            var sourceFile = repo.Store.FindItemFile(updated.Id);
            var targetFile = repo.Store.FindItemFile(newParent.Id);
            if (targetFile is null)
                return (null, Problem(404, "parent_not_found", $"parent file not found for {newParent.Id}"));
            var sourceDir = sourceFile is not null
                ? Path.GetDirectoryName(sourceFile)!
                : repo.Store.ItemsDir;
            var path = LinkPath.Relative(sourceDir, targetFile);
            var rows = updated.LinkRows.Where(r => r.Rel != LinkRel.Parent).ToList();
            rows.Insert(0, new LinkRow(LinkRel.Parent, newParent.Title, path, null));
            updated = updated with { Parent = newParent.Id, LinkRows = rows };
        }
        return (updated, null);
    }

    private static IReadOnlyList<BlockedDependentDto>? DependentDtos(
        IReadOnlyList<BlockedDependent>? dependents, IdShortener shortener) =>
        dependents?.Select(d => new BlockedDependentDto(
            d.Item.Id, shortener.Short(d.Item.Id), d.Item.Title, d.Remaining)).ToList();

    /// <summary>
    /// Apply <paramref name="body"/> against <paramref name="current"/>.
    /// Returns the updated item on success, or a Problem-Details
    /// <see cref="IResult"/> on validation failure.
    /// </summary>
    private static (Item? updated, IResult? error) ApplyPatch(
        Item current, ItemPatchRequestDto body, RepoServices repo)
    {
        var updated = current;
        if (body.State is not null)
        {
            if (string.Equals(body.State.Trim(), "blocked", StringComparison.OrdinalIgnoreCase))
                return (null, Problem(400, "invalid_state",
                    "'blocked' is no longer a valid state; use POST /api/items/{id}/block to set the orthogonal `blocked` flag"));
            if (!EnumStrings.TryParseItemState(body.State, out var st))
                return (null, Problem(400, "invalid_state", $"unknown state '{body.State}'"));
            updated = updated with { State = st };
        }
        if (body.Blocked is not null)
            updated = updated with { BlockedReason = body.Blocked.Length == 0 ? null : body.Blocked };
        if (body.Assignee is not null)
            updated = updated with { Assignee = body.Assignee.Length == 0 ? null : body.Assignee };
        if (body.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Title))
                return (null, Problem(400, "invalid_title", "title cannot be empty"));
            updated = updated with { Title = body.Title };
        }
        if (body.Effort is not null)
            updated = updated with { Effort = body.Effort.Length == 0 ? null : body.Effort };
        if (body.Due is not null)
        {
            if (body.Due.Length == 0) updated = updated with { Due = null };
            else if (DueDates.TryParse(body.Due, out var due)) updated = updated with { Due = due };
            else return (null, Problem(400, "invalid_due", $"invalid date '{body.Due}' — expected YYYY-MM-DD"));
        }
        if (body.Priority is not null)
        {
            // "" or "normal" resets; unknown is a 400 (lenient read degrades,
            // but explicit input is validated).
            if (Priorities.TryParse(body.Priority, out var prio)) updated = updated with { Priority = prio };
            else return (null, Problem(400, "invalid_priority", $"unknown priority '{body.Priority}' — expected someday|normal|important|critical"));
        }
        if (body.Parent is not null)
        {
            var (reparented, parentErr) = Reparent(updated, body.Parent, repo);
            if (parentErr is not null) return (null, parentErr);
            updated = reparented!;
        }
        if (body.AgentSkills is not null)
            updated = updated with { AgentSkills = body.AgentSkills };
        // Route through the KB Tags chokepoint so item + KB tag hygiene can't
        // drift (same normalization the CLI `set tags=` uses).
        if (body.Tags is not null)
            updated = updated with { Tags = Knit.Core.Kb.Tags.Normalize(body.Tags) };
        if (body.Slate is not null)
            updated = updated with { Slate = body.Slate.Length == 0 ? null : Knit.Core.Ids.Slug.From(body.Slate) };
        if (body.Health is not null)
        {
            // Project-only signal; unknown value or non-project item is a 400
            // (mirrors the CLI `set health=`; on-disk read stays lenient).
            if (!Healths.TryParse(body.Health, out var health))
                return (null, Problem(400, "invalid_health",
                    $"unknown health '{body.Health}' — expected on-track|at-risk|off-track"));
            if (health is not null && updated.Type != ItemType.Project)
                return (null, Problem(400, "invalid_health",
                    $"health is a project-only field; {updated.Id} is a {updated.Type.ToWire()}"));
            updated = updated with { Health = health };
        }
        if (body.Body is not null)
            updated = updated with { Body = body.Body };

        // Two-way sync: the leading body H1 and the frontmatter title are
        // one value. An explicit title wins and is mirrored into the H1;
        // otherwise a body edit's leading H1 becomes the title.
        if (body.Title is not null)
        {
            updated = updated with { Body = Knit.Core.Frontmatter.LeadingH1.SetOrInsert(updated.Body, updated.Title) };
        }
        else if (body.Body is not null)
        {
            var h1 = Knit.Core.Frontmatter.LeadingH1.Extract(updated.Body);
            if (h1 is not null) updated = updated with { Title = h1 };
        }

        return (updated, null);
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

    private static IResult ApplySort(RepoServices repo, HttpRequest req, string? root, bool shallow)
    {
        var by = req.Query["by"].ToString();
        if (!string.Equals(by, "state", StringComparison.OrdinalIgnoreCase))
            return Problem(400, "invalid_sort", $"unknown sort '{by}' (expected state)");
        var dryRun = Flag(req, "dryRun");
        var plan = RankSortApplier.Plan(repo.Store, root, shallow, SiblingOrder.Comparer(TreeSort.State));
        if (!dryRun) RankSortApplier.Write(repo.Store, plan);
        return Results.Json(
            new ApplySortResultDto(dryRun, plan.Moved, plan.Total, plan.GroupsMoved, plan.Moves.Select(m => m.Item.Id).ToList()),
            ApiJsonContext.Default.ApplySortResultDto);
    }

    private static bool Flag(HttpRequest req, string name)
    {
        var v = req.Query[name].ToString();
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
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

    private static string ComputeNextRank(ItemStore store)
    {
        var maxRank = store.AllItems()
            .Select(i => i.Rank)
            .Where(r => Rank.IsValid(r))
            .DefaultIfEmpty()
            .Max(StringComparer.Ordinal);
        return string.IsNullOrEmpty(maxRank) ? Rank.Initial() : Rank.Between(maxRank, "");
    }
}

internal static class ResultExtensions
{
    public static IResult WithETag(this IResult inner, string etag)
        => new HeaderResult(inner, (HeaderNames.ETag, etag));

    public static IResult WithLocation(this IResult inner, string location)
        => new HeaderResult(inner, (HeaderNames.Location, location));

    private sealed class HeaderResult(IResult inner, (string Name, string Value) header) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers[header.Name] = header.Value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
