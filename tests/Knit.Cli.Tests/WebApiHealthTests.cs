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
/// Server side of the project-only <c>health</c> signal: the
/// item DTO carries it, PATCH sets / clears it, and the field is rejected on
/// non-project items (mirrors the CLI <c>set health=</c> guard).
/// </summary>
public class WebApiHealthTests
{
    [Fact]
    public async Task Health_dto_patch_set_clear_and_project_only()
    {
        var root = CreateRepo(out var projectId, out var taskId);
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

                // Unset health starts null (no signal, distinct from on-track).
                var initial = await http.GetFromJsonAsync<ItemDto>($"/api/items/{projectId}", opts);
                Assert.Null(initial!.Health);

                // PATCH sets it on a project.
                var dto = await Patch(http, opts, projectId, new { health = "at-risk" });
                Assert.Equal("at-risk", dto.Health);

                // "" clears back to no signal.
                var cleared = await Patch(http, opts, projectId, new { health = "" });
                Assert.Null(cleared.Health);

                // Unknown value → 400.
                var bad = await PatchRaw(http, projectId, new { health = "sinking" });
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

                // Non-project item rejects a real value → 400.
                var onTask = await PatchRaw(http, taskId, new { health = "on-track" });
                Assert.Equal(HttpStatusCode.BadRequest, onTask.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static async Task<ItemDto> Patch(HttpClient http, System.Text.Json.JsonSerializerOptions opts, string id, object body)
    {
        var resp = await PatchRaw(http, id, body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemDto>(opts))!;
    }

    private static async Task<HttpResponseMessage> PatchRaw(HttpClient http, string id, object body)
    {
        var get = await http.GetAsync($"/api/items/{id}");
        var etag = get.Headers.ETag!.Tag;
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{id}")
        {
            Content = JsonContent.Create(body),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        return await http.SendAsync(patch);
    }

    private static string CreateRepo(out string projectId, out string taskId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-health-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        projectId = "01968000-0000-7000-8000-0000000000c1";
        taskId = "01968000-0000-7000-8000-0000000000c2";

        store.Write(new Item
        {
            Id = projectId, Title = "relaunch", Type = ItemType.Project, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# relaunch\n",
        });
        store.Write(new Item
        {
            Id = taskId, Title = "a task", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# a task\n",
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
