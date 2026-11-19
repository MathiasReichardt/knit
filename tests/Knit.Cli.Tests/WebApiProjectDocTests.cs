using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// Project doc CRUD endpoints: free-form doc create /
/// delete / rename, standalone roadmap scaffold, and If-Match-guarded body
/// PATCH under <c>/api/projects/{slug}/docs/...</c>, all
/// <c>PathContainment</c>-guarded to <c>.knit/projects/</c>. Also covers the
/// new <c>SearchKind.Project</c> full-text surface.
/// </summary>
public class WebApiProjectDocTests
{
    [Fact]
    public async Task Roadmap_scaffold_creates_standalone_and_errors_if_present()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var readmeBefore = File.ReadAllText(Path.Combine(root, ".knit", "projects", "relaunch", "readme.md"));

                var create = await http.PostAsync("/api/projects/relaunch/roadmap", null);
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                var dto = await create.Content.ReadFromJsonAsync<ProjectRoadmapResultDto>(opts);
                Assert.Equal(".knit/projects/relaunch/roadmap.md", dto!.Path);
                Assert.True(File.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "roadmap.md")));
                // Readme untouched.
                Assert.Equal(readmeBefore, File.ReadAllText(Path.Combine(root, ".knit", "projects", "relaunch", "readme.md")));

                // Second call conflicts (never overwrites).
                var again = await http.PostAsync("/api/projects/relaunch/roadmap", null);
                Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Free_form_doc_create_read_patch_delete_round_trip()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Create a flat free-form doc.
                var create = await http.PostAsJsonAsync("/api/projects/relaunch/docs",
                    new ProjectDocCreateRequestDto("notes", "Notes", false), opts);
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                var created = await create.Content.ReadFromJsonAsync<ProjectDocDto>(opts);
                Assert.Equal("notes.md", created!.Path);
                Assert.Contains("# Notes", created.Body);
                // filePath is absolute (drives the copy-path button) and points at the doc.
                Assert.True(Path.IsPathFullyQualified(created.FilePath));
                Assert.EndsWith(Path.Combine("projects", "relaunch", "notes.md"), created.FilePath);

                // Read it back with an ETag.
                var get = await http.GetAsync("/api/projects/relaunch/docs/notes.md");
                Assert.Equal(HttpStatusCode.OK, get.StatusCode);
                var etag = get.Headers.ETag!.Tag;

                // PATCH the body, If-Match guarded.
                var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/docs/notes.md")
                {
                    Content = JsonContent.Create(new ProjectDocPatchRequestDto("# Notes\n\nrevised body"), options: opts),
                };
                patch.Headers.TryAddWithoutValidation("If-Match", etag);
                var patchResp = await http.SendAsync(patch);
                Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
                var patched = await patchResp.Content.ReadFromJsonAsync<ProjectDocDto>(opts);
                Assert.Contains("revised body", patched!.Body);

                // Stale If-Match → 412.
                var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/docs/notes.md")
                {
                    Content = JsonContent.Create(new ProjectDocPatchRequestDto("whatever"), options: opts),
                };
                stale.Headers.TryAddWithoutValidation("If-Match", etag);
                Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.SendAsync(stale)).StatusCode);

                // Missing If-Match → 428.
                var noMatch = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/docs/notes.md")
                {
                    Content = JsonContent.Create(new ProjectDocPatchRequestDto("x"), options: opts),
                };
                Assert.Equal((HttpStatusCode)428, (await http.SendAsync(noMatch)).StatusCode);

                // Delete it.
                var del = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch/docs/notes.md"));
                Assert.Equal(HttpStatusCode.OK, del.StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/projects/relaunch/docs/notes.md")).StatusCode);
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Readme_body_is_patchable_but_reserved_names_are_not_create_delete()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // readme body edit works via the docs endpoint.
                var get = await http.GetAsync("/api/projects/relaunch/docs/readme.md");
                Assert.Equal(HttpStatusCode.OK, get.StatusCode);
                var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/docs/readme.md")
                {
                    Content = JsonContent.Create(new ProjectDocPatchRequestDto("# relaunch\n\nedited"), options: opts),
                };
                patch.Headers.TryAddWithoutValidation("If-Match", get.Headers.ETag!.Tag);
                Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(patch)).StatusCode);

                // Can't create over a reserved name, or delete readme/milestones via docs.
                var createReserved = await http.PostAsJsonAsync("/api/projects/relaunch/docs",
                    new ProjectDocCreateRequestDto("readme", null, false), opts);
                Assert.Equal(HttpStatusCode.BadRequest, createReserved.StatusCode);

                var delReadme = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch/docs/readme.md"));
                Assert.Equal(HttpStatusCode.BadRequest, delReadme.StatusCode);

                var patchMilestone = new HttpRequestMessage(HttpMethod.Patch, "/api/projects/relaunch/docs/milestones/beta.md")
                {
                    Content = JsonContent.Create(new ProjectDocPatchRequestDto("x"), options: opts),
                };
                Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(patchMilestone)).StatusCode);
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Folder_form_doc_delete_needs_deleteFolder_and_rename_moves_it()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Folder-form doc + a sibling file.
                Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/projects/relaunch/docs",
                    new ProjectDocCreateRequestDto("guide", "Guide", true), opts)).StatusCode);
                File.WriteAllText(Path.Combine(root, ".knit", "projects", "relaunch", "guide", "extra.md"), "# extra\n");

                // Without deleteFolder the sibling blocks → 409.
                var blocked = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch/docs/guide/readme.md"));
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                var blockedDto = await blocked.Content.ReadFromJsonAsync<ProjectDocDeleteResultDto>(opts);
                Assert.False(blockedDto!.Deleted);
                Assert.NotEmpty(blockedDto.BlockingFiles);

                // Rename the whole folder-form doc.
                var rename = await http.PostAsJsonAsync("/api/projects/relaunch/docs/rename",
                    new ProjectDocRenameRequestDto("guide/readme.md", "manual"), opts);
                Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
                Assert.True((await rename.Content.ReadFromJsonAsync<ProjectDocRenameResultDto>(opts))!.WasFolder);
                Assert.True(File.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "manual", "readme.md")));
                Assert.True(File.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "manual", "extra.md")));

                // Now force-delete it.
                var forced = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/projects/relaunch/docs/manual/readme.md?deleteFolder=true"));
                Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
                Assert.False(Directory.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "manual")));
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Doc_path_escape_is_rejected()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        try
        {
            var (app, http) = await Start(root);
            try
            {
                // A traversal in the create slug is refused (nothing written outside the folder).
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var resp = await http.PostAsJsonAsync("/api/projects/relaunch/docs",
                    new ProjectDocCreateRequestDto("../../escape", null, false), opts);
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Search_indexes_project_docs()
    {
        var root = CreateRepo();
        ScaffoldProject(root, "relaunch");
        var dir = Path.Combine(root, ".knit", "projects", "relaunch");
        File.WriteAllText(Path.Combine(dir, "roadmap.md"), "# relaunch — roadmap\n\nquarterly cadence\n");
        File.WriteAllText(Path.Combine(dir, "notes.md"), "# Notes\n\nunmistakable freeform marker\n");
        Directory.CreateDirectory(Path.Combine(dir, "milestones"));
        File.WriteAllText(Path.Combine(dir, "milestones", "beta.md"), "---\ntitle: Beta cut\n---\n\n# Beta cut\n");
        try
        {
            var (app, http) = await Start(root, search: true);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var freeform = await http.GetFromJsonAsync<SearchResultDto>("/api/search?q=freeform&kind=project", opts);
                Assert.Contains(freeform!.Hits, h => h.Kind == "project" && h.Id == "relaunch/notes");

                var milestone = await http.GetFromJsonAsync<SearchResultDto>("/api/search?q=beta&kind=project", opts);
                Assert.Contains(milestone!.Hits, h => h.Kind == "project" && h.Id == "relaunch/milestones/beta");

                // Surfaces in a cross-kind query too.
                var all = await http.GetFromJsonAsync<SearchResultDto>("/api/search?q=cadence", opts);
                Assert.Contains(all!.Hits, h => h.Kind == "project" && h.Id == "relaunch/roadmap");
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Co_create_scaffolds_folder_and_mints_linked_item()
    {
        var root = CreateRepo();
        try
        {
            var (app, http) = await Start(root);
            try
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var resp = await http.PostAsJsonAsync("/api/projects",
                    new ProjectCreateRequestDto("relaunch", "Relaunch", true, null), opts);
                Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
                var dto = await resp.Content.ReadFromJsonAsync<ProjectCreateResultDto>(opts);
                Assert.Equal("relaunch", dto!.Slug);
                Assert.True(dto.CreatedItem);
                Assert.True(dto.CreatedRoadmap);
                Assert.NotEmpty(dto.ItemId);

                // Both halves landed: folder readme + roadmap, and a project item
                // pointing back at the slug.
                Assert.True(File.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "readme.md")));
                Assert.True(File.Exists(Path.Combine(root, ".knit", "projects", "relaunch", "roadmap.md")));
                var store = new ItemStore(root, new SimpleFrontmatterSerializer());
                var item = store.Read(dto.ItemId);
                Assert.NotNull(item);
                Assert.Equal("relaunch", item!.Project);

                // Re-creating over the existing folder conflicts (never overwrites).
                var again = await http.PostAsJsonAsync("/api/projects",
                    new ProjectCreateRequestDto("relaunch", null, false, null), opts);
                Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

                // A bad slug (path escape) is refused.
                var bad = await http.PostAsJsonAsync("/api/projects",
                    new ProjectCreateRequestDto("../escape", null, false, null), opts);
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            }
            finally { await Stop(app); }
        }
        finally { TryDelete(root); }
    }

    // ---- helpers ----

    private static void ScaffoldProject(string root, string slug)
    {
        var dir = Path.Combine(root, ".knit", "projects", slug);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.md"), $"# {slug}\n\n## Goal\n");
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication app, HttpClient http)> Start(string root, bool search = false)
    {
        var port = GetFreePort();
        var app = WebApiBuilder.Build([], new WebApiOptions
        {
            RootPath = root,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
            EnableSearch = search,
        });
        await app.StartAsync();
        return (app, TestApi.Client(port));
    }

    private static async Task Stop(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-pdocweb-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
        _ = new ItemStore(root, new SimpleFrontmatterSerializer());
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
