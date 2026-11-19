namespace Knit.Core.Domain;

/// <summary>
/// Project health signal. Valid only on items of
/// type <see cref="ItemType.Project"/> — <c>verify</c> flags it anywhere else.
/// Unset (<c>null</c> on <see cref="Item.Health"/>) means "no signal", which
/// is distinct from <see cref="OnTrack"/>: a project nobody assessed yet is
/// not the same as one assessed healthy.
/// </summary>
public enum Health { OnTrack, AtRisk, OffTrack }

public static class Healths
{
    public static string ToWire(this Health h) => h switch
    {
        Health.OnTrack => "on-track",
        Health.AtRisk => "at-risk",
        Health.OffTrack => "off-track",
        _ => throw new ArgumentOutOfRangeException(nameof(h), h, "unknown health"),
    };

    /// <summary>
    /// Strict parse for CLI / API input. Empty → <c>null</c> (the clear case),
    /// returning <c>true</c>. An unknown non-empty value returns <c>false</c>
    /// so the caller can reject it loudly.
    /// </summary>
    public static bool TryParse(string? raw, out Health? value)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "": value = null; return true;
            case "on-track": value = Health.OnTrack; return true;
            case "at-risk": value = Health.AtRisk; return true;
            case "off-track": value = Health.OffTrack; return true;
            default: value = null; return false;
        }
    }

    /// <summary>
    /// Lenient parse of an on-disk <c>health:</c> value. Unknown or empty
    /// degrades to <c>null</c> (never throws) — matches "unset ⇒ no signal".
    /// </summary>
    public static Health? ParseLenient(string? raw) =>
        TryParse(raw, out var h) ? h : null;
}
