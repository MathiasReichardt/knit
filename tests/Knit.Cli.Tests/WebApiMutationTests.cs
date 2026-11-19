using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiMutationTests
{
    [Fact]
    public async Task Post_items_creates_item_and_returns_dto_with_etag()
    {
        var root = CreateRepo(out var _);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var body = new NewItemRequestDto("task", "First task", null, null);
            var resp = await http.PostAsJsonAsync("/api/items", body, JsonOpts);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            Assert.NotNull(resp.Headers.ETag);
            var created = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.NotNull(created);
            Assert.Equal("First task", created!.Title);
            Assert.Equal("task", created.Type);
            Assert.Equal("todo", created.State);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Post_items_rejects_invalid_hierarchy()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var body = new NewItemRequestDto("epic", "Bad child", taskId, null);
            var resp = await http.PostAsJsonAsync("/api/items", body, JsonOpts);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_state_updates_and_returns_new_etag()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;

            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(State: "in-progress", Assignee: "alice")),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var updated = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal("in-progress", updated!.State);
            Assert.Equal("alice", updated.Assignee);
            Assert.NotNull(resp.Headers.ETag);
            Assert.NotEqual(etag, resp.Headers.ETag!.Tag);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_body_mirrors_leading_h1_into_title()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;

            // Body-only edit whose leading H1 changed → title follows.
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(Body: "# Renamed via H1\n\nBody.\n")),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var updated = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal("Renamed via H1", updated!.Title);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_title_mirrors_into_leading_h1()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;
            var itemsDir = Path.Combine(root, ".knit", "items");
            var filesBefore = Directory.GetFiles(itemsDir);

            // Title-only edit (e.g. the header) → leading body H1 follows.
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(Title: "Renamed via title")),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var updated = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal("Renamed via title", updated!.Title);
            Assert.StartsWith("# Renamed via title", updated.Body);
            Assert.Equal(filesBefore, Directory.GetFiles(itemsDir)); // file name fixed at creation
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_with_stale_etag_returns_412()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(State: "done")),
            };
            req.Headers.TryAddWithoutValidation("If-Match", "\"deadbeef\"");
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_without_if_match_is_rejected()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(State: "done")),
            };
            var resp = await http.SendAsync(req);
            Assert.Equal((HttpStatusCode)428, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Patch_state_blocked_is_rejected()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}")
            {
                Content = JsonBody(new ItemPatchRequestDto(State: "blocked")),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("orthogonal", body, StringComparison.OrdinalIgnoreCase);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Post_block_sets_flag_and_unblock_clears_it()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;

            var blockReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/block")
            {
                Content = JsonBody(new BlockRequestDto(null, "vendor delay")),
            };
            blockReq.Headers.TryAddWithoutValidation("If-Match", etag);
            var blockResp = await http.SendAsync(blockReq);
            Assert.Equal(HttpStatusCode.OK, blockResp.StatusCode);
            var blocked = await blockResp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal("vendor delay", blocked!.BlockedReason);
            Assert.Equal("todo", blocked.State); // state untouched

            var newEtag = blockResp.Headers.ETag!.Tag;
            var unblockReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/unblock");
            unblockReq.Headers.TryAddWithoutValidation("If-Match", newEtag);
            var unblockResp = await http.SendAsync(unblockReq);
            Assert.Equal(HttpStatusCode.OK, unblockResp.StatusCode);
            var cleared = await unblockResp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Null(cleared!.BlockedReason);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Post_blocked_by_link_blocks_without_a_reason()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var created = await http.PostAsJsonAsync(
                "/api/items", new NewItemRequestDto("task", "the blocker", null, null), JsonOpts);
            var blocker = await created.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);

            // The row is the impediment — no reason text is written for it.
            var get = await http.GetAsync($"/api/items/{taskId}");
            var link = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/links")
            {
                Content = JsonBody(new LinkRequestDto("blocked-by", blocker!.Id, null)),
            };
            link.Headers.TryAddWithoutValidation("If-Match", get.Headers.ETag!.Tag);
            var resp = await http.SendAsync(link);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var updated = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.True(updated!.Blocked);
            Assert.Null(updated.BlockedReason);

            // A second blocker adds a row, still no reason.
            var created2 = await http.PostAsJsonAsync(
                "/api/items", new NewItemRequestDto("task", "another blocker", null, null), JsonOpts);
            var blocker2 = await created2.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            var link2 = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/links")
            {
                Content = JsonBody(new LinkRequestDto("blocked-by", blocker2!.Id, null)),
            };
            link2.Headers.TryAddWithoutValidation("If-Match", resp.Headers.ETag!.Tag);
            var resp2 = await http.SendAsync(link2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            var updated2 = await resp2.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal(2, updated2!.Links.Count(l => l.Rel == "Blocked by"));
            Assert.Null(updated2.BlockedReason);

            // A reason given with a blocker is that link's note, not the free-text reason.
            var block = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/block")
            {
                Content = JsonBody(new BlockRequestDto(blocker.Id, "needs its API")),
            };
            block.Headers.TryAddWithoutValidation("If-Match", resp2.Headers.ETag!.Tag);
            var resp3 = await http.SendAsync(block);
            Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
            var updated3 = await resp3.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Null(updated3!.BlockedReason);
            Assert.Equal("needs its API", updated3.Links.Single(l => l.TargetItemId == blocker.Id).Notes);

            // Editing the row: a new note, then a move to Related — which unblocks it.
            var row = updated3.Links.Single(l => l.TargetItemId == blocker.Id);
            var edit = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}/links")
            {
                Content = JsonBody(new LinkEditRequestDto(row.Rel, row.TargetPath, row.TargetText, "needs v2", "related")),
            };
            edit.Headers.TryAddWithoutValidation("If-Match", resp3.Headers.ETag!.Tag);
            var resp4 = await http.SendAsync(edit);
            Assert.Equal(HttpStatusCode.OK, resp4.StatusCode);
            var edited = (await resp4.Content.ReadFromJsonAsync<ItemDto>(JsonOpts))!.Links.Single(l => l.TargetItemId == blocker.Id);
            Assert.Equal(("Related", "needs v2"), (edited.Rel, edited.Notes));

            // Only blocks / blocked-by / related change into each other.
            var toParent = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{taskId}/links")
            {
                Content = JsonBody(new LinkEditRequestDto(edited.Rel, edited.TargetPath, edited.TargetText, null, "parent")),
            };
            toParent.Headers.TryAddWithoutValidation("If-Match", resp4.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(toParent)).StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Post_block_requires_by_or_reason()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/block")
            {
                Content = JsonBody(new BlockRequestDto(null, null)),
            };
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Post_dismiss_sets_dismissed_state()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/dismiss");
            req.Headers.TryAddWithoutValidation("If-Match", etag);
            var resp = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var dto = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            Assert.Equal("dismissed", dto!.State);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Items_query_blocked_only_filters_by_flag()
    {
        var root = CreateRepo(out var taskId);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var get = await http.GetAsync($"/api/items/{taskId}");
            var etag = get.Headers.ETag!.Tag;

            var blockReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{taskId}/block")
            {
                Content = JsonBody(new BlockRequestDto(null, "stuck")),
            };
            blockReq.Headers.TryAddWithoutValidation("If-Match", etag);
            await http.SendAsync(blockReq);

            var only = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/items?blocked=only", JsonOpts);
            Assert.Single(only!);
            Assert.True(only![0].Blocked);

            var hide = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/items?blocked=hide", JsonOpts);
            Assert.Empty(hide!);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Items_query_state_blocked_returns_400()
    {
        var root = CreateRepo(out var _);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var resp = await http.GetAsync("/api/items?state=blocked");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Design_get_emits_etag_and_patch_enforces_if_match()
    {
        var root = CreateRepo(out var _);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var create = await http.PostAsync("/api/designs",
                JsonBody(new { slug = "my-design", title = "My Design" }));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            Assert.NotNull(create.Headers.ETag);

            var get = await http.GetAsync("/api/designs/my-design");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var etag = get.Headers.ETag!.Tag;
            Assert.False(string.IsNullOrEmpty(etag));

            // Missing If-Match → 428.
            var noMatch = new HttpRequestMessage(HttpMethod.Patch, "/api/designs/my-design")
            { Content = JsonBody(new { body = "# My Design\n\nEdited.\n" }) };
            Assert.Equal((HttpStatusCode)428, (await http.SendAsync(noMatch)).StatusCode);

            // Stale If-Match → 412.
            var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/designs/my-design")
            { Content = JsonBody(new { body = "# My Design\n\nEdited.\n" }) };
            stale.Headers.TryAddWithoutValidation("If-Match", "\"deadbeef\"");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.SendAsync(stale)).StatusCode);

            // Correct If-Match → 200 + a fresh, different etag.
            var ok = new HttpRequestMessage(HttpMethod.Patch, "/api/designs/my-design")
            { Content = JsonBody(new { body = "# My Design\n\nEdited.\n" }) };
            ok.Headers.TryAddWithoutValidation("If-Match", etag);
            var okResp = await http.SendAsync(ok);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
            Assert.NotNull(okResp.Headers.ETag);
            Assert.NotEqual(etag, okResp.Headers.ETag!.Tag);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Kb_entry_get_emits_etag_and_patch_enforces_if_match()
    {
        var root = CreateRepo(out var _);
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;

            var create = await http.PostAsync("/api/kb/entry",
                JsonBody(new { bucket = "doc", slug = "test-doc", title = "Test Doc" }));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            Assert.NotNull(create.Headers.ETag);

            var get = await http.GetAsync("/api/kb/entry/doc/test-doc");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var etag = get.Headers.ETag!.Tag;
            Assert.False(string.IsNullOrEmpty(etag));

            // Missing If-Match → 428.
            var noMatch = new HttpRequestMessage(HttpMethod.Patch, "/api/kb/entry/doc/test-doc")
            { Content = JsonBody(new { body = "# Test Doc\n\nEdited.\n" }) };
            Assert.Equal((HttpStatusCode)428, (await http.SendAsync(noMatch)).StatusCode);

            // Stale If-Match → 412.
            var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/kb/entry/doc/test-doc")
            { Content = JsonBody(new { body = "# Test Doc\n\nEdited.\n" }) };
            stale.Headers.TryAddWithoutValidation("If-Match", "\"deadbeef\"");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.SendAsync(stale)).StatusCode);

            // Correct If-Match → 200 + a fresh, different etag.
            var ok = new HttpRequestMessage(HttpMethod.Patch, "/api/kb/entry/doc/test-doc")
            { Content = JsonBody(new { body = "# Test Doc\n\nEdited.\n" }) };
            ok.Headers.TryAddWithoutValidation("If-Match", etag);
            var okResp = await http.SendAsync(ok);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
            Assert.NotNull(okResp.Headers.ETag);
            Assert.NotEqual(etag, okResp.Headers.ETag!.Tag);
        }
        finally { TryDelete(root); }
    }

    private static StringContent JsonBody<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string CreateRepo(out string itemId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-mut-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        itemId = "01968000-0000-7000-8000-0000000000bb";
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = itemId,
            Title = "Sample",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Body = "# Sample\n",
        });
        return root;
    }

    private static async Task<HostCtx> StartAsync(string root)
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = root,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
        });
        await app.StartAsync();
        return new HostCtx(app, TestApi.Client(port));
    }

    private sealed class HostCtx(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
