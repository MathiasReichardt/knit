namespace Knit.Core.Ids;

/// <summary>
/// Mints identifiers for Knit entities (items + comments). Delegates to
/// <see cref="NanoId"/> — 18-char uppercase-digit ids, case-insensitive by
/// construction, no coordinator needed for concurrent/offline minting.
/// </summary>
public static class IdGenerator
{
    public static string New() => NanoId.New();

    public static bool IsValid(string id) => NanoId.IsValid(id);
}
