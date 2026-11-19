using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// Server side of priority: the item DTO carries the resolved
/// `priority` (always, even the normal default), the minPriority threshold
/// and sort=priority query params work, and PATCH sets / resets the field.
/// </summary>
public class WebApiPriorityTests
{
    [Fact]
    public async Task Priority_dto_filter_sort_and_patch()
    {
        var root = CreateRepo(out var criticalId, out var importantId, out var normalId);
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
                var opts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

                // DTO always carries the resolved value, incl. the normal default.
                var all = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items", opts);
                Assert.NotNull(all);
                Assert.Equal("normal", all.Single(i => i.Id == normalId).Priority);
                Assert.Equal("critical", all.Single(i => i.Id == criticalId).Priority);

                // minPriority threshold — important + critical, not normal.
                var min = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items?minPriority=important", opts);
                var minIds = min!.Select(i => i.Id).ToHashSet();
                Assert.Contains(importantId, minIds);
                Assert.Contains(criticalId, minIds);
                Assert.DoesNotContain(normalId, minIds);

                // sort=priority: critical-first, rank tiebreak.
                var sorted = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items?sort=priority", opts);
                Assert.Equal(new[] { criticalId, importantId, normalId }, sorted!.Select(i => i.Id).ToArray());

                // PATCH sets then resets.
                var get = await http.GetAsync($"/api/items/{normalId}");
                var etag = get.Headers.ETag!.Tag;
                var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{normalId}")
                {
                    Content = JsonContent.Create(new { priority = "critical" }),
                };
                patch.Headers.TryAddWithoutValidation("If-Match", etag);
                var patched = await http.SendAsync(patch);
                Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
                var dto = await patched.Content.ReadFromJsonAsync<ItemDto>(opts);
                Assert.Equal("critical", dto!.Priority);

                // Unknown level → 400.
                var reget = await http.GetAsync($"/api/items/{normalId}");
                var etag2 = reget.Headers.ETag!.Tag;
                var bad = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{normalId}")
                {
                    Content = JsonContent.Create(new { priority = "urgent" }),
                };
                bad.Headers.TryAddWithoutValidation("If-Match", etag2);
                var badResp = await http.SendAsync(bad);
                Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static string CreateRepo(out string criticalId, out string importantId, out string normalId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-prio-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        criticalId = "01968000-0000-7000-8000-0000000000b1";
        importantId = "01968000-0000-7000-8000-0000000000b2";
        normalId = "01968000-0000-7000-8000-0000000000b3";

        store.Write(new Item
        {
            Id = criticalId, Title = "critical", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "a", Priority = Priority.Critical, Created = DateTimeOffset.UtcNow, Body = "# critical\n",
        });
        store.Write(new Item
        {
            Id = importantId, Title = "important", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "b", Priority = Priority.Important, Created = DateTimeOffset.UtcNow, Body = "# important\n",
        });
        store.Write(new Item
        {
            Id = normalId, Title = "normal", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "c", Created = DateTimeOffset.UtcNow, Body = "# normal\n",
        });
        return root;
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
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
