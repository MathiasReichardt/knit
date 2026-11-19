using Knit.Core.Discovery;

namespace Knit.Cli.Tests;

/// <summary>
/// Test helpers that look up item files through <see cref="ItemLayout"/>
/// rather than touching the filesystem layout directly. Keeps the
/// flat-vs-folder distinction out of every test case.
/// </summary>
public static class ItemFixtures
{
    private static string ItemsDir(TempKnitRoot root) =>
        Path.Combine(root.Path, ".knit", "items");

    /// <summary>First item file under <c>.knit/items/</c>, in enumeration order.</summary>
    public static string FirstItemFile(TempKnitRoot root) =>
        ItemLayout.EnumerateFiles(ItemsDir(root)).First();

    /// <summary>All item files under <c>.knit/items/</c>.</summary>
    public static IReadOnlyList<string> AllItemFiles(TempKnitRoot root) =>
        ItemLayout.EnumerateFiles(ItemsDir(root)).ToList();

    /// <summary>The entry name (<c>&lt;id-slug&gt;</c>) for an item file.</summary>
    public static string EntryName(string file) => ItemLayout.EntryName(file);

    /// <summary>Extract the id from an entry name — NanoIDs have no internal
    /// hyphens, so the id is everything before the first <c>-</c>.</summary>
    public static string IdFromEntry(string entryName)
    {
        var dash = entryName.IndexOf('-');
        return dash < 0 ? entryName : entryName[..dash];
    }

    /// <summary>Convenience: id of the first item.</summary>
    public static string FirstItemId(TempKnitRoot root) =>
        IdFromEntry(EntryName(FirstItemFile(root)));
}
