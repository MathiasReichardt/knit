using System.Runtime.CompilerServices;

namespace Knit.Core.Discovery;

/// <summary>
/// One source of truth for the items-folder layout. Items are flat files,
/// <c>&lt;id-slug&gt;.md</c>, directly under <c>.knit/items/</c>; a folder
/// there is an invalid item (<c>knit verify</c>). Every caller that walks
/// <c>.knit/items/</c> or resolves an entry goes through here.
/// </summary>
public static class ItemLayout
{
    /// <summary>
    /// Enumerate every item file. Returns nothing when
    /// <paramref name="itemsDir"/> doesn't exist yet.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string itemsDir)
    {
        if (ListingProbe.Value is { } count) count.Value++;
        return Directory.Exists(itemsDir)
            ? Directory.EnumerateFiles(itemsDir, "*.md", SearchOption.TopDirectoryOnly)
            : [];
    }

    private static readonly AsyncLocal<StrongBox<int>?> ListingProbe = new();

    /// <summary>
    /// Test probe: counts <see cref="EnumerateFiles"/> calls from here on in
    /// the caller's flow, so a test can catch a folder listing per item.
    /// </summary>
    public static StrongBox<int> CountListings() => ListingProbe.Value = new StrongBox<int>();

    /// <summary>
    /// Resolve an id (or unique prefix) to its file path. Returns null
    /// when missing OR when the prefix is ambiguous.
    /// </summary>
    public static string? FindFile(string itemsDir, string idOrPrefix)
    {
        if (string.IsNullOrEmpty(idOrPrefix)) return null;
        var prefix = idOrPrefix.ToLowerInvariant();
        string? match = null;
        foreach (var file in EnumerateFiles(itemsDir))
        {
            if (EntryName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (match is not null) return null;
                match = file;
            }
        }
        return match;
    }

    /// <summary>The stable identity-name of an item file: <c>&lt;id-slug&gt;</c>, no extension.</summary>
    public static string EntryName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
    }

    /// <summary>
    /// Build the destination file path for a new flat entry with the
    /// given id and slug.
    /// </summary>
    public static string FlatFilePath(string itemsDir, string id, string slug) =>
        Path.Combine(itemsDir, $"{id}-{slug}.md");
}
