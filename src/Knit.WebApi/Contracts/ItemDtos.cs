using System.Text.Json.Serialization;

namespace Knit.WebApi.Contracts;

/// <summary>
/// Lean projection of an item used in lists and tree nodes. Excludes
/// body / comments / link rows — fetch the full <see cref="ItemDto"/>
/// when those are needed.
/// </summary>
public sealed record ItemSummaryDto(
    string Id,
    string ShortId,
    string Title,
    string Type,
    string State,
    string? Assignee,
    string? Parent,
    string? Rank,
    string? Effort,
    string? Due,
    string Priority,
    DateTimeOffset Created,
    bool Blocked,
    string? BlockedReason,
    bool HasChildren,
    IReadOnlyList<string> Tags,
    string FilePath,
    // File mtime as ISO-8601, populated only by the landing feed so
    // its cards can show "edited ~2 h ago". Omitted from tree/list/ready rows.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Modified = null,
    // Own slate: value only — inherited slates are a server-side filter
    // concern. Kept last so positional callers stay stable.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Slate = null,
    // Project-only signals, mirrored from the full ItemDto so the
    // SPA Projects nav can root on project items + link them to their doc
    // folder without a second fetch. Health = on-track|at-risk|off-track
    // (null = no signal); Project = the doc-folder slug pointer. Omitted when
    // null so non-project rows stay lean.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Health = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Project = null,
    // The blocked ancestor holding this item; omitted when nothing does.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    HeldByDto? HeldBy = null);

/// <summary>
/// The nearest open ancestor carrying a block. It holds the item and its
/// whole subtree (not ready) without writing anything to them.
/// </summary>
public sealed record HeldByDto(string Id, string ShortId, string Title);

/// <summary>
/// Full item payload returned by <c>GET /api/items/{id}</c>. Frontmatter,
/// h2-keyed body sections (per <see cref="Knit.Core.Frontmatter.BodySections"/>),
/// link rows, and comments. Etag is carried in the response header, not
/// the body.
/// </summary>
public sealed record ItemDto(
    string Id,
    string ShortId,
    string Title,
    string Type,
    string State,
    string? Assignee,
    string? Parent,
    string? Rank,
    string? Effort,
    string? Due,
    string Priority,
    DateTimeOffset Created,
    DateTimeOffset Modified,
    bool Blocked,
    string? BlockedReason,
    string? Summary,
    IReadOnlyList<string> Feature,
    IReadOnlyList<string> AgentSkills,
    IReadOnlyList<string> Tags,
    string? Design,
    string Body,
    IReadOnlyList<BodySectionDto> Sections,
    IReadOnlyList<LinkRowDto> Links,
    IReadOnlyList<CommentDto> Comments,
    string FilePath,
    // Kept last so positional callers stay stable.
    string? Slate = null,
    // Project-only signals. Health = on-track|at-risk|
    // off-track (null = no signal); Project = the doc-folder slug pointer.
    string? Health = null,
    string? Project = null,
    // Only on a call that closed the item (PATCH state, dismiss): open items
    // still blocked by it. Omitted on every other response.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<BlockedDependentDto>? BlockedDependents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    HeldByDto? HeldBy = null);

/// <summary>An open item still blocked by the item just closed.
/// <see cref="Remaining"/> is what else blocks it; null ⇔ the closed item was
/// its only impediment.</summary>
public sealed record BlockedDependentDto(string Id, string ShortId, string Title, string? Remaining);

public sealed record BodySectionDto(string Key, string Markdown);

public sealed record LinkRowDto(
    string Rel,
    string TargetText,
    string TargetPath,
    string? TargetItemId,
    string? Notes);

public sealed record CommentDto(
    string Id,
    string Author,
    DateTimeOffset Created,
    string Body);

public sealed record TreeNodeDto(
    ItemSummaryDto Item,
    IReadOnlyList<TreeNodeDto> Children,
    bool Match);

/// <summary>
/// Resolved context per <c>knit context</c>: own item + parent chain,
/// unioned agent skills, acceptance criteria + code pointers, blockers.
/// </summary>
public sealed record ContextDto(
    string Id,
    IReadOnlyList<ItemSummaryDto> ParentChain,
    IReadOnlyList<AgentSkillRefDto> AgentSkills,
    IReadOnlyList<KbRefDto> KbRefs,
    IReadOnlyList<ExternalRefDto> ExternalRefs,
    IReadOnlyList<AcceptanceCriterionDto> Acceptance,
    IReadOnlyList<string> CodePointers,
    IReadOnlyList<BlockerDto> Blockers,
    IReadOnlyList<BlockedStatusDto> Blocked,
    DesignRefDto? Design);

/// <summary><c>Checked</c> is null for a plain bullet, true/false for a task box.</summary>
public sealed record AcceptanceCriterionDto(string Text, bool? Checked);

public sealed record DesignRefDto(string Slug, string Path, string? InheritedFrom);

public sealed record AgentSkillRefDto(string Skill, string? InheritedFrom);

/// <summary>A Branch / Repo / PR row. <c>Url</c> is null when the
/// target isn't addressable by URL — a branch, a mount name, a directory.</summary>
public sealed record ExternalRefDto(
    string Rel,
    string Target,
    string? Url,
    string? Notes,
    string? InheritedFrom);

public sealed record KbRefDto(
    string TargetText,
    string TargetPath,
    string? Notes,
    string? InheritedFrom);

public sealed record BlockerDto(
    string Id,
    string ShortId,
    string Title,
    string State);

/// <summary>One source of a block: the item's own, or the ancestor holding it
/// (<see cref="InheritedFrom"/>). <see cref="Reason"/> is the combined summary;
/// <see cref="Note"/> + <see cref="BlockedBy"/> are its parts, so the SPA can
/// link each blocker.</summary>
public sealed record BlockedStatusDto(
    string Reason,
    string? InheritedFrom,
    string? Note,
    IReadOnlyList<BlockedByRefDto> BlockedBy);

/// <summary>A <c>Blocked by</c> row; <see cref="Id"/> is null when the target
/// isn't an item.</summary>
public sealed record BlockedByRefDto(string? Id, string Title, string? Notes);

public sealed record HealthDto(
    string RootPath,
    IdentityDto Identity,
    int ItemCount,
    string Version,
    int DueSoonDays,
    // Open-item due counts across the whole repo (never filtered) — feeds
    // the timeline nav-rail attention dot. Kept last so positional callers
    // stay stable.
    int OverdueCount = 0,
    int DueSoonCount = 0);

/// <param name="Handle">Null when no handle resolves — never <c>""</c>.</param>
public sealed record IdentityDto(string? Handle, string Display);

/// <summary>
/// SSE payload shape. Emitted as the JSON body of an
/// <c>event: created|changed|deleted</c> line.
/// </summary>
public sealed record EventDto(string Kind, string Id, string Op);
