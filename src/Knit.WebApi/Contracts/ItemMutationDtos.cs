namespace Knit.WebApi.Contracts;

/// <summary>
/// Request body for <c>POST /api/items</c>. Mirrors <c>knit add</c>:
/// <paramref name="Type"/> and <paramref name="Title"/> are required;
/// <paramref name="Parent"/> may be null for a root item;
/// <paramref name="Template"/> picks a non-default template variant
/// (same lookup as the CLI <c>--template</c> flag).
/// </summary>
public sealed record NewItemRequestDto(
    string Type,
    string Title,
    string? Parent,
    string? Template);

/// <summary>
/// Request body for <c>PATCH /api/items/{id}</c>. Every field is
/// optional — only fields the client sends are applied. Strings that
/// roundtrip <c>null</c> mean "no change"; a string clear must be
/// expressed by sending an empty <c>""</c>. <see cref="Parent"/> change
/// validates the hierarchy. Type changes go through
/// <c>POST /api/items/{id}/retype</c>; order changes through
/// <c>POST /api/items/reorder</c> — <c>rank</c> is not patchable, because
/// clients never construct a rank string.
/// </summary>
public sealed record ItemPatchRequestDto(
    string? State = null,
    string? Assignee = null,
    string? Title = null,
    string? Effort = null,
    string? Parent = null,
    IReadOnlyList<string>? AgentSkills = null,
    string? Blocked = null,
    string? Body = null,
    // Date-only YYYY-MM-DD. null = no change, "" = clear, a valid date sets;
    // a malformed value is a 400 (unlike the lenient on-disk read).
    string? Due = null,
    // someday|normal|important|critical. null = no change; "" or "normal"
    // resets to the Normal default; an unknown value is a 400.
    string? Priority = null,
    // Free-form tags; null = no change, [] clears. Normalized on write via
    // the KB Tags chokepoint.
    IReadOnlyList<string>? Tags = null,
    // Slate name (slugified server-side). null = no change, "" = clear.
    // Not validated against the registry — the dropdown offers the union
    // incl. item-only values, and dangling refs stay legal. Kept last
    // so positional callers stay stable.
    string? Slate = null,
    // Project-only health signal: on-track|at-risk|off-track. null = no
    // change, "" = clear; an unknown value or a non-project item is a 400
    // (mirrors the CLI `set health=`). Kept last for positional stability.
    string? Health = null);

/// <summary>
/// Request body for <c>POST /api/items/{id}/block</c>. At least one of
/// <see cref="By"/> or <see cref="Reason"/> must be set; if
/// <see cref="Reason"/> is omitted the server auto-fills from the
/// <see cref="By"/> target (matches CLI ergonomics).
/// </summary>
public sealed record BlockRequestDto(string? By, string? Reason);

/// <summary>
/// Request body for <c>POST /api/items/{id}/dismiss</c>. When
/// <see cref="Cascade"/> is true the server dismisses every open
/// descendant and returns <see cref="DismissResultDto"/>; otherwise it
/// returns just the parent's updated <see cref="ItemDto"/>.
/// </summary>
public sealed record DismissRequestDto(bool? Cascade);

public sealed record DismissResultDto(ItemDto Item, IReadOnlyList<ItemDto> Cascaded);

/// <summary>
/// Response body for <c>DELETE /api/items/{id}?cascade=true</c>. Returns
/// only the ids that were removed — the items themselves no longer exist
/// so a full DTO would be misleading.
/// </summary>
public sealed record DeleteResultDto(string Id, IReadOnlyList<string> Cascaded);

/// <summary>
/// Request body for <c>POST /api/items/{id}/retype</c>. Atomic
/// type-change + optional re-parent. <see cref="To"/> is one of
/// <c>epic|feature|bug|task</c>. <see cref="Parent"/> follows the
/// PATCH convention: <c>null</c> = keep current parent, empty string
/// = detach to root, non-empty = set/move under target. The endpoint
/// validates hierarchy on both edges and refuses to orphan existing
/// children.
/// </summary>
public sealed record RetypeRequestDto(string To, string? Parent);

/// <summary>
/// Failure body for retype-specific errors. Reuses
/// <see cref="ProblemDto"/>'s shape and adds an
/// <see cref="OffendingChildIds"/> list populated when the failure
/// is <c>would_orphan_children</c>.
/// </summary>
public sealed record RetypeProblemDto(
    string Title,
    int Status,
    string? Detail,
    string? Code,
    IReadOnlyList<string> OffendingChildIds);

