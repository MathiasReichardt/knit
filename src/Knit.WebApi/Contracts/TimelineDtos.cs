namespace Knit.WebApi.Contracts;

/// <summary>
/// One node of the deadline timeline (<c>GET /api/timeline</c>). Ghost nodes
/// are out-of-filter at-risk blockers kept in the payload so the graph never
/// silently drops the at-risk signal — the SPA renders them dimmed.
/// </summary>
public sealed record TimelineNodeDto(
    string Id,
    string ShortId,
    string Title,
    string Type,
    string State,
    string? Due,
    string Priority,
    string? Effort,
    string? Assignee,
    string? Parent,
    string? Blocked,
    string? Milestone,
    string? ParentPath,
    bool AtRisk,
    string? ImpliedDeadline,
    IReadOnlyList<string> AtRiskBlockers,
    string? SpanStart,
    string? SpanEnd,
    bool SubtreeAtRisk,
    IReadOnlyList<TimelineRolledDto> RolledUp,
    IReadOnlyList<string> AncestorIds,
    IReadOnlyList<TimelineRemainingDto> Remaining,
    int OpenBelow,
    bool Ghost);

/// <summary>
/// One row of the selection panel's remaining work — a direct child of
/// the node, or one of its open blockers. <c>OpenBelow</c> / <c>UndatedBelow</c>
/// count open descendants of the row itself, so one level of rows can report a
/// whole subtree without listing it.
/// </summary>
public sealed record TimelineRemainingDto(
    string Id,
    string ShortId,
    string Title,
    string Type,
    string State,
    string? Due,
    int OpenBelow,
    int UndatedBelow,
    bool Blocker);

/// <summary>
/// A descendant folded into a coarser timeline node — listed in the
/// details panel so a rollup anchor's hidden work is visible without a click.
/// </summary>
public sealed record TimelineRolledDto(
    string Id,
    string Title,
    string Type,
    string? Due);

/// <summary>Blocker edge: <c>From</c> blocks <c>To</c>.</summary>
public sealed record TimelineEdgeDto(string From, string To, bool AtRisk);

/// <summary>
/// A dated milestone for the timeline overlay — rendered as a
/// vertical guide + flag at its target. Only milestones with a target
/// appear; the <c>milestone</c> query filter narrows the set to one.
/// </summary>
public sealed record TimelineMilestoneDto(
    string Project,
    string Slug,
    string Title,
    string Target,
    string State);

public sealed record TimelineDto(
    IReadOnlyList<TimelineNodeDto> Nodes,
    IReadOnlyList<TimelineEdgeDto> Edges,
    IReadOnlyList<TimelineMilestoneDto> Milestones);
