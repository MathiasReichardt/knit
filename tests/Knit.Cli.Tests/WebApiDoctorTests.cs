using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Projects;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiDoctorTests
{
    [Fact]
    public async Task Doctor_verify_relink_round_trip()
    {
        var root = CreateRepoWithHealthIssues();
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
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // verify surfaces the broken ## Links target and the title drift.
                var verify = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", opts);
                Assert.NotNull(verify);
                var broken = Assert.Single(verify!.LinkTargets);
                Assert.Equal(".knit/kb/docs/widget.md", broken.Source);
                Assert.Equal("../guides/manual.md", broken.TargetPath);
                var drift = Assert.Single(verify.Titles);
                Assert.Equal(".knit/kb/docs/drift.md", drift.Source);
                Assert.Equal("Real Title", drift.FrontmatterTitle);
                Assert.Equal("Different H1", drift.BodyH1);
                // verify surfaces the invisible/invalid item file (bad id name).
                var invalid = Assert.Single(verify.InvalidItems);
                Assert.Equal(".knit/items/notanid-thing.md", invalid.Source);
                Assert.Contains("valid item id", invalid.Reason);
                // verify surfaces the stray design file (missing -design.md suffix).
                var stray = Assert.Single(verify.StrayFiles);
                Assert.Equal(".knit/designs/orphan-notes.md", stray.Source);
                Assert.Contains("design list", stray.Reason);

                // relink repairs the broken target by matching the one KB
                // artifact with the same basename (docs/manual.md).
                var relink = await http.PostAsJsonAsync(
                    "/api/doctor/relink",
                    new RelinkRequestDto(broken.Source, broken.TargetPath),
                    opts);
                Assert.Equal(HttpStatusCode.OK, relink.StatusCode);
                var relinkResult = await relink.Content.ReadFromJsonAsync<RelinkResultDto>(opts);
                Assert.NotNull(relinkResult);
                Assert.True(relinkResult!.Repaired);
                Assert.Equal("manual.md", relinkResult.NewTarget);

                // re-verify: the broken target is gone; the drift remains.
                var after = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", opts);
                Assert.Empty(after!.LinkTargets);
                Assert.Single(after.Titles);

                // relinking a target with no candidate is a no-op, not an error.
                var noCandidate = await http.PostAsJsonAsync(
                    "/api/doctor/relink",
                    new RelinkRequestDto(".knit/kb/docs/drift.md", "../nope/ghost.md"),
                    opts);
                Assert.Equal(HttpStatusCode.OK, noCandidate.StatusCode);
                var noCandidateResult = await noCandidate.Content.ReadFromJsonAsync<RelinkResultDto>(opts);
                Assert.False(noCandidateResult!.Repaired);
                Assert.Null(noCandidateResult.NewTarget);

                // relink against a missing source → 404.
                var missing = await http.PostAsJsonAsync(
                    "/api/doctor/relink",
                    new RelinkRequestDto(".knit/kb/docs/does-not-exist.md", "x.md"),
                    opts);
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_tree_with_a_file_held_by_another_process_is_423_file_locked()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRepoWithHealthIssues();
        try
        {
            var widget = Path.Combine(root, ".knit", "kb", "docs", "widget.md");
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
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                HttpResponseMessage tree;
                await using (new FileStream(widget, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    tree = await http.GetAsync("/api/kb/tree");

                Assert.Equal(HttpStatusCode.Locked, tree.StatusCode);
                Assert.Equal("application/problem+json", tree.Content.Headers.ContentType?.MediaType);
                var problem = await tree.Content.ReadFromJsonAsync<ProblemDto>(opts);
                Assert.Equal("file_locked", problem!.Code);
                Assert.Contains(widget, problem.Detail);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Relink_while_another_knit_holds_the_repo_lock_is_409_write_refused_and_writes_nothing()
    {
        var root = CreateRepoWithHealthIssues();
        try
        {
            var lockPath = Knit.Core.IO.RepoLock.PathFor(root);
            File.WriteAllText(lockPath,
                $$"""{"host":"some-other-host","pid":1,"operation":"kb split","since":"{{DateTimeOffset.UtcNow:O}}"}""");
            var widget = Path.Combine(root, ".knit", "kb", "docs", "widget.md");
            var before = File.ReadAllText(widget);

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
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var relink = await http.PostAsJsonAsync(
                    "/api/doctor/relink",
                    new RelinkRequestDto(".knit/kb/docs/widget.md", "../guides/manual.md"),
                    opts);

                Assert.Equal(HttpStatusCode.Conflict, relink.StatusCode);
                Assert.Equal("application/problem+json", relink.Content.Headers.ContentType?.MediaType);
                var problem = await relink.Content.ReadFromJsonAsync<ProblemDto>(opts);
                Assert.Equal("write_refused", problem!.Code);
                Assert.Contains("some-other-host (pid 1) running 'kb split'", problem.Detail);
                Assert.Equal(before, File.ReadAllText(widget));
                Assert.True(File.Exists(lockPath));
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Doctor_verify_surfaces_project_and_milestone_classes()
    {
        var root = CreateRepoWithProjectIssues();
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
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var verify = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", opts);
                Assert.NotNull(verify);

                var health = Assert.Single(verify!.HealthOnNonProject);
                Assert.Equal("task", health.Type);
                Assert.Equal("at-risk", health.Health);

                var pointer = Assert.Single(verify.ProjectPointers);
                Assert.Equal("task", pointer.Type);
                Assert.Equal("ghost", pointer.Slug);

                var malformed = Assert.Single(verify.MalformedMilestones);
                Assert.EndsWith("notes.txt", malformed.Source);

                var multi = Assert.Single(verify.MultipleMilestones);
                Assert.Equal(2, multi.Count);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Doctor_verify_surfaces_done_with_open_children()
    {
        var root = CreateRepoWithDoneOpenChild(out var epicId, out var featureId);
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
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var verify = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", opts);
                Assert.NotNull(verify);
                var row = Assert.Single(verify!.DoneWithOpenChildren);
                Assert.Equal(epicId, row.ParentId);
                Assert.Equal(featureId, row.ChildId);
                Assert.Equal("feature", row.ChildType);
                Assert.Equal("todo", row.ChildState);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateRepoWithDoneOpenChild(out string epicId, out string featureId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-doneopen-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());

        // Hyphen-free 18-char NanoIDs — the flat sibling-file id extraction
        // (parent resolution) splits a folder name at its first hyphen.
        epicId = "EPIC00000000000001";
        featureId = "FEATURE00000000001";

        // A done epic; its child feature is still open — the smell verify flags.
        var epicPath = store.Write(new Item
        {
            Id = epicId, Title = "shipped epic", Type = ItemType.Epic,
            State = ItemState.Done, Created = DateTimeOffset.UtcNow, Body = "# shipped epic\n",
        });
        store.Write(new Item
        {
            Id = featureId, Title = "loose end", Type = ItemType.Feature,
            State = ItemState.Todo, Created = DateTimeOffset.UtcNow, Body = "# loose end\n",
            // Parent lives in the ## Links table — the flat sibling file is the target.
            LinkRows = [new LinkRow(LinkRel.Parent, "shipped epic", Path.GetFileName(epicPath), null)],
        });

        return root;
    }

    private static string CreateRepoWithProjectIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-projissues-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());

        // Health on a non-project item.
        store.Write(new Item
        {
            Id = "01968000-0000-7000-8000-0000000000d1", Title = "risky", Type = ItemType.Task,
            State = ItemState.Todo, Health = Health.AtRisk, Created = DateTimeOffset.UtcNow, Body = "# risky\n",
        });
        // project: pointer on a non-project item.
        store.Write(new Item
        {
            Id = "01968000-0000-7000-8000-0000000000d2", Title = "misfiled", Type = ItemType.Task,
            State = ItemState.Todo, Project = "ghost", Created = DateTimeOffset.UtcNow, Body = "# misfiled\n",
        });
        // Two Milestone rows — a hand-edit state the one-per-item write path forbids.
        store.Write(new Item
        {
            Id = "01968000-0000-7000-8000-0000000000d3", Title = "double", Type = ItemType.Feature,
            State = ItemState.Todo, Created = DateTimeOffset.UtcNow, Body = "# double\n",
            LinkRows =
            [
                new LinkRow(LinkRel.Milestone, "q1", "../../projects/relaunch/milestones/q1.md", null),
                new LinkRow(LinkRel.Milestone, "q2", "../../projects/relaunch/milestones/q2.md", null),
            ],
        });

        // A project folder whose milestones/ holds a non-markdown stray.
        var milestones = ProjectsLayout.MilestonesDir(root, "relaunch");
        Directory.CreateDirectory(milestones);
        File.WriteAllText(ProjectsLayout.ReadmePath(root, "relaunch"), "---\ntitle: relaunch\n---\n\n# relaunch\n");
        File.WriteAllText(Path.Combine(milestones, "notes.txt"), "not a milestone doc\n");

        return root;
    }

    private static string CreateRepoWithHealthIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-health-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var docDir = Path.Combine(knitDir, "kb", "docs");
        Directory.CreateDirectory(docDir);

        // A repairable broken link: the ## Links target is gone, but exactly
        // one KB artifact shares its basename (docs/manual.md).
        File.WriteAllText(Path.Combine(docDir, "widget.md"),
            "---\ntitle: Widget overview\n---\n\n# Widget overview\n\nDescribes the widget subsystem.\n\n" +
            "## Links\n\n" +
            "| Rel     | Target                        | Notes |\n" +
            "|---------|-------------------------------|-------|\n" +
            "| Related | [Manual](../guides/manual.md) |       |\n");

        File.WriteAllText(Path.Combine(docDir, "manual.md"),
            "---\ntitle: Manual\n---\n\n# Manual\n\nThe manual.\n");

        // A title/h1 drift: frontmatter title differs from the leading H1.
        File.WriteAllText(Path.Combine(docDir, "drift.md"),
            "---\ntitle: Real Title\n---\n\n# Different H1\n\nBody.\n");

        // An invalid item file: the name's id-part ("notanid") isn't a valid
        // NanoID, so it silently vanishes from list/tree without this scan.
        File.WriteAllText(Path.Combine(knitDir, "items", "notanid-thing.md"),
            "---\nid: notanid\ntitle: Thing\ntype: task\nstate: todo\n---\n\n# Thing\n");

        // A stray design file: missing the `-design.md` suffix, so it never
        // shows in `design list` — surfaced as a stray file.
        var designsDir = Path.Combine(knitDir, "designs");
        Directory.CreateDirectory(designsDir);
        File.WriteAllText(Path.Combine(designsDir, "orphan-notes.md"),
            "---\ntitle: Orphan\n---\n\n# Orphan\n");

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
