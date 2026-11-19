namespace Knit.Core.Config;

/// <summary>
/// On-disk **format** version of a knit repo — bumps only when the storage
/// contract changes (frontmatter shape, folder rules, index format), never per
/// release. Stamped into <c>.knit/config.json</c> so a future breaking change
/// has a key to migrate from; there is no migration engine yet (by design —
/// the format is stable M1a→M3, so this stays 1 for the whole M-series).
/// </summary>
public static class FormatVersion
{
    public const int Current = 1;

    /// <summary>True when a repo was written by a knit that knows a format this binary doesn't.</summary>
    public static bool IsFuture(int repoVersion) => repoVersion > Current;

    public static string UpgradeMessage(int repoVersion) =>
        $"this repo uses knit format_version {repoVersion}, this binary understands {Current} — upgrade knit";
}
