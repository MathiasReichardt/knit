namespace Knit.Core.Domain;

/// <summary>
/// Item priority on a low→high scale. Ordinal-mapped for sorting/threshold
/// (<c>someday=0, normal=1, important=2, critical=3</c> — the enum order).
/// <c>Normal</c> is the working default: an item with no <c>priority:</c>
/// frontmatter sorts and filters exactly as <c>Normal</c>, and the field is
/// only written when it differs from <c>Normal</c> (keeps frontmatter clean).
/// </summary>
public enum Priority { Someday, Normal, Important, Critical }

public static class Priorities
{
    public const Priority Default = Priority.Normal;

    public static string ToWire(this Priority p) => p switch
    {
        Priority.Someday => "someday",
        Priority.Normal => "normal",
        Priority.Important => "important",
        Priority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, "unknown priority"),
    };

    /// <summary>
    /// Strict parse for CLI / API input. Empty or <c>normal</c> → <c>Normal</c>
    /// (the reset case), returning <c>true</c>. An unknown non-empty value
    /// returns <c>false</c> so the caller can reject it loudly.
    /// </summary>
    public static bool TryParse(string? raw, out Priority value)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "" or "normal": value = Priority.Normal; return true;
            case "someday": value = Priority.Someday; return true;
            case "important": value = Priority.Important; return true;
            case "critical": value = Priority.Critical; return true;
            default: value = Priority.Normal; return false;
        }
    }

    /// <summary>
    /// Lenient parse of an on-disk <c>priority:</c> value. Unknown or empty
    /// degrades to <c>Normal</c> (never throws) — matches "unset ⇒ normal".
    /// A malformed value is a doctor/verify warning candidate, not a read
    /// failure.
    /// </summary>
    public static Priority ParseLenient(string? raw) =>
        TryParse(raw, out var p) ? p : Priority.Normal;
}
