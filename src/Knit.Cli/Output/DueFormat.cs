using Knit.Core.Domain;

namespace Knit.Cli.Output;

/// <summary>
/// Monochrome-safe due-date rendering for <c>list</c> / <c>tree</c>. Overdue
/// gets a <c>⚠</c> glyph + red; due-soon amber; future/undated plain. Colors
/// are Spectre markup — auto-stripped on <c>--no-color</c> / <c>NO_COLOR</c> /
/// redirect / <c>--json</c> via the shared ANSI wiring, so the glyph and text
/// still read correctly with color off.
/// </summary>
public static class DueFormat
{
    /// <summary>Markup for a table cell / inline label. Empty string when the
    /// item has no due date.</summary>
    public static string Cell(DateOnly? due, ItemState state, DateOnly today, int dueSoonDays)
    {
        if (due is not { } d) return string.Empty;
        var text = DueDates.ToWire(d);
        return DueDates.Status(due, state, today, dueSoonDays) switch
        {
            DueStatus.Overdue => $"[red]⚠ {text}[/]",
            DueStatus.DueSoon => $"[yellow]{text}[/]",
            _ => text,
        };
    }
}