/// <summary>
/// Request body for <c>POST /api/items/{id}/reorder</c>. Moves the
/// item among its same-parent siblings (top-level items share one
/// "root" group). Position is one of: <c>top</c>, <c>bottom</c>,
/// <c>up</c>, <c>down</c>. The server computes the new fractional
/// rank from the neighbours — clients never construct a rank string.
/// </summary>
public sealed record ReorderRequestDto(string Position);

/// <summary>
/// Request body for <c>POST /api/items/reorder</c> — place one or more items
/// as siblings under <see cref="Parent"/>, in the order given, computing every
/// rank server-side. A drag is one gesture, so the whole set moves in one call
/// or not at all; per-row <see cref="ReorderRowDto.IfMatch"/> keeps the
/// optimistic-concurrency contract of single-item PATCH.
/// <see cref="Parent"/> is authoritative — null or <c>""</c> means the root
/// group — so a placement re-parents as well as reorders.
/// <see cref="After"/> / <see cref="Before"/> name the sibling the set lands
/// after / before; pass at most one, and neither to append to the end of the
/// group. One neighbour is enough: the server reads the other side from the
/// group's own order rather than trusting the client's view of it.
/// </summary>
public sealed record ReorderItemsRequestDto(
    IReadOnlyList<ReorderRowDto> Items,
    string? Parent,
    string? Before = null,
    string? After = null);

public sealed record ReorderRowDto(string Id, string IfMatch);

public sealed record ReorderItemsResultDto(IReadOnlyList<ItemDto> Items);

/// <summary>
/// <c>POST /api/items/{id}/apply-sort</c> and <c>/api/items/apply-sort</c>: the
/// items whose rank changes (or would, on a dry run). <c>Total</c> counts every
/// item in scope, <c>Groups</c> the sibling groups with a move.
/// </summary>
public sealed record ApplySortResultDto(bool DryRun, int Moved, int Total, int Groups, IReadOnlyList<string> MovedIds);

/// <summary>
/// Request body for <c>POST /api/items/{id}/links</c>.
/// <see cref="Rel"/> is one of <c>parent | blocks | blocked-by |
/// related | kb-ref</c>. <see cref="Target"/> is an item id (any
/// unambiguous form) for item rels or a <c>bucket:slug</c> KB ref
/// for <c>kb-ref</c>. <see cref="Notes"/> is optional.
/// </summary>
public sealed record LinkRequestDto(string Rel, string Target, string? Notes);

/// <summary>
/// Request body for <c>PATCH /api/items/{id}/links</c>: the row as read
/// (<see cref="Rel"/>, <see cref="TargetPath"/>, <see cref="TargetText"/> —
/// exact match), its new <see cref="Notes"/> (empty clears) and optionally a
/// <see cref="NewRel"/>, which only moves between <c>blocks | blocked-by | related</c>.
/// </summary>
public sealed record LinkEditRequestDto(string Rel, string TargetPath, string TargetText, string? Notes, string? NewRel);

/// <summary>
/// Request body for <c>POST /api/items/{id}/comments</c>. Identity
/// (author) is stamped by the server from <c>config.json</c> — the
/// client cannot impersonate. <see cref="Body"/> is required and
/// must be non-empty.
/// </summary>
public sealed record CommentRequestDto(string Body);

/// <summary>
/// Request body for <c>PATCH /api/items/{id}/comments/{commentId}</c>.
/// Edits the comment body in place; the identity and original
/// created timestamp are preserved.
/// </summary>
public sealed record CommentPatchDto(string Body);

/// <summary>
/// Response body for <c>POST /api/items/{id}/comments</c>.
/// Lifts the new comment id alongside the parent item, matching the
/// CLI's <c>--json</c> shape so SPA and CLI mutators are observationally
/// identical.
/// </summary>
public sealed record CommentMutationResultDto(ItemDto Item, CommentDto Comment);

/// <summary>
/// RFC 9457 problem details payload — the error envelope every
/// mutator endpoint returns on failure.
/// </summary>
public sealed record ProblemDto(
    string Title,
    int Status,
    string? Detail = null,
    string? Code = null);
