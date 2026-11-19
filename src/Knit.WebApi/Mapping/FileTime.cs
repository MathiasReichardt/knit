using System.Globalization;

namespace Knit.WebApi.Mapping;

/// <summary>
/// A file's last-write time as an ISO-8601 UTC string. Backs the landing
/// cards' "edited ~2 h ago" labels (11.x); mtime is the recency signal.
/// </summary>
public static class FileTime
{
    public static string ModifiedIso(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path)
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }
}
