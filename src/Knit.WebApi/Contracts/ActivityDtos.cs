namespace Knit.WebApi.Contracts;

/// <summary>
/// One row in the activity feed (1c.7). Derived from a file-watcher event
/// by <c>ActivityBuffer</c>. Session-only — the buffer starts empty on each
/// <c>knit web</c> run.
/// </summary>
/// <param name="Seq">Session-monotonic sequence number; ordering + client key.</param>
/// <param name="Ts">ISO-8601 UTC timestamp of when the change was observed.</param>
/// <param name="Category">
/// Filter bucket the SPA chips key off: <c>created | state | comment | kb |
/// other</c>.
/// </param>
/// <param name="Kind"><c>item</c> or <c>kb</c>.</param>
/// <param name="Id">
/// Item uuid (kind <c>item</c>) or <c>bucket:slug</c> ref (kind <c>kb</c>) —
/// the deep-link target.
/// </param>
/// <param name="Title">Display label for the link.</param>
/// <param name="Actor">Assignee (state/create) or comment author, when known.</param>
/// <param name="FromState">Prior state for a state change, else null.</param>
/// <param name="ToState">New state for a state change, else null.</param>
/// <param name="Op">Raw file op: <c>created | changed | deleted</c>.</param>
public sealed record ActivityEntryDto(
    long Seq,
    string Ts,
    string Category,
    string Kind,
    string Id,
    string Title,
    string? Actor,
    string? FromState,
    string? ToState,
    string Op);
