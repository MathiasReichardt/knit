using System.Collections.Concurrent;
using System.Threading.Channels;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Refs;
using Knit.Data;
using Knit.WebApi.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Knit.WebApi.Services;

/// <summary>
/// In-memory, session-only ring buffer of recent changes (1c.7). Subscribes
/// to <see cref="FileWatcher"/> events and classifies each into an
/// <see cref="ActivityEntryDto"/> by diffing the affected item against a
/// per-session snapshot (so a "changed" event becomes a state change vs a
/// comment vs a generic edit). SSE handlers fan out new entries via
/// <see cref="Subscribe"/>; <c>/api/activity</c> serves the current buffer.
/// </summary>
public sealed class ActivityBuffer : BackgroundService
{
    private const int Capacity = 500;

    private readonly string _root;
    private readonly FileWatcher? _watcher;
    private readonly ILogger<ActivityBuffer> _logger;

    private readonly object _gate = new();
    private readonly Queue<ActivityEntryDto> _entries = new();
    private readonly Dictionary<string, ItemSnapshot> _items = new();
    private readonly ConcurrentDictionary<Guid, Channel<ActivityEntryDto>> _subscribers = new();
    private long _seq;

    public ActivityBuffer(WebApiOptions options, ILogger<ActivityBuffer> logger, FileWatcher? watcher)
    {
        _root = options.RootPath;
        _logger = logger;
        _watcher = watcher;
    }

    private readonly record struct ItemSnapshot(ItemState State, int CommentCount, string Title);

    /// <summary>Current buffer contents, newest first.</summary>
    public IReadOnlyList<ActivityEntryDto> Snapshot()
    {
        lock (_gate) return _entries.Reverse().ToArray();
    }

    public IDisposable Subscribe(out ChannelReader<ActivityEntryDto> reader)
    {
        var channel = Channel.CreateUnbounded<ActivityEntryDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        reader = channel.Reader;
        return new Subscription(this, id);
    }

    private sealed class Subscription(ActivityBuffer owner, Guid id) : IDisposable
    {
        public void Dispose()
        {
            if (owner._subscribers.TryRemove(id, out var ch))
                ch.Writer.TryComplete();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (_watcher is null)
        {
            _logger.LogInformation("FileWatcher absent; ActivityBuffer is inert");
            return;
        }

        // Prime item snapshots so the first observed change classifies
        // correctly (state vs comment vs edit) instead of falling back to
        // a generic "edited". Existing files produce no activity rows.
        PrimeItemSnapshots();

        using var sub = _watcher.Subscribe(out var reader);
        try
        {
            await foreach (var evt in reader.ReadAllAsync(stopping))
            {
                // Missed events left the snapshots stale; re-prime so later edits classify against the files.
                if (evt == FileWatcher.Resync) { PrimeItemSnapshots(); continue; }
                try { Record(evt); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ActivityBuffer: failed to record {Kind}/{Id} {Op}", evt.Kind, evt.Id, evt.Op);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public void PrimeItemSnapshots()
    {
        try
        {
            var store = new ItemStore(_root, new SimpleFrontmatterSerializer());
            foreach (var item in store.AllItems())
                _items[item.Id] = new ItemSnapshot(item.State, item.Comments.Count, item.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ActivityBuffer: snapshot prime failed; first edits may be classified generically");
        }
    }

    /// <summary>
    /// Classify and store one watcher event. Returns the entry that was
    /// appended, or null when the event isn't surfaced (designs, config,
    /// generated KB indexes).
    /// </summary>
    public ActivityEntryDto? Record(EventDto evt)
    {
        var entry = evt.Kind switch
        {
            "item" => ClassifyItem(evt),
            "kb" => ClassifyKb(evt),
            _ => null, // designs / config: not in the feed
        };
        if (entry is null) return null;

        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        foreach (var ch in _subscribers.Values)
            ch.Writer.TryWrite(entry);
        return entry;
    }

    private ActivityEntryDto? ClassifyItem(EventDto evt)
    {
        if (evt.Op == "deleted")
        {
            var title = _items.TryGetValue(evt.Id, out var prev) ? prev.Title : evt.Id;
            _items.Remove(evt.Id);
            return Make("other", "item", evt.Id, title, actor: null, from: null, to: null, op: "deleted");
        }

        var store = new ItemStore(_root, new SimpleFrontmatterSerializer());
        var item = store.Read(evt.Id);
        if (item is null)
        {
            // File vanished between event and read — treat as a delete.
            _items.Remove(evt.Id);
            return null;
        }

        var snap = new ItemSnapshot(item.State, item.Comments.Count, item.Title);
        var had = _items.TryGetValue(evt.Id, out var before);
        _items[evt.Id] = snap;

        if (evt.Op == "created" || !had)
            return Make("created", "item", item.Id, item.Title, item.Assignee, from: null, to: null, op: "created");

        if (before.State != item.State)
            return Make("state", "item", item.Id, item.Title, item.Assignee,
                from: before.State.ToWire(), to: item.State.ToWire(), op: "changed");

        if (item.Comments.Count > before.CommentCount)
        {
            var author = item.Comments.Count > 0 ? item.Comments[^1].Author : item.Assignee;
            return Make("comment", "item", item.Id, item.Title, author, from: null, to: null, op: "changed");
        }

        return Make("other", "item", item.Id, item.Title, item.Assignee, from: null, to: null, op: "changed");
    }

    private ActivityEntryDto? ClassifyKb(EventDto evt)
    {
        // Watcher KB id is "<folder>/<tail-without-.md>"; translate the
        // folder back to a bucket and drop generated bucket indexes.
        var parts = evt.Id.Split('/');
        if (parts.Length < 2) return null;
        if (!BucketFolders.TryFromFolder(parts[0], out var bucket)) return null;

        var tail = string.Join('/', parts.Skip(1));
        if (tail.EndsWith("/readme", StringComparison.OrdinalIgnoreCase))
            tail = tail[..^"/readme".Length];
        if (string.Equals(Path.GetFileName(tail), "index", StringComparison.OrdinalIgnoreCase))
            return null;

        var refStr = new Ref(bucket, tail).ToString();
        return Make("kb", "kb", refStr, refStr, actor: null, from: null, to: null, op: evt.Op);
    }

    private ActivityEntryDto Make(
        string category, string kind, string id, string title,
        string? actor, string? from, string? to, string op)
    {
        var seq = Interlocked.Increment(ref _seq);
        return new ActivityEntryDto(
            seq, DateTimeOffset.UtcNow.ToString("o"), category, kind, id, title, actor, from, to, op);
    }
}
