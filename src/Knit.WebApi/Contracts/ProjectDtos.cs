namespace Knit.WebApi.Contracts;

/// <summary>
/// One milestone sub-doc as listed under a project folder
/// (<c>GET /api/projects</c>).
/// </summary>
public sealed record MilestoneDto(
    string Project,
    string Slug,
    string Title,
    string? Target,
    string State,
    int LinkedCount,
    // Fractional rank for manual ordering in the roadmap spine; null
    // for a milestone that predates ranking (sorts last, by slug).
    string? Rank);

/// <summary>
/// One project doc folder (<c>GET /api/projects</c>). <see cref="Files"/>
/// holds the free-form files (project-folder-relative, forward slashes) —
/// everything except readme/roadmap and the <c>milestones/</c> sub-docs;
/// content is fetched via the file endpoints.
/// </summary>
public sealed record ProjectFolderDto(
    string Slug,
    bool HasReadme,
    bool HasRoadmap,
    IReadOnlyList<MilestoneDto> Milestones,
    IReadOnlyList<string> Files);

/// <summary>
/// One card in the landing page's projects row (<c>GET /api/projects/landing</c>):
/// the doc folder, the project <em>item</em> and the status rollup joined
/// server-side, so the row needs a single round-trip.
///
/// <para><see cref="Slug"/> is null on the synthesized <em>all items</em>
/// stand-in the endpoint returns when the repo has no projects. That row
/// deliberately carries no health and no next milestone — it is a repo-wide
/// rollup, not a project, and must not read as one.</para>
/// </summary>
public sealed record ProjectLandingDto(
    string? Slug,
    string Title,
    string? ItemId,
    string? Health,
    int Total,
    int Done,
    int Percent,
    int Blocked,
    int Overdue,
    string? NextMilestoneSlug,
    string? NextMilestoneTitle,
    string? NextMilestoneTarget);

/// <summary>
/// Milestone detail + backlinks
/// (<c>GET /api/projects/{project}/milestones/{slug}</c>). Backlinks are
/// the reverse index over <c>Milestone</c> link rows: every item linking
/// this sub-doc.
/// </summary>
public sealed record MilestoneDetailDto(
    string Project,
    string Slug,
    string Title,
    string? Target,
    string State,
    string Body,
    IReadOnlyList<ItemSummaryDto> Backlinks,
    string FilePath,
    string? Rank);

/// <summary>Create a milestone (<c>POST /api/projects/{project}/milestones</c>).
/// <see cref="State"/> defaults to <c>planned</c> when null.</summary>
public sealed record MilestoneCreateRequestDto(
    string Slug,
    string? Title,
    string? Target,
    string? State);

/// <summary>
/// Frontmatter edits for a milestone PATCH. A null field is left untouched;
/// an empty-string <see cref="Target"/> clears the target date (JSON can't
/// tell null-the-value from omitted, so "" is the clear sentinel).
/// </summary>
public sealed record MilestonePatchFrontmatterDto(
    string? Title,
    string? State,
    string? Target);

/// <summary>Patch a milestone's frontmatter and/or body
/// (<c>PATCH /api/projects/{project}/milestones/{slug}</c>), If-Match guarded.</summary>
public sealed record MilestonePatchRequestDto(
    MilestonePatchFrontmatterDto? Frontmatter,
    string? Body);

/// <summary>Rename a milestone
/// (<c>POST /api/projects/{project}/milestones/{slug}/rename</c>).</summary>
public sealed record MilestoneRenameRequestDto(string NewSlug);

/// <summary>Reorder a project's milestones
/// (<c>POST /api/projects/{project}/milestones/reorder</c>). <see cref="Order"/>
/// is the full desired slug order, top to bottom, and must be a permutation of
/// the project's milestones. The server rewrites fractional ranks to match —
/// clients never construct a rank string.</summary>
public sealed record MilestoneReorderRequestDto(IReadOnlyList<string> Order);

/// <summary>Result of a milestone reorder — the applied slug order.</summary>
public sealed record MilestoneReorderResultDto(
    string Project,
    IReadOnlyList<string> Order);

/// <summary>Result of a milestone rename — back-links are re-pointed, so
/// <see cref="RepointedItemIds"/> lists the items that changed.</summary>
public sealed record MilestoneRenameResultDto(
    string Project,
    string OldSlug,
    string NewSlug,
    string OldPath,
    string NewPath,
    IReadOnlyList<string> RepointedItemIds,
    IReadOnlyList<string> FilesRelinked);

/// <summary>Move a milestone into another project
/// (<c>POST /api/projects/{project}/milestones/{slug}/move</c>).
/// <see cref="NewSlug"/> is only needed when the destination already holds
/// that slug — a collision without it is an error.</summary>
public sealed record MilestoneMoveRequestDto(string ToProject, string? NewSlug);

/// <summary>Result of a milestone move. Back-links are re-pointed, never
/// dropped, so <see cref="RepointedItemIds"/> lists the items that changed and
/// <see cref="CrossProjectItemIds"/> the subset that now points across a
/// project boundary (informational — <c>knit verify</c> lists them too).</summary>
public sealed record MilestoneMoveResultDto(
    string FromProject,
    string ToProject,
    string OldSlug,
    string NewSlug,
    string OldPath,
    string NewPath,
    IReadOnlyList<string> RepointedItemIds,
    IReadOnlyList<string> CrossProjectItemIds,
    IReadOnlyList<string> FilesRelinked);

