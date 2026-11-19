using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

/// <summary>
/// `/api/file?mount=` and `/api/mounts`: a mount is the one way a
/// request reads outside the repo root, and it stays read-only.
/// </summary>
public class WebApiMountTests
{
    [Fact]
    public async Task Mounted_file_is_served_while_the_same_path_is_refused_without_the_mount()
    {
        var (root, mountDir) = CreateRepoWithMount();
        try
        {
            Directory.CreateDirectory(Path.Combine(mountDir, "specs"));
            File.WriteAllText(Path.Combine(mountDir, "specs", "plan.md"), "# external plan\n");

            await WithApi(root, async http =>
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var dto = await http.GetFromJsonAsync<FileViewDto>(
                    "/api/file?mount=docs&path=specs/plan.md", opts);
                Assert.NotNull(dto);
                Assert.Equal("# external plan\n", dto!.Content);
                Assert.Equal("docs", dto.Mount);
                // Path comes back relative to the mount, not to the repo.
                Assert.Equal("specs/plan.md", dto.Path);

                // Without the mount the same file is outside the repo root, so
                // the ordinary path guard refuses it — the mount is the only
                // door, and it is named in config, never in the request.
                var refused = await http.GetAsync("/api/file?path=../mount-docs/specs/plan.md");
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task Dot_dot_cannot_climb_out_of_a_mount()
    {
        var (root, mountDir) = CreateRepoWithMount();
        try
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(mountDir)!, "secret.txt"), "shh\n");

            await WithApi(root, async http =>
            {
                var r = await http.GetAsync("/api/file?mount=docs&path=../secret.txt");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Contains("escapes mount", await r.Content.ReadAsStringAsync());
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task An_unknown_mount_answers_with_the_config_problem()
    {
        var (root, _) = CreateRepoWithMount();
        try
        {
            await WithApi(root, async http =>
            {
                var r = await http.GetAsync("/api/file?mount=nope&path=a.md");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                var body = await r.Content.ReadAsStringAsync();
                Assert.Contains("unknown mount", body);
                Assert.Contains("knit mount add", body);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task An_unresolved_mount_is_a_config_problem_not_a_missing_file()
    {
        var (root, mountDir) = CreateRepoWithMount();
        try
        {
            Directory.Delete(mountDir, recursive: true);

            await WithApi(root, async http =>
            {
                var r = await http.GetAsync("/api/file?mount=docs&path=a.md");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Contains("unresolved", await r.Content.ReadAsStringAsync());
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task Mount_list_reports_the_effective_path_its_layer_and_its_state()
    {
        var (root, mountDir) = CreateRepoWithMount();
        try
        {
            await WithApi(root, async http =>
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var dto = await http.GetFromJsonAsync<MountListDto>("/api/mounts", opts);

                var row = Assert.Single(dto!.Mounts);
                Assert.Equal("docs", row.Name);
                Assert.Equal(Path.GetFullPath(mountDir), Path.GetFullPath(row.EffectivePath));
                Assert.Equal(ConfigLoader.FileName, row.Source);
                Assert.True(row.Resolves);
                Assert.Null(row.Reason);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task There_is_no_write_endpoint_for_mounts()
    {
        // Mount config is edited with `knit mount`. The loopback API is
        // unauthenticated and cannot pick between the committed config and the
        // per-machine override for the user, so it doesn't try.
        var (root, _) = CreateRepoWithMount();
        try
        {
            await WithApi(root, async http =>
            {
                // 405, not 404: the route exists — for GET only.
                var post = await http.PostAsJsonAsync("/api/mounts", new { name = "x", path = "../y" });
                Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task Creating_a_source_from_a_literal_path_stores_the_split_form()
    {
        // Parity with `knit kb add source`: the caller sends the path it
        // can see, the covering mount is found, and what lands on disk is the
        // repointable form.
        var (root, mountDir) = CreateRepoWithMount();
        try
        {
            Directory.CreateDirectory(Path.Combine(mountDir, "specs"));

            await WithApi(root, async http =>
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var res = await http.PostAsJsonAsync("/api/kb/entry", new
                {
                    bucket = "source",
                    slug = "plan",
                    sourceKind = "file",
                    path = "../mount-docs/specs/plan.md",
                }, opts);
                Assert.Equal(HttpStatusCode.Created, res.StatusCode);

                var dto = await res.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("docs", dto!.Frontmatter.Mount);
                Assert.Equal("specs/plan.md", dto.Frontmatter.Path);
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    [Fact]
    public async Task Creating_a_source_no_mount_covers_is_refused()
    {
        var (root, _) = CreateRepoWithMount();
        try
        {
            await WithApi(root, async http =>
            {
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var res = await http.PostAsJsonAsync("/api/kb/entry", new
                {
                    bucket = "source",
                    slug = "plan",
                    sourceKind = "file",
                    path = "../elsewhere/plan.md",
                }, opts);
                Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
                Assert.Contains("no declared mount covers it", await res.Content.ReadAsStringAsync());
            });
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(root)!);
        }
    }

    /// <summary>Repo plus a sibling `mount-docs/` declared as mount `docs`.</summary>
    private static (string Root, string MountDir) CreateRepoWithMount()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"knit-mountapi-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "repo");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        var mountDir = Path.Combine(parent, "mount-docs");
        Directory.CreateDirectory(mountDir);
        ConfigLoader.Save(knitDir, new KnitConfig(
            Handle: "alice", Display: "Alice", Mounts: new() { ["docs"] = "../mount-docs" }));
        return (root, mountDir);
    }

    private static async Task WithApi(string root, Func<HttpClient, Task> body)
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
            await body(http);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
