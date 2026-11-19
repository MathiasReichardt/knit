using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiDesignTests
{
    [Fact]
    public async Task Design_endpoints_cover_list_get_create_patch_archive()
    {
        var root = CreateRepoWithDesign();
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
                // This test covers the design lifecycle, not optimistic concurrency
                // (WebApiMutationTests owns the 428/412 cases). Force-match every PATCH
                // so the If-Match guard doesn't reject them.
                http.DefaultRequestHeaders.Add("If-Match", "*");
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // List active designs — includes the seeded one with its h1 title.
                var list = await http.GetFromJsonAsync<DesignSummaryDto[]>("/api/designs", opts);
                Assert.NotNull(list);
                Assert.Contains(list!, d => d.Slug == "storage" && d.Title == "Storage layer" && !d.Completed);

                // Get one — full body, frontmatter stripped.
                var one = await http.GetFromJsonAsync<DesignDto>("/api/designs/storage", opts);
                Assert.NotNull(one);
                Assert.Equal("storage", one!.Slug);
                Assert.Equal("Storage layer", one.Title);
                Assert.Contains("# Storage layer", one.Body);
                Assert.DoesNotContain("---", one.Body);

                // Create.
                var create = await http.PostAsJsonAsync("/api/designs",
                    new DesignCreateRequestDto("auth", "Auth design"), opts);
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                var created = await create.Content.ReadFromJsonAsync<DesignDto>(opts);
                Assert.Equal("auth", created!.Slug);
                Assert.Equal("Auth design", created.Title);

                // Duplicate create → 409.
                var dup = await http.PostAsJsonAsync("/api/designs",
                    new DesignCreateRequestDto("auth", null), opts);
                Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

                // PATCH rewrites body; an unchanged H1 keeps the title.
                var patch = await http.PatchAsJsonAsync("/api/designs/storage",
                    new DesignPatchRequestDto("# Storage layer\n\nRewritten.\n"), opts);
                Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
                var patched = await patch.Content.ReadFromJsonAsync<DesignDto>(opts);
                Assert.Contains("Rewritten.", patched!.Body);
                Assert.Equal("Storage layer", patched.Title);

                // Editing the leading H1 mirrors into the frontmatter title.
                var retitle = await http.PatchAsJsonAsync("/api/designs/storage",
                    new DesignPatchRequestDto("# Storage subsystem\n\nRenamed via H1.\n"), opts);
                Assert.Equal(HttpStatusCode.OK, retitle.StatusCode);
                var retitled = await retitle.Content.ReadFromJsonAsync<DesignDto>(opts);
                Assert.Equal("Storage subsystem", retitled!.Title);
                // List reflects the new title too.
                var afterRetitle = await http.GetFromJsonAsync<DesignSummaryDto[]>("/api/designs", opts);
                Assert.Contains(afterRetitle!, d => d.Slug == "storage" && d.Title == "Storage subsystem");

                // Archive moves it under completed/<slug>/.
                var archive = await http.PostAsJsonAsync("/api/designs/storage/archive",
                    new DesignArchiveRequestDto(null), opts);
                Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
                var archived = await archive.Content.ReadFromJsonAsync<DesignArchiveResultDto>(opts);
                Assert.Equal("storage", archived!.Slug);
                Assert.Contains("completed/storage/", archived.NewPath);

                // Active list no longer has it; completed list does.
                var active = await http.GetFromJsonAsync<DesignSummaryDto[]>("/api/designs", opts);
                Assert.DoesNotContain(active!, d => d.Slug == "storage");
                var completed = await http.GetFromJsonAsync<DesignSummaryDto[]>("/api/designs?archived=true", opts);
                Assert.Contains(completed!, d => d.Slug == "storage" && d.Completed);

                // Fetch the archived design by slug.
                var fetchedArchived = await http.GetFromJsonAsync<DesignDto>("/api/designs/storage", opts);
                Assert.True(fetchedArchived!.Completed);

                // Unknown slug → 404.
                var missing = await http.GetAsync("/api/designs/nope");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

                // PATCH on an archived design amends it in place (still 200,
                // still flagged completed) — finished docs stay editable.
                var patchArchived = await http.PatchAsJsonAsync("/api/designs/storage",
                    new DesignPatchRequestDto("# Storage layer\n\nAmended after archive.\n"), opts);
                Assert.Equal(HttpStatusCode.OK, patchArchived.StatusCode);
                var amended = await patchArchived.Content.ReadFromJsonAsync<DesignDto>(opts);
                Assert.Contains("Amended after archive.", amended!.Body);
                Assert.True(amended.Completed);
                // FilePath is now absolute + OS-native (matches items) — not repo-relative.
                Assert.True(Path.IsPathRooted(amended.FilePath));
                Assert.Contains(Path.Combine("completed", "storage"), amended.FilePath);
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

    private static string CreateRepoWithDesign()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-design-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var designsDir = Path.Combine(knitDir, "designs");
        Directory.CreateDirectory(designsDir);
        File.WriteAllText(Path.Combine(designsDir, "storage-design.md"),
            "---\ntitle: Storage layer\nslug: storage\n---\n\n# Storage layer\n\nHow items are stored.\n");

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
