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
/// Server side of the due-date feature: the item DTO carries
/// `due`, health exposes `dueSoonDays`, the overdue / sort=due query params
/// work, and PATCH sets/clears the field.
/// </summary>
public class WebApiDueDateTests
{
    [Fact]
    public async Task Due_dto_health_filter_sort_and_patch()
    {
        var root = CreateRepo(out var overdueId, out var futureId, out var undatedId);
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

                var health = await http.GetFromJsonAsync<HealthDto>("/api/health", opts);
                Assert.Equal(3, health!.DueSoonDays);

                // Overdue filter: only the past-dated open item.
                var overdue = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items?overdue=1", opts);
                Assert.Single(overdue!);
                Assert.Equal(overdueId, overdue![0].Id);
                Assert.Equal("2000-01-01", overdue[0].Due);

                // sort=due: dated ascending, undated last.
                var sorted = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items?sort=due", opts);
                Assert.Equal(new[] { overdueId, futureId, undatedId }, sorted!.Select(i => i.Id).ToArray());

                // due-before filter.
                var before = await http.GetFromJsonAsync<List<ItemSummaryDto>>("/api/items?dueBefore=2020-01-01", opts);
                Assert.Single(before!);
                Assert.Equal(overdueId, before![0].Id);

                // PATCH sets then clears the due date (If-Match required).
                var get = await http.GetAsync($"/api/items/{undatedId}");
                var etag = get.Headers.ETag!.Tag;
                var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{undatedId}")
                {
                    Content = JsonContent.Create(new { due = "2030-05-05" }),
                };
                patch.Headers.TryAddWithoutValidation("If-Match", etag);
                var patched = await http.SendAsync(patch);
                Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
                var dto = await patched.Content.ReadFromJsonAsync<ItemDto>(opts);
                Assert.Equal("2030-05-05", dto!.Due);

                // Malformed date → 400.
                var reget = await http.GetAsync($"/api/items/{undatedId}");
                var etag2 = reget.Headers.ETag!.Tag;
                var bad = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{undatedId}")
                {
                    Content = JsonContent.Create(new { due = "2030-13-40" }),
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

    private static string CreateRepo(out string overdueId, out string futureId, out string undatedId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-due-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        overdueId = "01968000-0000-7000-8000-0000000000a1";
        futureId = "01968000-0000-7000-8000-0000000000a2";
        undatedId = "01968000-0000-7000-8000-0000000000a3";

        store.Write(new Item
        {
            Id = overdueId, Title = "overdue", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "a", Due = new DateOnly(2000, 1, 1), Created = DateTimeOffset.UtcNow, Body = "# overdue\n",
        });
        store.Write(new Item
        {
            Id = futureId, Title = "future", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "b", Due = new DateOnly(2999, 1, 1), Created = DateTimeOffset.UtcNow, Body = "# future\n",
        });
        store.Write(new Item
        {
            Id = undatedId, Title = "undated", Type = ItemType.Task, State = ItemState.Todo,
            Rank = "c", Created = DateTimeOffset.UtcNow, Body = "# undated\n",
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
