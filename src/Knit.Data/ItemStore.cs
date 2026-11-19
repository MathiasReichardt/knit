using System.Security.Cryptography;
using System.Text;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;
using Knit.Core.IO;

namespace Knit.Data;

/// <summary>
/// Reads and writes item entries under <c>.knit/items/</c>. Layout logic
/// (enumeration, name extraction) lives in
/// <see cref="ItemLayout"/> — this class is a thin IO wrapper.
/// <para>
/// Writes are guarded against a same-item collision: the store keeps a
/// hash of the bytes it first read per item, and <see cref="Write"/> re-reads
/// before the rename. On a mismatch it re-applies this writer's change once
/// via <see cref="ItemMerge"/>, or refuses. A TOCTOU check, not atomicity — a
/// window remains between the re-check and the rename; it catches agent-scale
/// races, not interleaved microseconds.
/// </para>
/// </summary>
public sealed class ItemStore
{
    private readonly string _itemsDir;
    private readonly ItemFile _itemFile;
    private readonly Dictionary<string, (byte[] Hash, Item Basis)> _read = new(StringComparer.Ordinal);
    private readonly ItemCache? _cache;
    // Every snapshot this store served, oldest first: a write reconciles against
    // the version it first handed out, as with _read.
    private readonly List<ItemSnapshot> _served = [];
    private ItemSnapshot? _pinned;

    public ItemStore(string root, IFrontmatterSerializer? yaml = null, ItemCache? cache = null)
    {
        _itemsDir = RepoDiscovery.ItemsDir(root);
        _itemFile = new ItemFile(yaml ?? new SimpleFrontmatterSerializer());
        _cache = cache;
    }

    public string ItemsDir => _itemsDir;

    public IEnumerable<string> AllItemFiles() => ItemLayout.EnumerateFiles(_itemsDir);

    // Without a cache the files are read once per store until it writes, like
    // the pinned snapshot: a CLI call or verify that asks repeatedly scans once.
    private Scanned? _scanned;

    private sealed record Scanned(List<Item> Items, Dictionary<string, (Item Item, string Path)> ById);

    public IReadOnlyList<Item> AllItems() => _cache is null ? Scan().Items : Snapshot().Items;

    /// <summary>
    /// The file of an item <see cref="AllItems"/> returned, from the same read:
    /// a lookup per item through <see cref="FindItemFile"/> lists the folder each time.
    /// </summary>
    public string? PathOf(string id) =>
        _cache is null ? Scan().ById.GetValueOrDefault(id).Path : Snapshot().ById(id)?.Path;

    /// <summary>Drop the read-once item list: files changed behind this store, or a live view repaints.</summary>
    public void Forget() => _scanned = null;

    /// <summary>The link graph over <see cref="AllItems"/>; built once per cache snapshot.</summary>
    public LinkGraph Graph() => _cache is null ? new LinkGraph(AllItems()) : Snapshot().Graph;

    // One snapshot per store until it writes, so a request sees one state.
    private ItemSnapshot Snapshot()
    {
        if (_pinned is not null) return _pinned;
        _pinned = _cache!.Current();
        if (!_served.Contains(_pinned)) _served.Add(_pinned);
        return _pinned;
    }

    private Scanned Scan()
    {
        if (_scanned is not null) return _scanned;
        var items = new List<Item>();
        var byId = new Dictionary<string, (Item, string)>(StringComparer.Ordinal);
        var failures = new List<(string Path, string Reason)>();
        foreach (var file in AllItemFiles())
        {
            Item? parsed = null;
            string? reason = null;
            try { parsed = Load(file); }
            catch (Exception ex) { reason = ex.Message; }
            if (parsed is null)
            {
                failures.Add((file, reason ?? "produced no item (empty or missing frontmatter)"));
                continue;
            }
            items.Add(parsed);
            byId.TryAdd(parsed.Id, (parsed, file));
        }
        if (failures.Count > 0)
        {
            foreach (var (path, reason) in failures)
                Console.Error.WriteLine($"warning: failed to parse {path}: {reason}");
            Console.Error.WriteLine(
                $"warning: {failures.Count} item file(s) failed to parse; run 'knit verify' to see which");
        }
        return _scanned = new Scanned(items, byId);
    }

