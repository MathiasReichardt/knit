using System.Security.Cryptography;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.IO;

namespace Knit.Data;

/// <summary>
/// The parsed item files of a long-running host, validated against the files
/// on every <see cref="Current"/>: the folder is listed and only files whose
/// mtime or size moved since they were parsed are read again. A missed watcher
/// event costs a late push, never wrong data. The files stay the only store.
/// </summary>
public sealed class ItemCache
{
    // An edit in the same mtime tick that keeps the size looks unchanged
    // (git's "racy git"); an entry this close to its file's mtime is re-read
    // until it isn't. Two seconds covers FAT's tick.
    private static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    private readonly string _itemsDir;
    private readonly ItemFile _itemFile;
    private readonly Func<string, byte[]> _readBytes;
    private readonly Lock _gate = new();
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private ItemSnapshot? _last;

    /// <param name="readBytes">Reads a file; tests pass one that edits the file mid-read.</param>
    public ItemCache(string root, IFrontmatterSerializer? yaml = null, Func<string, byte[]>? readBytes = null)
    {
        _itemsDir = RepoDiscovery.ItemsDir(root);
        _itemFile = new ItemFile(yaml ?? new SimpleFrontmatterSerializer());
        _readBytes = readBytes ?? AtomicFile.ReadAllBytes;
    }

    /// <summary>One parsed file. <see cref="Item"/> is null when it failed to parse, with the reason in <see cref="Error"/>.</summary>
    public sealed record Entry(string Path, DateTime MTime, long Size, DateTime CheckedAt, byte[] Hash, Item? Item, string? Error)
    {
        internal bool Matches(FileInfo f) =>
            f.LastWriteTimeUtc == MTime && f.Length == Size && CheckedAt - MTime >= RacyWindow;
    }

    /// <summary>The items as the files hold them now.</summary>
    public ItemSnapshot Current()
    {
        lock (_gate)
        {
            var files = Directory.Exists(_itemsDir)
                ? new DirectoryInfo(_itemsDir).EnumerateFiles("*.md", SearchOption.TopDirectoryOnly)
                : [];
            var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var listed = new List<Entry>();
            var changed = false;
            foreach (var file in files)
            {
                if (_entries.TryGetValue(file.FullName, out var known) && known.Matches(file))
                {
                    next[known.Path] = known;
                    listed.Add(known);
                    continue;
                }
                changed = true;
                var (entry, stable) = Load(file);
                if (entry is null) continue;
                if (stable) next[entry.Path] = entry;
                listed.Add(entry);
            }
            if (!changed && _last is not null && next.Count == _entries.Count) return _last;
            _entries = next;
            return _last = new ItemSnapshot(listed);
        }
    }

    /// <summary>Forget every entry; the next <see cref="Current"/> parses every file.</summary>
    public void Drop()
    {
        lock (_gate)
        {
            _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            _last = null;
        }
    }

    /// <summary>
    /// Read every file regardless of its stat and replace the entries whose
    /// bytes differ from the file. Returns the replaced paths: any is a cache
    /// that went stale behind its own check.
    /// </summary>
    public IReadOnlyList<string> Heal()
    {
        lock (_gate)
        {
            var stale = new List<string>();
            foreach (var entry in _entries.Values.ToList())
            {
                var file = new FileInfo(entry.Path);
                if (!file.Exists) continue;
                var (fresh, stable) = Load(file);
                if (fresh is null || fresh.Hash.AsSpan().SequenceEqual(entry.Hash)) continue;
                stale.Add(entry.Path);
                if (stable) _entries[entry.Path] = fresh;
                else _entries.Remove(entry.Path);
            }
            if (stale.Count > 0) _last = null;
            return stale;
        }
    }

    // Stat before read: the entry keeps the stat taken before the bytes, and
    // isn't kept when a second stat differs — otherwise old bytes would pair
    // with the new mtime and no later check would catch it.
    private (Entry? Entry, bool Stable) Load(FileInfo file)
    {
        var checkedAt = DateTime.UtcNow;
        var (mtime, size) = (file.LastWriteTimeUtc, file.Length);
        byte[] bytes;
        try { bytes = _readBytes(file.FullName); }
        catch (FileNotFoundException) { return (null, false); }
        catch (DirectoryNotFoundException) { return (null, false); }

        Item? item = null;
        string? error = null;
        try { item = _itemFile.Read(AtomicFile.Decode(bytes)); }
        catch (Exception ex) { error = ex.Message; }
        if (item is null)
        {
            error ??= "produced no item (empty or missing frontmatter)";
            Console.Error.WriteLine($"warning: failed to parse {file.FullName}: {error}");
        }

        file.Refresh();
        var stable = file.Exists && file.LastWriteTimeUtc == mtime && file.Length == size;
        return (new Entry(file.FullName, mtime, size, checkedAt, SHA256.HashData(bytes), item, error), stable);
    }
}

/// <summary>
/// One immutable view of the items, in folder order: a request reads one
/// snapshot throughout, never half an update. The link graph is built once
/// per snapshot, from memory.
/// </summary>
public sealed class ItemSnapshot
{
    private readonly Dictionary<string, ItemCache.Entry> _byPath;
    private readonly Dictionary<string, ItemCache.Entry> _byId;
    private readonly Lazy<LinkGraph> _graph;

    internal ItemSnapshot(IReadOnlyList<ItemCache.Entry> entries)
    {
        Entries = entries;
        Items = entries.Where(e => e.Item is not null).Select(e => e.Item!).ToList();
        _byPath = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
        _byId = new Dictionary<string, ItemCache.Entry>(StringComparer.Ordinal);
        foreach (var e in entries)
            if (e.Item is not null) _byId.TryAdd(e.Item.Id, e);
        _graph = new Lazy<LinkGraph>(() => new LinkGraph(Items));
    }

    public IReadOnlyList<ItemCache.Entry> Entries { get; }
    public IReadOnlyList<Item> Items { get; }
    public LinkGraph Graph => _graph.Value;

    public ItemCache.Entry? ByPath(string path) => _byPath.GetValueOrDefault(Path.GetFullPath(path));
    public ItemCache.Entry? ById(string id) => _byId.GetValueOrDefault(id);
}
