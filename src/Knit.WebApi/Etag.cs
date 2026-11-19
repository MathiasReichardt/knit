using System.IO.Hashing;
using Knit.Data;

namespace Knit.WebApi;

/// <summary>
/// Strong ETag over a file's raw bytes. The file is the store, so any edit —
/// through the API, the CLI or by hand — changes the etag; a hash over parsed
/// fields misses whatever the projection leaves out, and a stale etag makes a
/// revalidating GET answer 304 with the pre-edit document.
/// </summary>
public static class Etag
{
    /// <summary>Etag of the file at <paramref name="path"/>; <c>"0"</c> when it doesn't exist.</summary>
    public static string OfFile(string? path)
    {
        if (path is null || !File.Exists(path)) return "\"0\"";
        var hash = XxHash64.HashToUInt64(File.ReadAllBytes(path));
        return $"\"{hash:x16}\"";
    }

    public static string OfItem(ItemStore store, string id) => OfFile(store.FindItemFile(id));
}
