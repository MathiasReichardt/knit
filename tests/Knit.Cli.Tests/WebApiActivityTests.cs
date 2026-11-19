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

public class WebApiActivityTests
{
    [Fact]
    public void Classifies_create_state_comment_and_delete_for_items()
    {
        var (root, id) = CreateRepoWithItem();
        try
        {
            var store = new ItemStore(root, new SimpleFrontmatterSerializer());
            var buffer = new ActivityBuffer(new WebApiOptions { RootPath = root }, NullLogger<ActivityBuffer>.Instance, watcher: null);

            // Snapshot the current (todo, no comments) state first.
            buffer.PrimeItemSnapshots();

            // A create event (even on an already-primed item) reads as created.
            var created = buffer.Record(new EventDto("item", id, "created"));
            Assert.Equal("created", created!.Category);

            // State change: todo -> in-progress.
            var item = store.Read(id)!;
            store.Write(item with { State = ItemState.InProgress });
            var stateChange = buffer.Record(new EventDto("item", id, "changed"));
            Assert.Equal("state", stateChange!.Category);
            Assert.Equal("todo", stateChange.FromState);
            Assert.Equal("in-progress", stateChange.ToState);

            // Comment added.
            item = store.Read(id)!;
            store.Write(item with
            {
                Comments = [new Comment("c1", "bob", DateTimeOffset.UtcNow, "looks good")],
            });
            var comment = buffer.Record(new EventDto("item", id, "changed"));
            Assert.Equal("comment", comment!.Category);
            Assert.Equal("bob", comment.Actor);

            // Plain body edit with no state/comment delta -> other.
            item = store.Read(id)!;
            store.Write(item with { Body = item.Body + "\nmore\n" });
            var edit = buffer.Record(new EventDto("item", id, "changed"));
            Assert.Equal("other", edit!.Category);

            // Delete.
            var deleted = buffer.Record(new EventDto("item", id, "deleted"));
            Assert.Equal("other", deleted!.Category);
            Assert.Equal("deleted", deleted.Op);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Kb_events_translate_folder_to_bucket_ref_and_skip_indexes()
    {
        var buffer = new ActivityBuffer(
            new WebApiOptions { RootPath = Path.GetTempPath() }, NullLogger<ActivityBuffer>.Instance, watcher: null);

        var entry = buffer.Record(new EventDto("kb", "adrs/0002-foo", "changed"));
        Assert.NotNull(entry);
        Assert.Equal("kb", entry!.Category);
        Assert.Equal("adr:0002-foo", entry.Id);

        // Grow-to-folder readme collapses to the bare slug.
        var folder = buffer.Record(new EventDto("kb", "features/bar/readme", "changed"));
        Assert.Equal("feature:bar", folder!.Id);

        // Generated bucket index is not surfaced.
        Assert.Null(buffer.Record(new EventDto("kb", "adrs/index", "changed")));
    }

    [Fact]
    public void Buffer_caps_at_500_and_returns_newest_first()
    {
        var buffer = new ActivityBuffer(
            new WebApiOptions { RootPath = Path.GetTempPath() }, NullLogger<ActivityBuffer>.Instance, watcher: null);

        for (var i = 0; i < 600; i++)
            buffer.Record(new EventDto("kb", $"docs/doc-{i}", "changed"));

        var snap = buffer.Snapshot();
        Assert.Equal(500, snap.Count);
        // Newest first: the last recorded entry has the highest seq.
        Assert.True(snap[0].Seq > snap[^1].Seq);
    }

    [Fact]
    public async Task Activity_endpoint_returns_empty_when_watch_disabled()
    {
        var (root, _) = CreateRepoWithItem();
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
                var entries = await http.GetFromJsonAsync<List<ActivityEntryDto>>("/api/activity", opts);
                Assert.NotNull(entries);
                Assert.Empty(entries!);
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
    public async Task Events_stream_drains_on_shutdown_without_blocking()
    {
        var (root, _) = CreateRepoWithItem();
        try
        {
            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = true,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                // Open the SSE stream and read the initial ": connected" frame so the
                // handler is actively pumping when we shut down. The response is left
                // open (not disposed) across StopAsync so the request is genuinely
                // in-flight — only ApplicationStopping can unblock it.
                using var resp = await http.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync();
                var buf = new byte[64];
                Assert.True(await stream.ReadAsync(buf) > 0);

                // With the request-abort token linked to ApplicationStopping, StopAsync
                // drains the open SSE request promptly. Without the link it blocks on
                // the ~30s shutdown timeout, so a 10s ceiling proves the fix.
                var stop = app.StopAsync();
                var done = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.Same(stop, done);
                await stop;
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
        finally { TryDelete(root); }
    }

    private static (string Root, string ItemId) CreateRepoWithItem()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-activity-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var itemId = "01968000-0000-7000-8000-0000000000bb";
        var store = new ItemStore(root, new SimpleFrontmatterSerializer());
        store.Write(new Item
        {
            Id = itemId,
            Title = "Sample",
            Type = ItemType.Task,
            State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow,
            Body = "# Sample\n\nbody\n",
        });
        return (root, itemId);
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
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
