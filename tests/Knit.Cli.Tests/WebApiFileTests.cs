using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Knit.Core.Config;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Knit.WebApi.Endpoints;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiFileTests
{
    [Fact]
    public async Task File_endpoint_serves_text_rejects_escape_caps_size_flags_binary()
    {
        var root = CreateRepo();
        try
        {
            // Inside repo: a code file and a KB index file.
            var srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            var csFile = Path.Combine(srcDir, "Foo.cs");
            File.WriteAllText(csFile, "namespace X;\npublic class Foo { }\n");

            var kbDir = Path.Combine(root, ".knit", "kb", "docs");
            Directory.CreateDirectory(kbDir);
            File.WriteAllText(Path.Combine(kbDir, "widget.md"), "# Widget\n");

            // Outside repo: a sibling file that must not be served.
            var outside = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}.txt");
            File.WriteAllText(outside, "secret\n");

            // Binary-ish file under repo (contains a NUL byte).
            var binFile = Path.Combine(srcDir, "blob.bin");
            File.WriteAllBytes(binFile, [0x48, 0x65, 0x00, 0x6C, 0x6F]);

            // Oversized file just over the cap.
            var bigFile = Path.Combine(srcDir, "big.txt");
            File.WriteAllBytes(bigFile, new byte[FileEndpoints.MaxBytes + 1]);

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

                // Serves source file content.
                var src = await http.GetFromJsonAsync<FileViewDto>("/api/file?path=src/Foo.cs", opts);
                Assert.NotNull(src);
                Assert.Equal("text/x-csharp", src!.Mime);
                Assert.False(src.Truncated);
                Assert.Contains("class Foo", src.Content);
                Assert.Equal(Path.GetFullPath(csFile), src.FilePath);

                // Serves a KB markdown file too (the viewer also handles in-kb paths).
                var md = await http.GetFromJsonAsync<FileViewDto>(
                    "/api/file?path=" + Uri.EscapeDataString(".knit/kb/docs/widget.md"), opts);
                Assert.NotNull(md);
                Assert.Equal("text/markdown", md!.Mime);
                Assert.Contains("Widget", md.Content);

                // .. escape rejected as 400 invalid_path.
                var escape = await http.GetAsync("/api/file?path=" + Uri.EscapeDataString("../" + Path.GetFileName(outside)));
                Assert.Equal(HttpStatusCode.BadRequest, escape.StatusCode);

                // Absolute path rejected.
                var abs = await http.GetAsync("/api/file?path=" + Uri.EscapeDataString(outside));
                Assert.Equal(HttpStatusCode.BadRequest, abs.StatusCode);

                // Missing file → 404.
                var missing = await http.GetAsync("/api/file?path=src/Missing.cs");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

                // Missing path param → 400.
                var noPath = await http.GetAsync("/api/file");
                Assert.Equal(HttpStatusCode.BadRequest, noPath.StatusCode);

                // Oversized file returns metadata with truncated=true and empty body.
                var big = await http.GetFromJsonAsync<FileViewDto>("/api/file?path=src/big.txt", opts);
                Assert.NotNull(big);
                Assert.True(big!.Truncated);
                Assert.Equal("", big.Content);
                Assert.True(big.SizeBytes > FileEndpoints.MaxBytes);

                // Binary file returns octet-stream mime and empty body.
                var bin = await http.GetFromJsonAsync<FileViewDto>("/api/file?path=src/blob.bin", opts);
                Assert.NotNull(bin);
                Assert.Equal("application/octet-stream", bin!.Mime);
                Assert.Equal("", bin.Content);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
                try { File.Delete(outside); } catch { }
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task File_endpoint_strips_utf8_bom_from_text_content()
    {
        var root = CreateRepo();
        try
        {
            var bomFile = Path.Combine(root, "bom.txt");
            var payload = new List<byte> { 0xEF, 0xBB, 0xBF };
            payload.AddRange(Encoding.UTF8.GetBytes("hello\n"));
            File.WriteAllBytes(bomFile, payload.ToArray());

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
                var dto = await http.GetFromJsonAsync<FileViewDto>("/api/file?path=bom.txt", opts);
                Assert.NotNull(dto);
                Assert.Equal("hello\n", dto!.Content);
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
    public async Task Raw_endpoint_serves_listed_types_with_nosniff_and_refuses_the_rest()
    {
        var root = CreateRepo();
        var outside = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}");
        try
        {
            var docs = Path.Combine(root, ".knit", "kb", "docs");
            Directory.CreateDirectory(docs);
            File.WriteAllBytes(Path.Combine(docs, "shot.png"), [0x89, 0x50, 0x4E, 0x47]);
            File.WriteAllText(Path.Combine(docs, "spec.pdf"), "%PDF-1.4");
            File.WriteAllText(Path.Combine(docs, "page.html"), "<script>alert(1)</script>");
            File.WriteAllText(Path.Combine(docs, "flow.svg"), "<svg/>");
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(outside, "secret.png"), [1]);
            var linked = TryLink(Path.Combine(root, "linked"), outside);

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
                async Task<HttpResponseMessage> Get(string path) =>
                    await http.GetAsync("/api/raw?path=" + Uri.EscapeDataString(path));

                foreach (var (path, type) in new[] { (".knit/kb/docs/shot.png", "image/png"), (".knit/kb/docs/spec.pdf", "application/pdf") })
                {
                    var r = await Get(path);
                    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                    Assert.Equal(type, r.Content.Headers.ContentType?.MediaType);
                    Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
                }
                Assert.Equal([0x89, 0x50, 0x4E, 0x47], await (await Get(".knit/kb/docs/shot.png")).Content.ReadAsByteArrayAsync());

                foreach (var path in new[] { ".knit/kb/docs/page.html", ".knit/kb/docs/flow.svg" })
                {
                    var r = await Get(path);
                    Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                    Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
                }
                Assert.Equal(HttpStatusCode.BadRequest, (await Get("../" + Path.GetFileName(outside) + "/secret.png")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await Get(".knit/kb/docs/missing.png")).StatusCode);
                if (linked) Assert.Equal(HttpStatusCode.BadRequest, (await Get("linked/secret.png")).StatusCode);
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
            TryDelete(outside);
        }
    }

    // A directory symlink needs a privilege Windows may not grant; the escape
    // check runs where it can be set up.
    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string CreateRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-file-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });
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
