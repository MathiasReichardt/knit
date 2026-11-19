using System.Collections.Concurrent;
using System.Threading.Channels;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Frontmatter;
using Knit.Core.IO;
using Knit.Core.Paths;
using Knit.Core.Refs;
using Knit.Data;
using Knit.WebApi.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Knit.WebApi.Services;

/// <summary>
/// Watches <c>.knit/</c> (excluding <c>.cache/</c>) and emits
/// burst-windowed <see cref="EventDto"/>s. SSE handlers subscribe via
/// <see cref="Subscribe"/>; the watcher fans out to every active reader.
/// Also watches the knit repo's git <c>HEAD</c>, so the header's branch
/// follows a <c>git checkout</c> without the SPA polling for it, and the
/// user's conventions file beside the user-level config.
/// </summary>
public sealed class FileWatcher : IHostedService, IDisposable
{
    private readonly string _root;
    private readonly string? _gitDir;
    private readonly ILogger<FileWatcher> _logger;
    private readonly ItemCache? _cache;
    private FileSystemWatcher? _fsw;
    private FileSystemWatcher? _headFsw;
    private FileSystemWatcher? _userFsw;
    private readonly object _gate = new();
    private readonly Dictionary<(string Kind, string Id), string> _pending = new();
    private System.Threading.Timer? _flush;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromMilliseconds(200);
    private readonly ConcurrentDictionary<Guid, Channel<EventDto>> _subscribers = new();

    public static readonly EventDto Resync = new("resync", "all", "resync");

    public FileWatcher(WebApiOptions options, ILogger<FileWatcher> logger)
    {
        _root = options.RootPath;
        _gitDir = GitHead.CheckoutFor(options.RootPath, options.GitRoot) is { } checkout ? GitWorktree.GitDir(checkout) : null;
        _logger = logger;
        _cache = options.ItemCache;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var knitDir = RepoDiscovery.KnitDir(_root);
        if (!Directory.Exists(knitDir))
        {
            _logger.LogWarning("FileWatcher: .knit/ missing at {Path}, watcher disabled", knitDir);
            return Task.CompletedTask;
        }

        _fsw = new FileSystemWatcher(knitDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            // The maximum: a git checkout or a burst of CLI writes overflows the 8 KB default.
            InternalBufferSize = 64 * 1024,
        };
        _fsw.Error += (_, e) => OnWatcherError(e.GetException());
        _fsw.Created += (_, e) => OnFsEvent("created", e.FullPath);
        _fsw.Changed += (_, e) => OnFsEvent("changed", e.FullPath);
        _fsw.Deleted += (_, e) => OnFsEvent("deleted", e.FullPath);
        _fsw.Renamed += (_, e) =>
        {
            // An atomic write renames its temp over the target: a change, not a delete + create.
            if (AtomicFile.IsTemp(e.OldFullPath))
            {
                OnFsEvent("changed", e.FullPath);
                return;
            }
            OnFsEvent("deleted", e.OldFullPath);
            OnFsEvent("created", e.FullPath);
        };
        _fsw.EnableRaisingEvents = true;
        _logger.LogInformation("FileWatcher watching {Path}", knitDir);
        WatchHead();
        WatchUserConventions();
        return Task.CompletedTask;
    }

    // Absent until someone writes it; the SPA's own write refreshes the view
    // without this, so a folder created later goes unwatched until a restart.
    private void WatchUserConventions()
    {
        var dir = UserPaths.ConfigDir;
        if (!Directory.Exists(dir)) return;
        _userFsw = new FileSystemWatcher(dir)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        FileSystemEventHandler onChange = (_, e) => OnUserEvent(e.FullPath);
        _userFsw.Created += onChange;
        _userFsw.Changed += onChange;
        _userFsw.Deleted += onChange;
        _userFsw.Renamed += (_, e) => OnUserEvent(e.FullPath);
        _userFsw.EnableRaisingEvents = true;
    }

    private void OnUserEvent(string fullPath)
    {
        if (Path.GetFileName(fullPath) == Conventions.FileName) Enqueue("conventions", "user", "changed");
    }

    // The git directory, not the file: git writes HEAD.lock and renames it
    // over HEAD, which a watch on HEAD itself would miss.
    private void WatchHead()
    {
        if (_gitDir is null || !Directory.Exists(_gitDir)) return;
        _headFsw = new FileSystemWatcher(_gitDir)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        FileSystemEventHandler onChange = (_, e) => OnHeadEvent(e.FullPath);
        _headFsw.Created += onChange;
        _headFsw.Changed += onChange;
        _headFsw.Renamed += (_, e) => OnHeadEvent(e.FullPath);
        _headFsw.EnableRaisingEvents = true;
    }

    private void OnHeadEvent(string fullPath)
    {
        if (Path.GetFileName(fullPath) == "HEAD") Enqueue("git", "head", "changed");
    }

