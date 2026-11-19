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
using Knit.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Knit.Cli.Tests;

/// <summary>
/// The server's <see cref="ItemCache"/>: validated against the
/// files on every read, so it answers like a full scan without events.
/// </summary>
public class ItemCacheTests : IDisposable
{
    private static readonly DateTime LongAgo = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"knit-cache-{Guid.NewGuid():N}");
    private readonly ItemStore _plain;

    public ItemCacheTests()
    {
        var knitDir = Path.Combine(_root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "ci", Display = "CI" });
        _plain = new ItemStore(_root, new SimpleFrontmatterSerializer());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Edit_add_delete_and_rename_are_picked_up_without_an_event()
    {
        var cache = new ItemCache(_root);
        var a = Add("Alpha");
        Assert.Equal(["Alpha"], Titles(cache));

        var b = Add("Bravo");
        Assert.Equal(["Alpha", "Bravo"], Titles(cache).Order());

        Edit(a, "Alpha", "Alphabet");
        Assert.Equal(["Alphabet", "Bravo"], Titles(cache).Order());

        var moved = Path.Combine(Path.GetDirectoryName(b)!, Path.GetFileName(b).Replace("bravo", "renamed"));
        File.Move(b, moved);
        Assert.Equal(["Alphabet", "Bravo"], Titles(cache).Order());
        Assert.NotNull(cache.Current().ByPath(moved));

        File.Delete(a);
        Assert.Equal(["Bravo"], Titles(cache));
    }

    [Fact]
    public void An_unchanged_folder_is_not_read_again()
    {
        var cache = new ItemCache(_root);
        Age(Add("Alpha"));

        Assert.Same(cache.Current(), cache.Current());
    }

    [Fact]
    public void A_cached_store_answers_like_the_plain_one_malformed_files_included()
    {
        var parent = Add("Parent");
        Add("Child", parent);
        Add("Other");
        File.WriteAllText(Path.Combine(_plain.ItemsDir, "notanid-thing.md"), "---\nbroken: [\n---\n");
        var cached = new ItemStore(_root, new SimpleFrontmatterSerializer(), new ItemCache(_root));

        Assert.Equal(
            _plain.AllItems().Select(i => (i.Id, i.Title, i.Parent)),
            cached.AllItems().Select(i => (i.Id, i.Title, i.Parent)));
        var parentId = _plain.AllItems().Single(i => i.Title == "Parent").Id;
        Assert.Equal(
            new LinkGraph(_plain.AllItems()).ChildrenOf(parentId),
            cached.Graph().ChildrenOf(parentId));
    }

    [Fact]
    public void A_store_reads_one_snapshot_until_it_writes()
    {
        var a = Add("Alpha");
        var store = new ItemStore(_root, new SimpleFrontmatterSerializer(), new ItemCache(_root));
        var first = store.AllItems();

        Edit(a, "Alpha", "Alphabet");

        Assert.Same(first, store.AllItems());
        store.Write(store.AllItems().Single() with { State = ItemState.Done });
        Assert.Equal("Alphabet", Assert.Single(store.AllItems()).Title);
    }

    [Fact]
    public async Task Concurrent_readers_and_a_writer_each_see_whole_snapshots()
    {
        for (var i = 0; i < 20; i++) Add($"Item {i:00}");
        var cache = new ItemCache(_root);
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            var n = 0;
            while (!stop.IsCancellationRequested) Add($"Late {n++:000}");
        });

        Parallel.For(0, 200, _ =>
        {
            var snapshot = cache.Current();
            Assert.Equal(snapshot.Entries.Count, snapshot.Items.Count);
        });
        stop.Cancel();
        await writer;
    }

    [Fact]
    public void A_write_through_the_cache_still_merges_a_concurrent_edit()
    {
        var a = Add("Alpha");
        var store = new ItemStore(_root, new SimpleFrontmatterSerializer(), new ItemCache(_root));
        var mine = store.Read(Id(a))!;

        File.WriteAllText(a, File.ReadAllText(a).Replace("# x", "# x\n\ntheirs"));
        store.Write(mine with { State = ItemState.Done });

        var merged = _plain.Read(Id(a))!;
        Assert.Equal(ItemState.Done, merged.State);
        Assert.Contains("theirs", merged.Body);
    }

    [Fact]
    public void An_edit_in_the_snapshots_mtime_tick_with_the_same_size_is_picked_up()
    {
        var a = Add("Alpha");
        var cache = new ItemCache(_root);
        Assert.Equal(["Alpha"], Titles(cache));
        var mtime = File.GetLastWriteTimeUtc(a);

        File.WriteAllText(a, File.ReadAllText(a).Replace("Alpha", "Bravo"));
        File.SetLastWriteTimeUtc(a, mtime);

        Assert.Equal(["Bravo"], Titles(cache));
    }

    [Fact]
    public void A_file_changed_while_it_is_read_is_read_again()
    {
        var a = Add("Alpha");
        Age(a);
        var reads = 0;
        var cache = new ItemCache(_root, readBytes: path =>
        {
            var bytes = File.ReadAllBytes(path);
            if (reads++ == 0) Edit(path, "Alpha", "Bravo");
            return bytes;
        });

        Assert.Equal(["Alpha"], Titles(cache));
        Assert.Equal(["Bravo"], Titles(cache));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Heal_replaces_an_entry_made_stale_behind_its_back()
    {
        var a = Add("Alpha");
        Age(a);
        var cache = new ItemCache(_root);
        cache.Current();

        SameStatEdit(a, "Alpha", "Bravo");
        Assert.Equal(["Alpha"], Titles(cache));

        Assert.Equal([Path.GetFullPath(a)], cache.Heal());
        Assert.Equal(["Bravo"], Titles(cache));
        Assert.Empty(cache.Heal());
    }

    [Fact]
    public void A_watcher_resync_drops_the_cache()
    {
        var a = Add("Alpha");
        Age(a);
        var options = new WebApiOptions { RootPath = _root, ItemCache = new ItemCache(_root) };
        options.ItemCache!.Current();
        SameStatEdit(a, "Alpha", "Bravo");
        using var watcher = new FileWatcher(options, NullLogger<FileWatcher>.Instance);

        watcher.OnWatcherError(new InternalBufferOverflowException());

        Assert.Equal(["Bravo"], Titles(options.ItemCache));
    }

    [Fact]
    public async Task Doctor_verify_reads_the_files_and_heals_the_cache()
    {
        var a = Add("Alpha");
        Age(a);
        await WithApi(async (http, options) =>
        {
            options.ItemCache!.Current();

            // Same size and mtime, but no longer an item: only a file read can tell.
            var text = File.ReadAllText(a);
            File.WriteAllText(a, "xxx" + text[3..]);
            Age(a);
            Assert.Equal(["Alpha"], Titles(options.ItemCache!));

            var verify = await http.GetFromJsonAsync<VerifyResultDto>("/api/doctor/verify", Web);

            Assert.Single(verify!.InvalidItems);
            Assert.DoesNotContain("Alpha", Titles(options.ItemCache!));
        });
    }

    [Fact]
    public async Task Clearing_the_cache_rereads_the_files()
    {
        var a = Add("Alpha");
        Age(a);
        await WithApi(async (http, options) =>
        {
            options.ItemCache!.Current();
            SameStatEdit(a, "Alpha", "Bravo");

            var r = await http.DeleteAsync("/api/cache");

            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
            Assert.Equal(["Bravo"], Titles(options.ItemCache));
        });
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_store_without_cache_lists_the_files_once_until_it_writes_or_forgets()
    {
        var store = new ItemStore(_root, new SimpleFrontmatterSerializer());
        var a = Add("Alpha");
        var listings = ItemLayout.CountListings();

        Assert.Same(store.AllItems(), store.AllItems());
        Assert.Equal(1, listings.Value);

        store.Write(store.AllItems().Single() with { Title = "Alphabet" });
        Assert.Equal(["Alphabet"], store.AllItems().Select(i => i.Title));

        Edit(a, "Alphabet", "Behind");
        Assert.Equal(["Alphabet"], store.AllItems().Select(i => i.Title));
        store.Forget();
        Assert.Equal(["Behind"], store.AllItems().Select(i => i.Title));
    }

    private static List<string> Titles(ItemCache cache) => cache.Current().Items.Select(i => i.Title).ToList();

    private string Add(string title, string? parentPath = null)
    {
        var id = _plain.NewItemId();
        LinkRow[] links = parentPath is null
            ? []
            : [new LinkRow(LinkRel.Parent, "parent", Path.GetFileName(parentPath), null)];
        return _plain.Write(new Item
        {
            Id = id, Title = title, Type = ItemType.Task, State = ItemState.Todo,
            Created = DateTimeOffset.UtcNow, Body = "# x\n", LinkRows = links,
        });
    }

    private static string Id(string path) => Path.GetFileName(path)[..18];

    private static void Edit(string path, string from, string to) =>
        File.WriteAllText(path, File.ReadAllText(path).Replace(from, to));

    // Out of the racy window, so only a changed stat makes the cache read it again.
    private static void Age(string path) => File.SetLastWriteTimeUtc(path, LongAgo);

    // A content change the stat can't see: same size, mtime put back.
    private static void SameStatEdit(string path, string from, string to)
    {
        Assert.Equal(from.Length, to.Length);
        Edit(path, from, to);
        Age(path);
    }

    private async Task WithApi(Func<HttpClient, WebApiOptions, Task> body)
    {
        var port = GetFreePort();
        var options = new WebApiOptions
        {
            RootPath = _root,
            Port = port,
            OpenBrowser = false,
            EnableStaticFiles = false,
            EnableWatch = false,
            EnableSearch = false,
        };
        var app = WebApiBuilder.Build([], options);
        await app.StartAsync();
        try
        {
            using var http = TestApi.Client(port);
            await body(http, options);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
