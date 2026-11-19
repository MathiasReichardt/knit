using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiTimelineTests
{
    private static readonly string TargetId = new('A', 18);
    private static readonly string BlockerId = new('B', 18);

    [Fact]
    public async Task Timeline_endpoint_reports_at_risk_ghosts_and_health_counts()
    {
        var root = CreateRepo();
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
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Unfiltered: the undated blocker of a dated item is at-risk,
                // with a blocks edge to the endangered target.
                var timeline = await http.GetFromJsonAsync<TimelineDto>("/api/timeline", opts);
                Assert.NotNull(timeline);
                var blocker = Assert.Single(timeline!.Nodes, n => n.Id == BlockerId);
                Assert.True(blocker.AtRisk);
                Assert.False(blocker.Ghost);
                var target = Assert.Single(timeline.Nodes, n => n.Id == TargetId);
                Assert.Equal([BlockerId], target.AtRiskBlockers);
                // The node surfaces its milestone as the qualified ref.
                Assert.Equal("relaunch/beta", target.Milestone);
                Assert.Null(blocker.Milestone);
                // The overlay carries the one dated milestone.
                var ms = Assert.Single(timeline.Milestones);
                Assert.Equal("relaunch", ms.Project);
                Assert.Equal("beta", ms.Slug);
                Assert.Equal("2099-01-01", ms.Target);
                Assert.Equal("planned", ms.State);
                var edge = Assert.Single(timeline.Edges);
                Assert.Equal(BlockerId, edge.From);
                Assert.Equal(TargetId, edge.To);
                Assert.True(edge.AtRisk);

                // Slate-filtered: the out-of-filter blocker survives as a ghost.
                var filtered = await http.GetFromJsonAsync<TimelineDto>("/api/timeline?slate=q1", opts);
                var ghost = Assert.Single(filtered!.Nodes, n => n.Id == BlockerId);
                Assert.True(ghost.Ghost);
                Assert.Single(filtered.Edges);

                // Depth validation errors instead of guessing.
                var bad = await http.GetAsync("/api/timeline?depth=story");
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);

                // Health carries the repo-wide open-item due counts (the
                // overdue target feeds the nav-rail attention dot).
                var health = await http.GetFromJsonAsync<HealthDto>("/api/health", opts);
                Assert.Equal(1, health!.OverdueCount);
                Assert.Equal(0, health.DueSoonCount);
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
    public async Task Blocked_by_row_paints_an_edge_and_unblock_can_remove_it()
    {
        var root = CreateBlockedByRepo();
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
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // The natural blocking flow authors only a Blocked-by row on
                // the blocked item — it must still produce the edge,
                // and the node carries the live blocked reason.
                var timeline = await http.GetFromJsonAsync<TimelineDto>("/api/timeline", opts);
                var edge = Assert.Single(timeline!.Edges);
                Assert.Equal(BlockerId, edge.From);
                Assert.Equal(TargetId, edge.To);
                var target = Assert.Single(timeline.Nodes, n => n.Id == TargetId);
                Assert.StartsWith("waiting on the blocker; blocked by ", target.Blocked);

                var item = await http.GetAsync($"/api/items/{TargetId}");
                var etag = item.Headers.ETag!.Tag;
                // Unblock drops the row → edge disappears.
                var req = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{TargetId}/unblock");
                req.Headers.TryAddWithoutValidation("If-Match", etag);
                var unblock = await http.SendAsync(req);
                Assert.True(unblock.IsSuccessStatusCode);

                var after = await http.GetFromJsonAsync<TimelineDto>("/api/timeline", opts);
                Assert.Empty(after!.Edges);
                Assert.Null(Assert.Single(after.Nodes, n => n.Id == TargetId).Blocked);
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
    public async Task Project_filter_scopes_the_timeline_to_the_project_subtree()
    {
        var root = CreateProjectScopeRepo();
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
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // ?project scopes to the project item's subtree (containment) —
                // the outside item drops even though it is dated in the window.
                var scoped = await http.GetFromJsonAsync<TimelineDto>(
                    "/api/timeline?project=relaunch&depth=all", opts);
                Assert.NotNull(scoped);
                var ids = scoped!.Nodes.Select(n => n.Id).ToHashSet();
                Assert.Contains(InsideId, ids);
                Assert.Contains(ProjectItemId, ids);   // the project item itself is in scope
                Assert.DoesNotContain(OutsideId, ids);
                // …and the overlay follows the scope: the other project's
                // roadmap used to render over this project's work.
                var ms = Assert.Single(scoped.Milestones);
                Assert.Equal("relaunch", ms.Project);

                var all = await http.GetFromJsonAsync<TimelineDto>("/api/timeline?depth=all", opts);
                Assert.Equal(2, all!.Milestones.Count);

                // Unknown project 400s rather than returning an empty timeline.
                var bad = await http.GetAsync("/api/timeline?project=nope");
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static readonly string ProjectItemId = new('P', 18);
    private static readonly string InsideId = new('I', 18);
    private static readonly string OutsideId = new('O', 18);

    private static string CreateProjectScopeRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-timeline-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        // The doc folder must exist for the slug to validate. A dated
        // milestone in each project proves ?project narrows the overlay too.
        Directory.CreateDirectory(Path.Combine(knitDir, "projects", "relaunch"));
        foreach (var (project, slug) in new[] { ("relaunch", "beta"), ("sunset", "cutover") })
        {
            var msDir = Path.Combine(knitDir, "projects", project, "milestones");
            Directory.CreateDirectory(msDir);
            File.WriteAllText(Path.Combine(msDir, $"{slug}.md"),
                $"---\ntitle: {slug}\ntarget: 2099-01-01\nstate: planned\n---\n# {slug}\n");
        }

        var future = DateOnly.FromDateTime(DateTime.Now).AddDays(10);
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = ProjectItemId,
            Title = "relaunch program",
            Type = ItemType.Project,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Project = "relaunch",
            Body = "# relaunch program\n",
        });
        store.Write(new Item
        {
            Id = InsideId,
            Title = "in the project",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            // Parent is denormalized from a Parent link row on read — set the
            // row, not the field, or the containment walk won't see it.
            LinkRows = [new LinkRow(LinkRel.Parent, "relaunch program", $"../{ProjectItemId}-relaunch-program.md", null)],
            Due = future,
            Body = "# in the project\n",
        });
        store.Write(new Item
        {
            Id = OutsideId,
            Title = "outside the project",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Due = future,
            Body = "# outside the project\n",
        });
        return root;
    }

    private static string CreateBlockedByRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-timeline-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = TargetId,
            Title = "dated target",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Due = DateOnly.FromDateTime(DateTime.Now).AddDays(5),
            BlockedReason = "waiting on the blocker",
            Body = "# dated target\n",
            LinkRows = [new LinkRow(LinkRel.BlockedBy, "undated blocker", $"../{BlockerId}-undated-blocker.md", null)],
        });
        store.Write(new Item
        {
            Id = BlockerId,
            Title = "undated blocker",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Body = "# undated blocker\n",
        });
        return root;
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-timeline-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        // A dated milestone sub-doc for the overlay.
        var msDir = Path.Combine(knitDir, "projects", "relaunch", "milestones");
        Directory.CreateDirectory(msDir);
        File.WriteAllText(Path.Combine(msDir, "beta.md"),
            "---\ntitle: Beta\ntarget: 2099-01-01\nstate: planned\n---\n# Beta\n");

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = TargetId,
            Title = "dated target",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Due = DateOnly.FromDateTime(DateTime.Now).AddDays(-5),
            Slate = "q1",
            Body = "# dated target\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "beta", "../../projects/relaunch/milestones/beta.md", null)],
        });
        store.Write(new Item
        {
            Id = BlockerId,
            Title = "undated blocker",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Body = "# undated blocker\n",
            LinkRows = [new LinkRow(LinkRel.Blocks, "dated target", $"../{TargetId}-dated-target.md", null)],
        });
        return root;
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