    public Task StopAsync(CancellationToken ct)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _fsw?.Dispose();
        _fsw = null;
        _headFsw?.Dispose();
        _headFsw = null;
        _userFsw?.Dispose();
        _userFsw = null;
        _flush?.Dispose();
        _flush = null;
    }

    public IDisposable Subscribe(out ChannelReader<EventDto> reader)
    {
        var channel = Channel.CreateUnbounded<EventDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        reader = channel.Reader;
        return new Subscription(this, id);
    }

    private sealed class Subscription(FileWatcher owner, Guid id) : IDisposable
    {
        public void Dispose()
        {
            if (owner._subscribers.TryRemove(id, out var ch))
                ch.Writer.TryComplete();
        }
    }

    /// <summary>
    /// The watcher lost events (buffer overflow) or failed: subscribers can't
    /// know what changed, so they get one <see cref="Resync"/> event and refetch
    /// everything. Pending events are dropped; the resync covers them. The item
    /// cache is dropped too: a burst like a checkout is rebuilt once, from the files.
    /// </summary>
    public void OnWatcherError(Exception ex)
    {
        _logger.LogWarning(ex, "FileWatcher lost events; sending a resync");
        _cache?.Drop();
        lock (_gate) _pending.Clear();
        foreach (var sub in _subscribers.Values)
            sub.Writer.TryWrite(Resync);
    }

    private void OnFsEvent(string op, string fullPath)
    {
        if (Classify(fullPath) is { } classified) Enqueue(classified.Kind, classified.Id, op);
    }

    private void Enqueue(string kind, string id, string op)
    {
        var key = (kind, id);
        lock (_gate)
        {
            // Last-op-wins inside the burst window. created > changed; deleted overrides everything.
            if (_pending.TryGetValue(key, out var existing))
            {
                if (existing == "deleted") return;
                if (op == "deleted") _pending[key] = "deleted";
                else if (existing != "created") _pending[key] = op;
            }
            else _pending[key] = op;

            _flush ??= new System.Threading.Timer(_ => Flush(), null, BurstWindow, Timeout.InfiniteTimeSpan);
            _flush.Change(BurstWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        List<EventDto> events;
        lock (_gate)
        {
            events = _pending.Select(kv => new EventDto(kv.Key.Kind, kv.Key.Id, kv.Value)).ToList();
            _pending.Clear();
        }
        foreach (var evt in events)
            foreach (var sub in _subscribers.Values)
                sub.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Map a filesystem path under <c>.knit/</c> to a logical resource
    /// (<c>(kind, id)</c>). Returns null for paths inside
    /// <c>.cache/</c> or anything we don't surface yet.
    /// </summary>
    public (string Kind, string Id)? Classify(string fullPath)
    {
        var knitDir = RepoDiscovery.KnitDir(_root);
        var rel = Path.GetRelativePath(knitDir, fullPath).Replace('\\', '/');
        if (rel.StartsWith("..", StringComparison.Ordinal)) return null;
        if (AtomicFile.IsTemp(fullPath)) return null;
        if (rel.StartsWith(".cache/", StringComparison.Ordinal) || rel == ".cache") return null;

        if (rel.Equals("config.json", StringComparison.Ordinal))
            return ("config", "config");
        if (rel.Equals(Knit.Core.Slates.SlateRegistry.FileName, StringComparison.Ordinal))
            return ("slates", "slates");
        if (rel.Equals(Conventions.FileName, StringComparison.Ordinal))
            return ("conventions", "repo");

        var segs = rel.Split('/');
        if (segs.Length == 0) return null;

        if (segs[0].Equals(BucketFolders.Items, StringComparison.Ordinal) && segs.Length >= 2)
        {
            var id = LinkPath.IdFromFolderName(segs[1]);
            return id is null ? null : ("item", id);
        }
        if (segs[0].Equals("kb", StringComparison.Ordinal) && segs.Length >= 2)
        {
            // KB ids are bucket/slug; strip .md if present and join.
            var tail = string.Join('/', segs.Skip(1));
            if (tail.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) tail = tail[..^3];
            return ("kb", tail);
        }
        if (segs[0].Equals("designs", StringComparison.Ordinal) && segs.Length >= 2)
        {
            var tail = string.Join('/', segs.Skip(1));
            if (tail.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) tail = tail[..^3];
            return ("design", tail);
        }
        if (segs[0].Equals(Knit.Core.Projects.ProjectsLayout.ProjectsDirName, StringComparison.Ordinal) && segs.Length >= 2)
        {
            // Project doc ids are <project>/<relative-doc-path> (no .md).
            var tail = string.Join('/', segs.Skip(1));
            if (tail.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) tail = tail[..^3];
            return ("projects", tail);
        }
        return null;
    }
}
