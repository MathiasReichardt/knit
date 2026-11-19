namespace Knit.WebApi.Contracts;

/// <summary>
/// Backs the SPA summary strip. Counts only — the strip's job is the numbers
/// you can't get by looking at the tree; anything that needs a *list* (which
/// items are blocked) is a filter click, not a payload.
/// </summary>
public sealed record StatusDto(
    string Scope,
    int Total,
    int Done,
    int InProgress,
    int Review,
    int Todo,
    int Dismissed,
    int Blocked,
    int Overdue,
    int Unassigned,
    int Percent,
    string? Health);

/// <summary>
/// The status page the SPA menu wrote. <see cref="Path"/> is relative to the
/// knit root, so the toast names it the way the repo does.
/// </summary>
public sealed record StatusReportDto(string Path, bool Written);

/// <summary>
/// The status page's state for the SPA's stale dot and its view.
/// <see cref="Exists"/> is false for a missing page and for a file knit did
/// not write; every hint and entry point keys off it.
/// </summary>
public sealed record StatusPageDto(string Path, bool Exists, string? Generated, bool Stale);