/// <summary>Result of <c>DELETE /api/projects/{project}/milestones/{slug}</c>
/// (also its <c>?dryRun=true</c> preview). Back-links are left dangling by
/// default (<see cref="DanglingItemIds"/>) or scrubbed with
/// <c>?scrubLinks=true</c> (<see cref="ScrubbedItemIds"/>).</summary>
public sealed record MilestoneDeleteResultDto(
    string Project,
    string Slug,
    bool DryRun,
    string? DeletedPath,
    IReadOnlyList<string> DanglingItemIds,
    IReadOnlyList<string> ScrubbedItemIds);

/// <summary>
/// A single project doc's body (<c>GET</c>/<c>POST</c>/<c>PATCH
/// /api/projects/{slug}/docs/...</c>). <see cref="Path"/> is the doc path
/// relative to the project folder (forward slashes, e.g. <c>readme.md</c>,
/// <c>notes.md</c>, <c>sub/readme.md</c>). Carried alongside a strong ETag
/// header for optimistic-concurrency body edits.
/// </summary>
public sealed record ProjectDocDto(
    string Project,
    string Path,
    string Body,
    string FilePath);

/// <summary>Create a free-form project doc
/// (<c>POST /api/projects/{slug}/docs</c>). Either <see cref="Slug"/> or
/// <see cref="Title"/> must be given; <see cref="AsFolder"/> creates the folder
/// form (<c>&lt;slug&gt;/readme.md</c>).</summary>
public sealed record ProjectDocCreateRequestDto(
    string? Slug,
    string? Title,
    bool AsFolder);

/// <summary>Patch a project doc's body
/// (<c>PATCH /api/projects/{slug}/docs/{path}</c>), If-Match guarded.</summary>
public sealed record ProjectDocPatchRequestDto(string? Body);

/// <summary>Rename / move a free-form project doc
/// (<c>POST /api/projects/{slug}/docs/rename</c>). Inbound prose links are
/// repointed; the result lists the files that changed.</summary>
public sealed record ProjectDocRenameRequestDto(string OldPath, string NewPath);

/// <summary>Result of a project-doc rename.</summary>
public sealed record ProjectDocRenameResultDto(
    string Project,
    string OldPath,
    string NewPath,
    bool WasFolder,
    IReadOnlyList<string> FilesRelinked);

/// <summary>Result of <c>DELETE /api/projects/{slug}/docs/{path}</c>. A
/// folder-form doc with sibling files is not removed unless
/// <c>?deleteFolder=true</c>; <see cref="BlockingFiles"/> then lists what
/// forced the choice (empty on a successful delete).</summary>
public sealed record ProjectDocDeleteResultDto(
    string Project,
    string Path,
    bool Deleted,
    bool IsFolderForm,
    IReadOnlyList<string> BlockingFiles);

/// <summary>Result of <c>POST /api/projects/{slug}/roadmap</c> — scaffolds
/// <c>roadmap.md</c> standalone (errors if it already exists; never touches
/// the readme).</summary>
public sealed record ProjectRoadmapResultDto(
    string Project,
    string Path);

/// <summary>
/// Co-create a project (<c>POST /api/projects</c>): scaffold the doc folder
/// (<c>readme.md</c>) <em>and</em> mint-or-link a <c>project</c> tracker item in
/// one action (<c>ProjectCreator</c>). When
/// <see cref="LinkItemId"/> is set that existing project item is linked;
/// otherwise a fresh one is minted. <see cref="Title"/> defaults to the slug
/// when null. The folder-only escape hatch (CLI <c>--no-item</c>) is
/// intentionally not exposed — the web flow never leaves an orphan half.
/// </summary>
public sealed record ProjectCreateRequestDto(
    string Slug,
    string? Title,
    bool Roadmap,
    string? LinkItemId);

/// <summary>Result of <c>POST /api/projects</c> — the joined project.
/// <see cref="CreatedItem"/> is false when an existing item was linked.</summary>
public sealed record ProjectCreateResultDto(
    string Slug,
    string ItemId,
    string FolderPath,
    bool CreatedItem,
    bool CreatedRoadmap);

/// <summary>
/// Result of <c>DELETE /api/projects/{slug}</c> — also the shape of its
/// <c>?dryRun=true</c> impact preview. Removing a project deletes the
/// project item + doc folder but re-roots (does not cascade) its direct
/// children; <see cref="DanglingItemIds"/> are items whose back-links point
/// at the project and are left dangling by default (or listed in
/// <see cref="ScrubbedItemIds"/> when <c>?scrubLinks=true</c>).
/// </summary>
public sealed record ProjectDeleteResultDto(
    string Slug,
    bool DryRun,
    string? RemovedItemId,
    IReadOnlyList<string> ReRootedChildIds,
    IReadOnlyList<string> ScrubbedItemIds,
    IReadOnlyList<string> DanglingItemIds,
    int MilestoneCount,
    int FileCount,
    bool FolderDeleted);
