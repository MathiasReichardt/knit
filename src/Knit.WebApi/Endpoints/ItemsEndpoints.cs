using System.Net.Mime;
using System.Text.Json;
using Knit.Core;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Data;
using Knit.WebApi.Contracts;
using Knit.WebApi.Mapping;
using Knit.WebApi.Queries;
using Knit.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Knit.WebApi.Endpoints;

public static class ItemsEndpoints
{
    public static void MapItems(this IEndpointRouteBuilder app, WebApiOptions options)
    {
        app.MapGet("/api/items", (HttpRequest req) =>
        {
            var (q, parseErr) = ItemQueryParser.Parse(req.Query);
            if (q is null) return Results.BadRequest(parseErr);

            var repo = new RepoServices(options);
            // Only the hierarchy order reads tree_sort — a bad value must not break other sorts.
            if (q.SortKey == SortKey.Tree && ItemQueryRunner.ResolveTreeSort(repo, q) is { } treeSortErr)
                return Results.BadRequest(treeSortErr);
            var graph = repo.Graph();
            var result = ItemQueryRunner.Run(repo, graph, q);
            if (result.Error is not null)
                return result.IsNotFound ? Results.NotFound(result.Error) : Results.BadRequest(result.Error);

            var shortener = repo.Shortener();
            IReadOnlyList<Item> items = result.Items;
            // For list mode, default sort is rank — already applied by runner.
            // If the caller hasn't filtered at all, fall back to the legacy
            // "all items, created asc" default so 1c.0 behaviour is preserved.
            if (!q.HasAnyMatchFilter && string.IsNullOrEmpty(q.Under) && req.Query.Count == 0)
            {
                items = graph.Items.OrderBy(i => i.Created).ToList();
            }

            var pathMap = ItemMapping.BuildPathMap(options.RootPath);
            var list = items.Select(i => ItemMapping.ToSummary(i, shortener, graph, pathMap)).ToList();
            return Results.Json((IReadOnlyList<ItemSummaryDto>)list, ApiJsonContext.Default.IReadOnlyListItemSummaryDto);
        });

        app.MapGet("/api/items/landing", (HttpRequest req) =>
        {
            // Items row for the landing page: in-progress leaves first
            // (mtime desc), then the most-recently-modified other items backfill
            // the remaining slots. `mine=1` scopes to the caller's assignee.
            var offset = Paging.Int(req.Query, "offset", 0);
            var limit = Paging.Int(req.Query, "limit", 5);
            var mine = Paging.Flag(req.Query, "mine");

            var repo = new RepoServices(options);
            var graph = repo.Graph();
            var pathMap = ItemMapping.BuildPathMap(options.RootPath);

            string? handle = null;
            if (mine)
            {
                var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath));
                handle = cfg.Handle;
            }

            DateTime Mtime(string id)
            {
                if (pathMap.TryGetValue(id, out var p))
                {
                    try { return File.GetLastWriteTimeUtc(p); } catch { /* fall through */ }
                }
                return DateTime.MinValue;
            }

            // "In-progress leaf" = in-progress with no in-progress descendant —
            // one card per independent active chain.
            bool IsActiveLeaf(Item i) =>
                i.State == ItemState.InProgress
                && !graph.Subtree(i.Id).Any(d => d.Id != i.Id && d.State == ItemState.InProgress);

            IEnumerable<Item> population = graph.Items;
            if (handle is not null)
                population = population.Where(i => string.Equals(i.Assignee, handle, StringComparison.Ordinal));
            var pop = population.ToList();

            var leaves = pop.Where(IsActiveLeaf).OrderByDescending(i => Mtime(i.Id)).ToList();
            var leafIds = new HashSet<string>(leaves.Select(i => i.Id), StringComparer.Ordinal);
            var rest = pop.Where(i => !leafIds.Contains(i.Id)).OrderByDescending(i => Mtime(i.Id)).ToList();
            var ordered = leaves.Concat(rest).ToList();

            var shortener = repo.Shortener();
            var page = Paging.Page(ordered, offset, limit, req.HttpContext.Response);
            var list = page
                .Select(i => ItemMapping.ToSummary(
                    i, shortener, graph, pathMap,
                    modified: pathMap.TryGetValue(i.Id, out var p) ? FileTime.ModifiedIso(p) : ""))
                .ToList();
            return Results.Json((IReadOnlyList<ItemSummaryDto>)list, ApiJsonContext.Default.IReadOnlyListItemSummaryDto);
        });

        app.MapGet("/api/items/{id}", (string id, HttpRequest req) =>
        {
            var repo = new RepoServices(options);
            var item = repo.Store.Read(id);
            if (item is null) return Results.NotFound();
            var etag = Etag.OfItem(repo.Store, item.Id);
            var ifNoneMatch = req.Headers[HeaderNames.IfNoneMatch].ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            var dto = ItemMapping.ToDto(item, repo.Shortener(), options.RootPath, repo.Store.Read);
            var http = req.HttpContext;
            http.Response.Headers.ETag = etag;
            return Results.Json(dto, ApiJsonContext.Default.ItemDto);
        });

        app.MapGet("/api/items/{id}/context", (string id) =>
        {
            var repo = new RepoServices(options);
            var item = repo.Store.Read(id);
            if (item is null) return Results.NotFound();
            var graph = repo.Graph();
            var resolved = ItemContextResolver.Resolve(graph, item);
            var pathMap = ItemMapping.BuildPathMap(options.RootPath);
            var dto = ItemMapping.ToContextDto(resolved, graph, repo.Shortener(), pathMap);
            return Results.Json(dto, ApiJsonContext.Default.ContextDto);
        });

        app.MapGet("/api/tree", (HttpRequest req) =>
        {
            var (q, parseErr) = ItemQueryParser.Parse(req.Query);
            if (q is null) return Results.BadRequest(parseErr);

            var repo = new RepoServices(options);
            if (ItemQueryRunner.ResolveTreeSort(repo, q) is { } treeSortErr)
                return Results.BadRequest(treeSortErr);
            var siblings = SiblingOrder.Comparer(q.TreeSort);
            var graph = repo.Graph();
            var shortener = repo.Shortener();

            var pathMap = ItemMapping.BuildPathMap(options.RootPath);
            IReadOnlyList<TreeNodeDto> tree;
            if (!q.HasAnyMatchFilter && string.IsNullOrEmpty(q.Under))
            {
                tree = ItemMapping.ToTree(graph, shortener, siblings, pathMap);
            }
            else
            {
                var result = ItemQueryRunner.Run(repo, graph, q);
                if (result.Error is not null)
                    return result.IsNotFound ? Results.NotFound(result.Error) : Results.BadRequest(result.Error);

                Item? rootItem = null;
                if (!string.IsNullOrEmpty(q.Under))
                    rootItem = ItemQueryRunner.TryResolveById(graph, q.Under!);

                tree = ItemMapping.ToFilteredTree(
                    graph, shortener, result.MatchIds,
                    flat: q.FilterMode == TreeFilterMode.Flat,
                    rootItem: rootItem,
                    siblings: siblings,
                    pathMap: pathMap);
            }

            return Results.Json(tree, ApiJsonContext.Default.IReadOnlyListTreeNodeDto);
        });

        app.MapGet("/api/ready", (HttpRequest req) =>
        {
            var (q, parseErr) = ItemQueryParser.Parse(req.Query);
            if (q is null) return Results.BadRequest(parseErr);

            var repo = new RepoServices(options);
            var graph = repo.Graph();
            var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath));

            // Ready(null) means everyone's; "mine" with no handle is nobody's.
            if (q.Mine && cfg.Handle is null)
                return Results.Json((IReadOnlyList<ItemSummaryDto>)[], ApiJsonContext.Default.IReadOnlyListItemSummaryDto);

            // Every parsed filter applies — under, slate, type, … — the query
            // runner's matches intersected with the ready set.
            var result = ItemQueryRunner.Run(repo, graph, q);
            if (result.Error is not null)
                return result.IsNotFound ? Results.NotFound(result.Error) : Results.BadRequest(result.Error);
            var ready = graph.Ready(q.Mine ? cfg.Handle : null)
                .Where(i => result.MatchIds.Contains(i.Id))
                .OrderBy(i => i.Rank, StringComparer.Ordinal)
                .ToList();

            var shortener = repo.Shortener();
            var pathMap = ItemMapping.BuildPathMap(options.RootPath);
            var list = ready.Select(i => ItemMapping.ToSummary(i, shortener, graph, pathMap)).ToList();
            return Results.Json((IReadOnlyList<ItemSummaryDto>)list, ApiJsonContext.Default.IReadOnlyListItemSummaryDto);
        });

        app.MapGet("/api/identity", () =>
        {
            var cfg = ConfigLoader.Load(RepoDiscovery.KnitDir(options.RootPath));
            var dto = new IdentityDto(cfg.Handle, cfg.Display);
            return Results.Json(dto, ApiJsonContext.Default.IdentityDto);
        });

        app.MapGet("/api/health", () =>
        {
            var repo = new RepoServices(options);
            var cfg = ConfigLoader.Load(repo.KnitDir);
            var itemCount = repo.Store.AllItemFiles().Count();
            // Repo-wide (never filtered) open-item due counts — the timeline
            // nav-rail attention dot needs a reliable global "something's due"
            // signal regardless of the current view's filters.
            var today = DateOnly.FromDateTime(DateTime.Now);
            int overdue = 0, dueSoon = 0;
            foreach (var i in repo.Store.AllItems())
            {
                switch (DueDates.Status(i.Due, i.State, today, cfg.DueSoonDays))
                {
                    case DueStatus.Overdue: overdue++; break;
                    case DueStatus.DueSoon: dueSoon++; break;
                }
            }
            var dto = new HealthDto(
                RootPath: Path.GetFullPath(options.RootPath),
                Identity: new IdentityDto(cfg.Handle, cfg.Display),
                ItemCount: itemCount,
                Version: VersionInfo.Get(typeof(WebApiBuilder).Assembly),
                DueSoonDays: cfg.DueSoonDays,
                OverdueCount: overdue,
                DueSoonCount: dueSoon);
            return Results.Json(dto, ApiJsonContext.Default.HealthDto);
        });
    }

    public static void MapEvents(this IEndpointRouteBuilder app, FileWatcher? watcher, ActivityBuffer? activity)
    {
        app.MapGet("/api/events", async (HttpContext ctx, IHostApplicationLifetime lifetime, CancellationToken requestAborted) =>
        {
            // Link the request-abort token to the host's ApplicationStopping so a
            // Ctrl+C drains this stream promptly. Without it, the pumps below only
            // observe client disconnect, so graceful shutdown blocks on the open SSE
            // request until the shutdown timeout force-aborts the connection.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                requestAborted, lifetime.ApplicationStopping);
            var ct = linked.Token;

            ctx.Response.Headers.ContentType = MediaTypeNames.Text.EventStream;
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            // One writer at a time so SSE frames never interleave across the
            // resource / activity / heartbeat pumps sharing this response.
            using var writeGate = new SemaphoreSlim(1, 1);
            async Task Write(string frame)
            {
                await writeGate.WaitAsync(ct);
                try
                {
                    await ctx.Response.WriteAsync(frame, ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                finally { writeGate.Release(); }
            }

            if (watcher is null)
            {
                // --no-watch: keep the stream open but emit nothing actionable.
                await Write(": file-watcher disabled\n\n");
                await HeartbeatLoop(Write, ct);
                return;
            }

            using var sub = watcher.Subscribe(out var reader);

            IDisposable? actSub = null;
            System.Threading.Channels.ChannelReader<ActivityEntryDto>? actReader = null;
            if (activity is not null)
            {
                actSub = activity.Subscribe(out var ar);
                actReader = ar;
            }

            await Write(": connected\n\n");

            var pumps = new List<Task> { HeartbeatLoop(Write, ct), EventPump(reader, Write, ct) };
            if (actReader is not null) pumps.Add(ActivityPump(actReader, Write, ct));
            try { await Task.WhenAll(pumps); }
            catch (OperationCanceledException) { }
            finally { actSub?.Dispose(); }
        });
    }

    public static void MapActivity(this IEndpointRouteBuilder app, ActivityBuffer? activity)
    {
        app.MapGet("/api/activity", () =>
            Results.Json(
                activity is null ? Array.Empty<ActivityEntryDto>() : activity.Snapshot(),
                ApiJsonContext.Default.IReadOnlyListActivityEntryDto));
    }

    private static async Task EventPump(
        System.Threading.Channels.ChannelReader<EventDto> reader, Func<string, Task> write, CancellationToken ct)
    {
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(evt, ApiJsonContext.Default.EventDto);
            await write($"event: {evt.Op}\ndata: {json}\n\n");
        }
    }

    private static async Task ActivityPump(
        System.Threading.Channels.ChannelReader<ActivityEntryDto> reader, Func<string, Task> write, CancellationToken ct)
    {
        await foreach (var entry in reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(entry, ApiJsonContext.Default.ActivityEntryDto);
            await write($"event: activity\ndata: {json}\n\n");
        }
    }

    private static async Task HeartbeatLoop(Func<string, Task> write, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch (OperationCanceledException) { return; }
            try { await write(": ping\n\n"); } catch { return; }
        }
    }
}
