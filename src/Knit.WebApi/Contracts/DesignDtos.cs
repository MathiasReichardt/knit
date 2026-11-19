namespace Knit.WebApi.Contracts;

/// <summary>
/// Lean projection of a design doc for list rows: slug, title (frontmatter
/// title, else first <c>#</c> heading, else slug), repo-relative path, and
/// whether it lives under <c>completed/</c>.
/// </summary>
public sealed record DesignSummaryDto(
    string Slug,
    string Title,
    string FilePath,
    bool Completed,
    string Modified);

/// <summary>
/// Full design payload. Designs are markdown only — the body carries the
/// frontmatter-stripped content the detail pane renders + edits.
/// </summary>
public sealed record DesignDto(
    string Slug,
    string Title,
    string FilePath,
    bool Completed,
    string Body,
    string Modified);

public sealed record DesignCreateRequestDto(
    string Slug,
    string? Title);

/// <summary>
/// Body-only patch. Frontmatter (title / slug / created) is preserved
/// server-side — the Designs screen edits prose, not metadata.
/// </summary>
public sealed record DesignPatchRequestDto(
    string Body);

public sealed record DesignArchiveRequestDto(
    string? Cycle);

public sealed record DesignArchiveResultDto(
    string Slug,
    string OldPath,
    string NewPath,
    IReadOnlyList<string> FilesRelinked);
