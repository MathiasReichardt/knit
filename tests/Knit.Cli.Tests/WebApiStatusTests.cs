using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// Server side of the summary strip. Shares
/// <c>StatusRollup</c> with `knit status`, so this pins the wire shape and the
/// scope handling rather than re-testing the arithmetic.
/// </summary>
public class WebApiStatusTests
{
    [Fact]
    public async Task Status_reports_scope_counts_and_rejects_an_unknown_project()
    {
        var root = CreateRepo(out var projectId);
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

                var all = await http.GetFromJsonAsync<StatusDto>("/api/status", opts);
                Assert.Equal("all items", all!.Scope);
                Assert.Equal(3, all.Total);
                Assert.Equal(1, all.Done);
                Assert.Equal(33, all.Percent);

                // Health surfaces because the scope holds exactly one project.
                Assert.Equal("at-risk", all.Health);

                var under = await http.GetFromJsonAsync<StatusDto>($"/api/status?under={projectId}", opts);
                Assert.Equal(2, under!.Total); // the project + its child
                Assert.StartsWith("under: ", under.Scope);

                // A silent empty report would read as "this project is empty".
                var unknown = await http.GetAsync("/api/status?project=nope");
                Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

                var missing = await http.GetAsync("/api/status?under=deadbeef");
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

    private static string CreateRepo(out string projectId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-status-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        // Real NanoIDs: a Parent link row only resolves when the target folder
        // carries a valid id, so hand-picked uuid-shaped strings silently
        // produce an unparented child.
        projectId = IdGenerator.New();
        var childId = IdGenerator.New();
        var loneId = IdGenerator.New();

        var projectFile = store.Write(new Item
        {
            Id = projectId, Title = "relaunch", Type = ItemType.Project, State = ItemState.Todo,
            Health = Health.AtRisk, Created = DateTimeOffset.UtcNow, Body = "# relaunch\n",
        });
        // Items are written flat (`<id>-<slug>.md`) until they grow a folder,
        // so a sibling target is the bare file name.
        var projectTarget = Path.GetFileName(projectFile);
        // Parent is authoritative in the body link table, not the frontmatter
        // field — a bare `Parent = id` doesn't round-trip through the store.
        store.Write(new Item
        {
            Id = childId, Title = "child", Type = ItemType.Task, State = ItemState.Done,
            Parent = projectId, Created = DateTimeOffset.UtcNow, Body = "# child\n",
            LinkRows = [new LinkRow(LinkRel.Parent, "relaunch", projectTarget, null)],
        });
        store.Write(new Item
        {
            Id = loneId, Title = "outsider", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# outsider\n",
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