    public string? FindItemFile(string idOrPrefix) => ItemLayout.FindFile(_itemsDir, idOrPrefix);

    /// <summary>
    /// Mint a fresh item id, regenerating on the astronomically-rare chance
    /// that an item folder with that id already exists. Cheap insurance for
    /// coordinator-free concurrent minting; no migration/lock needed.
    /// </summary>
    public string NewItemId()
    {
        string id;
        do { id = IdGenerator.New(); }
        while (FindItemFile(id) is not null);
        return id;
    }

    public Item? Read(string idOrPrefix)
    {
        // A full id the read-once scan already holds: a lookup per finding or
        // row must not list the folder each time.
        if (_scanned?.ById.TryGetValue(idOrPrefix, out var known) == true) return known.Item;
        var file = FindItemFile(idOrPrefix);
        if (file is null) return null;
        if (_cache is not null && Snapshot().ByPath(file) is { } cached) return cached.Item;
        try { return Load(file); }
        catch { return null; }
    }

    private Item Load(string file)
    {
        var bytes = AtomicFile.ReadAllBytes(file);
        var item = Parse(bytes);
        _read.TryAdd(item.Id, (SHA256.HashData(bytes), item));
        return item;
    }

    private Item Parse(byte[] bytes) => _itemFile.Read(AtomicFile.Decode(bytes));

    /// <summary>
    /// Persist <paramref name="item"/>. Returns the absolute file path.
    /// </summary>
    /// <remarks>
    /// The file name is fixed at creation: a retitle never moves the file, so
    /// incoming relative links can't go stale. <c>knit move</c> renames explicitly.
    /// </remarks>
    /// <exception cref="WriteRefusedException">
    /// Another writer changed the item's body since this store read it, or
    /// changed the item again while this write was re-applied.
    /// </exception>
    public string Write(Item item)
    {
        var existing = FindItemFile(item.Id);
        if (existing is not null) item = Reconcile(existing, item);
        var filePath = existing ?? ItemLayout.FlatFilePath(_itemsDir, item.Id, Slug.From(item.Title));
        Directory.CreateDirectory(_itemsDir);
        var content = Encoding.UTF8.GetBytes(_itemFile.Write(item));
        AtomicFile.WriteAllBytes(filePath, content);
        // Parsed back, so a later merge compares like with like.
        _read[item.Id] = (SHA256.HashData(content), Parse(content));
        _pinned = null;
        _scanned = null;
        return filePath;
    }

    private Item Reconcile(string file, Item mine)
    {
        if (!_read.TryGetValue(mine.Id, out var read) && !TryServed(mine.Id, out read)) return mine;
        var bytes = AtomicFile.ReadAllBytes(file);
        var hash = SHA256.HashData(bytes);
        if (hash.AsSpan().SequenceEqual(read.Hash)) return mine;

        Item theirs;
        try { theirs = Parse(bytes); }
        catch (Exception ex)
        {
            throw new WriteRefusedException(
                $"{file} changed since this command read it and no longer parses ({ex.Message}) — nothing written; run 'knit verify'", ex);
        }
        var merged = ItemMerge.ThreeWay(read.Basis, mine, theirs)
                     ?? throw new WriteRefusedException(
                         $"{file} changed since this command read it: another writer edited its body too — nothing written; re-run");
        if (!SHA256.HashData(AtomicFile.ReadAllBytes(file)).AsSpan().SequenceEqual(hash))
            throw new WriteRefusedException(
                $"{file} kept changing while this command re-applied its change — nothing written; re-run");
        return merged;
    }

    private bool TryServed(string id, out (byte[] Hash, Item Basis) read)
    {
        foreach (var snapshot in _served)
            if (snapshot.ById(id) is { Item: { } item } entry)
            {
                read = (entry.Hash, item);
                return true;
            }
        read = default;
        return false;
    }

    /// <summary>
    /// Remove an item's file. Files beside it stay (<c>knit verify</c> reports
    /// them as stray). Returns true when something was deleted.
    /// Caller is responsible for cleaning up back-references from other items.
    /// </summary>
    public bool Delete(string idOrPrefix)
    {
        var file = FindItemFile(idOrPrefix);
        if (file is null) return false;
        File.Delete(file);
        _pinned = null;
        _scanned = null;
        return true;
    }
}
