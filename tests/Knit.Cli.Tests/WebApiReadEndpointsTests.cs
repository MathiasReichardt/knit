using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiReadEndpointsTests
{
    [Fact]
    public async Task Items_tree_context_identity_health_all_serve_expected_shapes()
    {
        var root = CreateRepoWithItem(out var itemId);
        try
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
            try
            {
                using var http = TestApi.Client(port);
                var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var items = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items", jsonOpts);
                Assert.NotNull(items);
                Assert.Single(items!);
                Assert.Equal(itemId, items![0].Id);
                Assert.False(string.IsNullOrEmpty(items[0].ShortId));

                var tree = await http.GetFromJsonAsync<List<TreeNodeDto>>("/api/tree", jsonOpts);
                Assert.NotNull(tree);
                Assert.Single(tree!);

                var single = await http.GetAsync($"/api/items/{itemId}");
                Assert.Equal(HttpStatusCode.OK, single.StatusCode);
                Assert.True(single.Headers.ETag is not null);
                var etag = single.Headers.ETag!.Tag;

                var notModified = new HttpRequestMessage(HttpMethod.Get, $"/api/items/{itemId}");
                notModified.Headers.TryAddWithoutValidation("If-None-Match", etag);
                var second = await http.SendAsync(notModified);
                Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

                var context = await http.GetFromJsonAsync<ContextDto>($"/api/items/{itemId}/context", jsonOpts);
                Assert.NotNull(context);
                Assert.Equal(itemId, context!.Id);
                Assert.NotNull(context.Design);
                Assert.Equal("web-ui", context.Design!.Slug);
                Assert.Null(context.Design.InheritedFrom);
                Assert.Contains("web-ui-design.md", context.Design.Path);

                var identity = await http.GetFromJsonAsync<IdentityDto>("/api/identity", jsonOpts);
                Assert.NotNull(identity);
                Assert.Equal("alice", identity!.Handle);

                var health = await http.GetFromJsonAsync<HealthDto>("/api/health", jsonOpts);
                Assert.NotNull(health);
                Assert.Equal(1, health!.ItemCount);
                Assert.False(string.IsNullOrEmpty(health.Version));

                var missing = await http.GetAsync("/api/items/00000000-0000-7000-8000-000000000099");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Identity_without_a_handle_serves_null_not_empty()
    {
        var root = CreateRepoWithItem(out _);
        ConfigLoader.SaveRawLayer(Path.Combine(root, ".knit"), ConfigLoader.FileName, []);
        try
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
            try
            {
                using var http = TestApi.Client(port);
                using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/identity"));
                Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("handle").ValueKind);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static string CreateRepoWithItem(out string itemId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-endpoints-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        itemId = "01968000-0000-7000-8000-0000000000aa";
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        var item = new Item
        {
            Id = itemId,
            Title = "Sample",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Design = "web-ui",
            Body = "# Sample\n\nbody text\n\n## Acceptance criteria\n\n- one\n- two\n",
        };
        store.Write(item);
        return root;
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
