using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiSearchTests
{
    [Fact]
    public async Task Search_indexes_items_kb_and_designs()
    {
        var root = CreateRepoWithContent();
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
                EnableSearch = true,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Live in-memory scan: no warm-up, results are available immediately.
                var itemHits = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=sample&kind=item", opts);
                Assert.NotNull(itemHits);
                Assert.False(itemHits!.Clipped);
                Assert.Contains(itemHits.Hits, h => h.Kind == "item" && h.Title == "Sample item");

                var kbHits = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=widget&kind=kb", opts);
                Assert.NotNull(kbHits);
                Assert.Contains(kbHits!.Hits, h => h.Kind == "kb" && h.Title == "Widget overview");

                var designHits = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=approach&kind=design", opts);
                Assert.NotNull(designHits);
                Assert.Contains(designHits!.Hits, h => h.Kind == "design");

                // Tag-only query: "persistence" appears only in the entry's
                // frontmatter tags, never in its title or body.
                var tagHits = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=persistence&kind=kb", opts);
                Assert.NotNull(tagHits);
                Assert.Contains(tagHits!.Hits, h => h.Kind == "kb" && h.Id == "doc:widget");

                // Cross-kind query.
                var all = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=widget", opts);
                Assert.NotNull(all);
                Assert.Contains(all!.Hits, h => h.Kind == "kb");

                // Mid-word substring hit: "ample" matches "Sample item"
                // even though it is not a word-start prefix, and the snippet is
                // plain text (no <mark>).
                var substr = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=ample&kind=item", opts);
                Assert.NotNull(substr);
                var sampleHit = Assert.Single(substr!.Hits, h => h.Title == "Sample item");
                Assert.DoesNotContain("<mark>", sampleHit.Snippet);

                // ref:bucket:slug shortcut returns the entry directly.
                var refHit = await http.GetFromJsonAsync<SearchResultDto>(
                    "/api/search?q=ref:doc:widget", opts);
                Assert.NotNull(refHit);
                Assert.Single(refHit!.Hits);
                Assert.Equal("kb", refHit.Hits[0].Kind);
                Assert.Equal("doc:widget", refHit.Hits[0].Id);

                // Empty query returns empty hits, not an error.
                var empty = await http.GetAsync("/api/search?q=");
                Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
                var emptyBody = await empty.Content.ReadFromJsonAsync<SearchResultDto>(opts);
                Assert.NotNull(emptyBody);
                Assert.Empty(emptyBody!.Hits);
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

    private static string CreateRepoWithContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-search-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = "01968000-0000-7000-8000-0000000000aa",
            Title = "Sample item",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Body = "this body mentions sample and widget keywords for FTS",
        });

        // KB entry under .knit/kb/docs/widget.md (folder is plural)
        var docDir = Path.Combine(knitDir, "kb", "docs");
        Directory.CreateDirectory(docDir);
        File.WriteAllText(Path.Combine(docDir, "widget.md"),
            "---\ntitle: Widget overview\ntags: [persistence, caching]\n---\n\n# Widget overview\n\nDescribes the widget subsystem.\n");

        // Design under .knit/designs/sample-design.md
        var designDir = Path.Combine(knitDir, "designs");
        Directory.CreateDirectory(designDir);
        File.WriteAllText(Path.Combine(designDir, "sample-design.md"),
            "# Sample design\n\nThis describes the approach in detail.\n");

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
