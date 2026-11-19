using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Knit.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// WebApi surfaces for project doc folders: the
/// <c>/api/projects</c> browse endpoints, milestone backlinks, the
/// <c>?milestone=</c> filter on <c>/api/items</c>, and the FileWatcher
/// <c>projects</c> SSE kind.
/// </summary>
public class WebApiProjectTests
{
    [Fact]
    public async Task Projects_endpoints_list_folders_and_milestone_backlinks()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", "2026-09-01"), ("go-live", "Go live", null));
        File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "notes.md"), "# notes");
        var linkedId = "PROJTESTITEM0000A1";
        store.Write(new Item
        {
            Id = linkedId, Title = "In the cut", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# In the cut\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var projects = await http.GetFromJsonAsync<ProjectFolderDto[]>("/api/projects", opts);
                var project = Assert.Single(projects!);
                Assert.Equal("relaunch", project.Slug);
                Assert.True(project.HasReadme);
                Assert.False(project.HasRoadmap);
                Assert.Equal(["notes.md"], project.Files);
                Assert.Equal(2, project.Milestones.Count);
                var beta = Assert.Single(project.Milestones, m => m.Slug == "beta");
                Assert.Equal("Beta cut", beta.Title);
                Assert.Equal("2026-09-01", beta.Target);
                Assert.Equal(1, beta.LinkedCount);

                var detail = await http.GetFromJsonAsync<MilestoneDetailDto>("/api/projects/relaunch/milestones/beta", opts);
                Assert.Equal("Beta cut", detail!.Title);
                var backlink = Assert.Single(detail.Backlinks);
                Assert.Equal(linkedId, backlink.Id);
                // filePath is absolute (drives the copy-path button) and points at the milestone.
                Assert.True(Path.IsPathFullyQualified(detail.FilePath));
                Assert.EndsWith(Path.Combine("projects", "relaunch", "milestones", "beta.md"), detail.FilePath);

                var missing = await http.GetAsync("/api/projects/relaunch/milestones/nope");
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
    public async Task Items_and_tree_filter_by_milestone()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));
        SeedProject(root, "other", ("beta", "Other beta", null));
        var linkedId = "PROJTESTITEM0000B1";
        var looseId = "PROJTESTITEM0000B2";
        store.Write(new Item
        {
            Id = linkedId, Title = "Linked", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Linked\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        store.Write(new Item
        {
            Id = looseId, Title = "Loose", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Loose\n",
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var filtered = await http.GetFromJsonAsync<ItemSummaryDto[]>("/api/items?milestone=relaunch/beta", opts);
                var only = Assert.Single(filtered!);
                Assert.Equal(linkedId, only.Id);

                var tree = await http.GetFromJsonAsync<TreeNodeDto[]>("/api/tree?milestone=relaunch/beta", opts);
                Assert.Single(tree!);

                // Ambiguous bare slug (both projects carry 'beta') → 400.
                var ambiguous = await http.GetAsync("/api/items?milestone=beta");
                Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);

                var unknown = await http.GetAsync("/api/items?milestone=relaunch/nope");
                Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
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
    public async Task Add_and_remove_milestone_link_round_trips()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));
        var id = "PROJTESTITEM0000C1";
        store.Write(new Item
        {
            Id = id, Title = "Work", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Work\n",
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // If-Match guard: fetch the current ETag first.
                var get = await http.GetAsync($"/api/items/{id}");
                var etag = get.Headers.ETag!.Tag;

                var addReq = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{id}/links")
                {
                    Content = JsonContent.Create(new LinkRequestDto("milestone", "relaunch/beta", null)),
                };
                addReq.Headers.TryAddWithoutValidation("If-Match", etag);
                var addResp = await http.SendAsync(addReq);
                Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
                var added = await addResp.Content.ReadFromJsonAsync<ItemDto>(opts);
                var row = Assert.Single(added!.Links, l => l.Rel == "Milestone");
                Assert.Equal("Beta cut", row.TargetText);

                // The ?milestone= filter now matches the item.
                var filtered = await http.GetFromJsonAsync<ItemSummaryDto[]>(
                    "/api/items?milestone=relaunch/beta", opts);
                Assert.Contains(filtered!, i => i.Id == id);

                // Remove it by the qualified ref.
                var delReq = new HttpRequestMessage(
                    HttpMethod.Delete, $"/api/items/{id}/links/milestone/relaunch/beta");
                delReq.Headers.TryAddWithoutValidation("If-Match", addResp.Headers.ETag!.Tag);
                var delResp = await http.SendAsync(delReq);
                Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
                var removed = await delResp.Content.ReadFromJsonAsync<ItemDto>(opts);
                Assert.DoesNotContain(removed!.Links, l => l.Rel == "Milestone");
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
    public async Task Delete_project_removes_folder_item_reroots_children_and_leaves_links_dangling()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));

        var projId = store.NewItemId();
        store.Write(new Item
        {
            Id = projId, Title = "Relaunch program", Type = ItemType.Project, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Relaunch program\n", Project = "relaunch",
        });
        var projFile = store.FindItemFile(projId)!;
        var childId = store.NewItemId();
        store.Write(new Item
        {
            Id = childId, Title = "Child", Type = ItemType.Feature, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Child\n",
            LinkRows = [new LinkRow(LinkRel.Parent, "Relaunch program", LinkPath.Relative(store.ItemsDir, projFile), null)],
        });
        var linkedId = store.NewItemId();
        store.Write(new Item
        {
            Id = linkedId, Title = "Linked", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Linked\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Dry-run reports the impact without writing.
                var dry = await (await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch?dryRun=true")))
                    .Content.ReadFromJsonAsync<ProjectDeleteResultDto>(opts);
                Assert.True(dry!.DryRun);
                Assert.Equal(projId, dry.RemovedItemId);
                Assert.Equal([childId], dry.ReRootedChildIds);
                Assert.Equal([linkedId], dry.DanglingItemIds);
                Assert.False(dry.FolderDeleted);
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/items/{projId}")).StatusCode);

                var res = await (await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch")))
                    .Content.ReadFromJsonAsync<ProjectDeleteResultDto>(opts);
                Assert.Equal(projId, res!.RemovedItemId);
                Assert.Equal([childId], res.ReRootedChildIds);
                Assert.Equal([linkedId], res.DanglingItemIds);
                Assert.Empty(res.ScrubbedItemIds);
                Assert.Equal(1, res.MilestoneCount);
                Assert.True(res.FolderDeleted);

                // Project item gone; doc folder gone.
                Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/items/{projId}")).StatusCode);
                var folders = await http.GetFromJsonAsync<ProjectFolderDto[]>("/api/projects", opts);
                Assert.Empty(folders!);

                // Direct child re-rooted (parent cleared), not deleted.
                var child = await http.GetFromJsonAsync<ItemDto>($"/api/items/{childId}", opts);
                Assert.Null(child!.Parent);

                // Milestone back-link left dangling (row still present).
                var linked = await http.GetFromJsonAsync<ItemDto>($"/api/items/{linkedId}", opts);
                Assert.Contains(linked!.Links, l => l.Rel == "Milestone");
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
    public async Task Delete_project_with_scrubLinks_strips_back_references()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));
        var linkedId = store.NewItemId();
        store.Write(new Item
        {
            Id = linkedId, Title = "Linked", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Linked\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var res = await (await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch?scrubLinks=true")))
                    .Content.ReadFromJsonAsync<ProjectDeleteResultDto>(opts);
                Assert.Equal([linkedId], res!.ScrubbedItemIds);
                Assert.Empty(res.DanglingItemIds);

                var linked = await http.GetFromJsonAsync<ItemDto>($"/api/items/{linkedId}", opts);
                Assert.DoesNotContain(linked!.Links, l => l.Rel == "Milestone");
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
    public async Task Delete_unknown_project_is_404()
    {
        var root = CreateRepo(out _);
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/nope"));
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
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
    public async Task Milestone_create_patch_and_delete_round_trip()
    {
        var root = CreateRepo(out _);
        Directory.CreateDirectory(Path.Combine(root, ".knit", "projects", "relaunch"));
        File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "readme.md"), "# relaunch\n");
        File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "roadmap.md"), "# relaunch — roadmap\n"); // milestones are roadmap-gated
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Create with a non-default state.
                var create = await http.PostAsJsonAsync("/api/projects/relaunch/milestones",
                    new MilestoneCreateRequestDto("beta", "Beta cut", "2026-09-01", "reached"), opts);
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                var created = await create.Content.ReadFromJsonAsync<MilestoneDetailDto>(opts);
                Assert.Equal("Beta cut", created!.Title);
                Assert.Equal("reached", created.State);
                var etag = create.Headers.ETag!.Tag;

                // PATCH frontmatter + body, If-Match guarded.
                var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/milestones/beta")
                {
                    Content = JsonContent.Create(
                        new MilestonePatchRequestDto(
                            new MilestonePatchFrontmatterDto(null, "dropped", ""), "# Beta cut\n\nrevised"),
                        options: opts),
                };
                patch.Headers.TryAddWithoutValidation("If-Match", etag);
                var patchResp = await http.SendAsync(patch);
                Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
                var patched = await patchResp.Content.ReadFromJsonAsync<MilestoneDetailDto>(opts);
                Assert.Equal("dropped", patched!.State);
                Assert.Null(patched.Target);              // cleared via ""
                Assert.Contains("revised", patched.Body);

                // Stale If-Match is rejected 412.
                var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/milestones/beta")
                {
                    Content = JsonContent.Create(new MilestonePatchRequestDto(
                        new MilestonePatchFrontmatterDto(null, "planned", null), null), options: opts),
                };
                stale.Headers.TryAddWithoutValidation("If-Match", etag);
                Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.SendAsync(stale)).StatusCode);

                // Delete.
                var del = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch/milestones/beta"));
                Assert.Equal(HttpStatusCode.OK, del.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/projects/relaunch/milestones/beta")).StatusCode);
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
    public async Task Milestone_rename_repoints_backlinks_delete_dangles_and_scrubs()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));
        var linkedId = store.NewItemId();
        store.Write(new Item
        {
            Id = linkedId, Title = "Linked", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Linked\n",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var rn = await http.PostAsJsonAsync("/api/projects/relaunch/milestones/beta/rename",
                    new MilestoneRenameRequestDto("beta-2"), opts);
                Assert.Equal(HttpStatusCode.OK, rn.StatusCode);
                var renamed = await rn.Content.ReadFromJsonAsync<MilestoneRenameResultDto>(opts);
                Assert.Equal("beta-2", renamed!.NewSlug);
                Assert.Equal(linkedId, Assert.Single(renamed.RepointedItemIds));

                // Back-link re-pointed at the new file.
                var linked = await http.GetFromJsonAsync<ItemDto>($"/api/items/{linkedId}", opts);
                var row = Assert.Single(linked!.Links, l => l.Rel == "Milestone");
                Assert.Contains("beta-2.md", row.TargetPath);

                // dry-run delete reports the dangling backlink without writing.
                var dry = await (await http.SendAsync(new HttpRequestMessage(
                        HttpMethod.Delete, "/api/projects/relaunch/milestones/beta-2?dryRun=true")))
                    .Content.ReadFromJsonAsync<MilestoneDeleteResultDto>(opts);
                Assert.True(dry!.DryRun);
                Assert.Equal(linkedId, Assert.Single(dry.DanglingItemIds));
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/projects/relaunch/milestones/beta-2")).StatusCode);

                // scrubLinks delete strips the row.
                var res = await (await http.SendAsync(new HttpRequestMessage(
                        HttpMethod.Delete, "/api/projects/relaunch/milestones/beta-2?scrubLinks=true")))
                    .Content.ReadFromJsonAsync<MilestoneDeleteResultDto>(opts);
                Assert.Equal(linkedId, Assert.Single(res!.ScrubbedItemIds));
                Assert.Empty(res.DanglingItemIds);
                var after = await http.GetFromJsonAsync<ItemDto>($"/api/items/{linkedId}", opts);
                Assert.DoesNotContain(after!.Links, l => l.Rel == "Milestone");
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
    public async Task Milestone_move_repoints_backlinks_reports_cross_project_and_needs_a_slug_on_collision()
    {
        var root = CreateRepo(out var store);
        SeedProject(root, "relaunch", ("beta", "Beta cut", null));
        SeedProject(root, "platform", ("beta", "Platform beta", null));
        var linkedId = store.NewItemId();
        store.Write(new Item
        {
            Id = linkedId, Title = "Linked", Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# Linked",
            LinkRows = [new LinkRow(LinkRel.Milestone, "Beta cut", "../projects/relaunch/milestones/beta.md", null)],
        });
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Destination already holds 'beta' — refused, no rename flag.
                var clash = await http.PostAsJsonAsync("/api/projects/relaunch/milestones/beta/move",
                    new MilestoneMoveRequestDto("platform", null), opts);
                Assert.NotEqual(HttpStatusCode.OK, clash.StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/projects/relaunch/milestones/beta")).StatusCode);

                var mv = await http.PostAsJsonAsync("/api/projects/relaunch/milestones/beta/move",
                    new MilestoneMoveRequestDto("platform", "beta-cut"), opts);
                Assert.Equal(HttpStatusCode.OK, mv.StatusCode);
                var moved = await mv.Content.ReadFromJsonAsync<MilestoneMoveResultDto>(opts);
                Assert.Equal("platform", moved!.ToProject);
                Assert.Equal("beta-cut", moved.NewSlug);
                Assert.Equal(linkedId, Assert.Single(moved.RepointedItemIds));
                // The item sits outside any project subtree, so it now links across a boundary.
                Assert.Equal(linkedId, Assert.Single(moved.CrossProjectItemIds));

                Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/projects/relaunch/milestones/beta")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/projects/platform/milestones/beta-cut")).StatusCode);

                var linked = await http.GetFromJsonAsync<ItemDto>($"/api/items/{linkedId}", opts);
                var row = Assert.Single(linked!.Links, l => l.Rel == "Milestone");
                Assert.Contains("platform", row.TargetPath);
                Assert.Contains("beta-cut.md", row.TargetPath);
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
    public async Task Milestone_reorder_rewrites_ranks_and_reflows_the_list()
    {
        var root = CreateRepo(out _);
        Directory.CreateDirectory(Path.Combine(root, ".knit", "projects", "relaunch"));
        File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "readme.md"), "# relaunch\n");
        File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "roadmap.md"), "# relaunch — roadmap\n"); // milestones are roadmap-gated
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Created alpha, beta, gamma → the list comes back in creation
                // order (append rank), each with a rank.
                foreach (var s in new[] { "alpha", "beta", "gamma" })
                    Assert.Equal(HttpStatusCode.Created,
                        (await http.PostAsJsonAsync("/api/projects/relaunch/milestones",
                            new MilestoneCreateRequestDto(s, s, null, null), opts)).StatusCode);

                var before = await http.GetFromJsonAsync<ProjectFolderDto[]>("/api/projects", opts);
                Assert.Equal(new[] { "alpha", "beta", "gamma" },
                    before!.Single().Milestones.Select(m => m.Slug).ToArray());
                Assert.All(before!.Single().Milestones, m => Assert.False(string.IsNullOrEmpty(m.Rank)));

                var reorder = await http.PostAsJsonAsync("/api/projects/relaunch/milestones/reorder",
                    new MilestoneReorderRequestDto(new[] { "gamma", "alpha", "beta" }), opts);
                Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);

                var after = await http.GetFromJsonAsync<ProjectFolderDto[]>("/api/projects", opts);
                Assert.Equal(new[] { "gamma", "alpha", "beta" },
                    after!.Single().Milestones.Select(m => m.Slug).ToArray());

                // A non-permutation is rejected without writing.
                var bad = await http.PostAsJsonAsync("/api/projects/relaunch/milestones/reorder",
                    new MilestoneReorderRequestDto(new[] { "gamma", "alpha" }), opts);
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
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
    public void FileWatcher_classifies_project_doc_paths_as_projects_kind()
    {
        var root = CreateRepo(out _);
        try
        {
            var watcher = new FileWatcher(
                new WebApiOptions { RootPath = root, Port = 0 },
                NullLogger<FileWatcher>.Instance);

            var milestone = Path.Combine(root, ".knit", "projects", "relaunch", "milestones", "beta.md");
            Assert.Equal(("projects", "relaunch/milestones/beta"), watcher.Classify(milestone));

            var readme = Path.Combine(root, ".knit", "projects", "relaunch", "readme.md");
            Assert.Equal(("projects", "relaunch/readme"), watcher.Classify(readme));

            // Bare projects/ root stays unclassified (no doc id to refresh).
            Assert.Null(watcher.Classify(Path.Combine(root, ".knit", "projects")));
        }
        finally { TryDelete(root); }
    }

    private static void SeedProject(string root, string slug, params (string Slug, string Title, string? Target)[] milestones)
    {
        var dir = Path.Combine(root, ".knit", "projects", slug);
        Directory.CreateDirectory(Path.Combine(dir, "milestones"));
        File.WriteAllText(Path.Combine(dir, "readme.md"), $"# {slug}\n");
        foreach (var (msSlug, title, target) in milestones)
        {
            var fm = $"---\ntitle: {title}\n" + (target is null ? "" : $"target: {target}\n") + "state: planned\n---\n";
            File.WriteAllText(Path.Combine(dir, "milestones", $"{msSlug}.md"), fm + $"\n# {title}\n");
        }
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient http)> Start(string root)
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = root,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
            EnableSearch = false,
        });
        await app.StartAsync();
        return (app, TestApi.Client(port));
    }

    private static string CreateRepo(out ItemStore store)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-projects-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
        store = new ItemStore(root, new SimpleFrontmatterSerializer());
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
