using Knit.Core.Discovery;
using Knit.Core.Frontmatter;
using Knit.Core.Ids;

namespace Knit.Data;

/// <summary>
/// Shortest-unique-prefix id renderer (git-style), with a short floor.
/// Sourced from <see cref="ItemLayout"/>.
/// </summary>
public sealed class IdShortener
{
    // NanoIDs are uniformly random (no shared time-prefix like UUIDv7), so
    // short prefixes collide far less; a 4-char floor is comfortable at
    // tracker scale while the constructor still grows it on real collisions.
    public const int Floor = 4;

    private readonly Dictionary<string, string> _shortByFull;

    // Sorted, an id shares its longest prefix with a neighbor, so the shortest
    // unique prefix is one past that: n log n where a check against every id
    // was quadratic (0.4 s at 10k items).
    private IdShortener(List<string> ids)
    {
        ids.Sort(StringComparer.Ordinal);
        _shortByFull = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var shared = Math.Max(
                i > 0 ? CommonPrefix(id, ids[i - 1]) : 0,
                i < ids.Count - 1 ? CommonPrefix(id, ids[i + 1]) : 0);
            var n = Math.Max(Floor, shared + 1);
            _shortByFull[id] = n < id.Length ? id[..n] : id;
        }
    }

    public static IdShortener FromItemsDir(string root)
    {
        var ids = new List<string>();
        var itemsDir = RepoDiscovery.ItemsDir(root);
        foreach (var file in ItemLayout.EnumerateFiles(itemsDir))
        {
            var name = ItemLayout.EntryName(file);
            var idPart = LinkPath.IdFromFolderName(name);
            if (idPart is not null && NanoId.IsValid(idPart))
                ids.Add(idPart.ToLowerInvariant());
        }
        return new IdShortener(ids);
    }

    public string Short(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        var lower = id.ToLowerInvariant();
        if (_shortByFull.TryGetValue(lower, out var s)) return s;
        return lower.Length >= Floor ? lower[..Floor] : lower;
    }

    private static int CommonPrefix(string a, string b) => a.AsSpan().CommonPrefixLength(b);
}
