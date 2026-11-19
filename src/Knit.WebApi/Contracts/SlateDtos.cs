namespace Knit.WebApi.Contracts;

/// <summary>
/// One slate as returned by <c>GET /api/slates</c>. Registry rows come
/// first in table order; slate values found only on items (no registry
/// row — hand-typed or post-<c>rm</c> leftovers) are appended
/// with <see cref="InRegistry"/> false so the assign dropdown can union
/// them without legitimising the row.
/// </summary>
public sealed record SlateDto(
    string Name,
    string Status,
    string? Notes,
    int ItemCount,
    bool InRegistry);

/// <summary>
/// Request body for <c>POST /api/slates</c>. <see cref="Name"/> is
/// slugified server-side; <see cref="Status"/> defaults to
/// <c>planned</c>.
/// </summary>
public sealed record SlateCreateRequestDto(
    string Name,
    string? Status,
    string? Notes);

/// <summary>
/// Request body for <c>PATCH /api/slates/{name}</c>. Every field is
/// optional — only fields the client sends are applied.
/// <see cref="Rename"/> triggers the item-walk the CLI's
/// <c>slate rename</c> does; <see cref="Before"/> / <see cref="After"/>
/// (exactly one) reorder the row relative to the named anchor.
/// A <c>""</c> <see cref="Notes"/> clears the cell.
/// </summary>
public sealed record SlatePatchRequestDto(
    string? Rename,
    string? Status,
    string? Notes,
    string? Before,
    string? After);

/// <summary>
/// Response body for <c>PATCH /api/slates/{name}</c>.
/// <see cref="ItemsUpdated"/> is non-zero only for a rename.
/// </summary>
public sealed record SlatePatchResultDto(SlateDto Slate, int ItemsUpdated);

/// <summary>
/// Response body for <c>DELETE /api/slates/{name}</c>: the registry row
/// goes and <c>slate:</c> is cleared from every item that carried it.
/// </summary>
public sealed record SlateDeleteResultDto(string Name, int ItemsCleared);
