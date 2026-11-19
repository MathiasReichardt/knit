namespace Knit.Core.Domain;

/// <summary>
/// Due-date proximity classification for emphasis and filtering. Computed
/// relative to a caller-supplied "today" and the <c>due_soon_days</c>
/// window so the CLI and web agree on "soon". Closed items
/// (<see cref="ItemState.Done"/> / <see cref="ItemState.Dismissed"/>) are
/// never overdue or due-soon — finished work isn't actionable.
/// </summary>
public enum DueStatus { None, Overdue, DueSoon, Future }

public static class DueDates
{
    public static DueStatus Status(DateOnly? due, ItemState state, DateOnly today, int dueSoonDays)
    {
        if (due is not { } d) return DueStatus.None;
        if (state is ItemState.Done or ItemState.Dismissed) return DueStatus.None;
        if (d < today) return DueStatus.Overdue;
        if (d <= today.AddDays(Math.Max(0, dueSoonDays))) return DueStatus.DueSoon;
        return DueStatus.Future;
    }

    /// <summary>Lenient parse of a frontmatter <c>due:</c> value. Returns
    /// <c>null</c> for empty or malformed input (never throws) — matches the
    /// "degrade to no due, never fail the item" rule. Strict callers (CLI
    /// input validation) should use <see cref="TryParse"/>.</summary>
    public static DateOnly? ParseLenient(string? raw) =>
        TryParse(raw, out var d) ? d : null;

    public static bool TryParse(string? raw, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return DateOnly.TryParseExact(
            raw.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value);
    }

    public static string ToWire(DateOnly due) =>
        due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
