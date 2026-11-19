using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// <c>POST /api/items/reorder</c>. The SPA names the target parent
/// and one neighbour row; every rank is computed here, so these specs are the
/// contract a dropped drag relies on.
/// </summary>
public class WebApiPlacementTests
{
    [Fact]
    public async Task Places_a_row_after_the_named_sibling()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            var placed = await Place(http, [ids["c"]], parent: ids["p"], after: ids["a"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);

            Assert.Equal(new[] { "a", "c", "b" }, await OrderAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Places_a_row_before_the_first_sibling()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            var placed = await Place(http, [ids["c"]], parent: ids["p"], before: ids["a"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);

            Assert.Equal(new[] { "c", "a", "b" }, await OrderAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Places_a_multi_select_in_the_order_given()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            // c then b, dropped right after a — the request order is the
            // resulting order, which is what a multi-row drag needs.
            var placed = await Place(http, [ids["c"], ids["b"]], parent: ids["p"], after: ids["a"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);

            Assert.Equal(new[] { "a", "c", "b" }, await OrderAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Appends_when_no_neighbour_is_named()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            var placed = await Place(http, [ids["a"]], parent: ids["p"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);

            Assert.Equal(new[] { "b", "c", "a" }, await OrderAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Reparents_and_ranks_in_one_call()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            // "loose" starts top-level; the drop puts it under p, right after a.
            var placed = await Place(http, [ids["loose"]], parent: ids["p"], after: ids["a"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);

            var moved = await GetAsync(http, ids["loose"]);
            Assert.Equal(ids["p"], moved.Parent);
            Assert.Equal(
                new[] { "a", "loose", "b", "c" },
                await OrderAsync(http, ids, "a", "b", "c", "loose"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Refuses_a_parent_the_hierarchy_disallows()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            // p is a feature; an epic cannot live under it.
            var placed = await Place(http, [ids["epic"]], parent: ids["p"]);
            Assert.Equal(HttpStatusCode.BadRequest, placed.Status);
            Assert.Null((await GetAsync(http, ids["epic"])).Parent);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task A_stale_etag_fails_the_whole_call_and_writes_nothing()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            var ranksBefore = await RanksAsync(http, ids, "a", "b", "c");
            var body = new
            {
                items = new[]
                {
                    new { id = ids["c"], ifMatch = "\"stale\"" },
                    new { id = ids["b"], ifMatch = await EtagAsync(http, ids["b"]) },
                },
                parent = ids["p"],
                after = ids["a"],
            };
            var resp = await http.PostAsync("/api/items/reorder", JsonBody(body));
            Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
            Assert.Equal(ranksBefore, await RanksAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Respaces_a_group_whose_ranks_leave_no_room()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            // Legacy ranks: nothing fits between "n" and "na", so a drop in
            // that gap has nowhere to land until the group is respaced.
            Poke(root, ids["a"], "n");
            Poke(root, ids["b"], "na");
            Poke(root, ids["c"], "t");

            var placed = await Place(http, [ids["c"]], parent: ids["p"], after: ids["a"]);
            Assert.Equal(HttpStatusCode.OK, placed.Status);
            Assert.Equal(new[] { "a", "c", "b" }, await OrderAsync(http, ids, "a", "b", "c"));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Rejects_naming_both_neighbours()
    {
        var root = CreateRepo();
        try
        {
            await using var ctx = await StartAsync(root);
            using var http = ctx.Client;
            var ids = await SeedAsync(http);

            var body = new
            {
                items = new[] { new { id = ids["c"], ifMatch = await EtagAsync(http, ids["c"]) } },
                parent = ids["p"],
                before = ids["b"],
                after = ids["a"],
            };
            var resp = await http.PostAsync("/api/items/reorder", JsonBody(body));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { TryDelete(root); }
    }

    // --- helpers ---

    private static async Task<(HttpStatusCode Status, string Payload)> Place(
        HttpClient http, string[] itemIds, string? parent, string? before = null, string? after = null)
    {
        var rows = new List<object>();
        foreach (var id in itemIds)
            rows.Add(new { id, ifMatch = await EtagAsync(http, id) });
        var body = new { items = rows, parent, before, after };
        var resp = await http.PostAsync("/api/items/reorder", JsonBody(body));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<string> EtagAsync(HttpClient http, string id)
    {
        var get = await http.GetAsync($"/api/items/{id}");
        return get.Headers.ETag!.Tag;
    }

    private static async Task<ItemDto> GetAsync(HttpClient http, string id)
    {
        var get = await http.GetAsync($"/api/items/{id}");
        return (await get.Content.ReadFromJsonAsync<ItemDto>(JsonOpts))!;
    }

    /// <summary>The given keys, sorted by the rank the server gave them.</summary>
    private static async Task<string[]> OrderAsync(
        HttpClient http, Dictionary<string, string> ids, params string[] keys)
    {
        var ranks = await RanksAsync(http, ids, keys);
        return keys.Zip(ranks)
            .OrderBy(x => x.Second, StringComparer.Ordinal)
            .Select(x => x.First)
            .ToArray();
    }

    private static async Task<string[]> RanksAsync(
        HttpClient http, Dictionary<string, string> ids, params string[] keys)
    {
        var ranks = new List<string>();
        foreach (var k in keys) ranks.Add((await GetAsync(http, ids[k])).Rank ?? "");
        return ranks.ToArray();
    }

    private static void Poke(string root, string id, string rank)
    {
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        var item = store.Read(id)!;
        store.Write(item with { Rank = rank });
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-place-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
        return root;
    }

    /// <summary>
    /// A feature "p" with three children a &lt; b &lt; c, plus a top-level
    /// task "loose" and a top-level epic — enough to place into, to re-parent
    /// into, and to bounce off the hierarchy. Seeded through the API because
    /// an item's parent lives in its <c>## Links</c> row, not a frontmatter
    /// field, so hand-written items would come back parentless.
    /// </summary>
    private static async Task<Dictionary<string, string>> SeedAsync(HttpClient http)
    {
        var ids = new Dictionary<string, string>();

        async Task Add(string key, string type, string title, string? parent)
        {
            var resp = await http.PostAsJsonAsync(
                "/api/items", new NewItemRequestDto(type, title, parent, null), JsonOpts);
            resp.EnsureSuccessStatusCode();
            var dto = await resp.Content.ReadFromJsonAsync<ItemDto>(JsonOpts);
            ids[key] = dto!.Id;
        }

        // Creation order fixes the ranks ascending.
        await Add("p", "feature", "Parent feature", null);
        await Add("a", "task", "Child a", ids["p"]);
        await Add("b", "task", "Child b", ids["p"]);
        await Add("c", "task", "Child c", ids["p"]);
        await Add("loose", "task", "Top-level task", null);
        await Add("epic", "epic", "Top-level epic", null);
        return ids;
    }

    private static StringContent JsonBody(object body) =>
        new(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

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

    private sealed class HostCtx(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient client)
        : IAsyncDisposable
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
