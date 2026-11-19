namespace Knit.WebApi.Contracts;

/// <summary>
/// Result of <c>GET /api/doctor/verify</c> — the same four consistency
/// classes <c>knit verify</c> reports, shaped for the Doctor screen.
/// Source / expected paths are repo-relative with forward slashes so the
/// SPA can map a row's <c>source</c> to a route (item / KB / file view).
/// </summary>
public sealed record VerifyResultDto(
    IReadOnlyList<InvalidItemRowDto> InvalidItems,
    IReadOnlyList<StrayFileRowDto> StrayFiles,
    IReadOnlyList<LinkTargetRowDto> LinkTargets,
    IReadOnlyList<MalformedLinkRowDto> MalformedItemLinks,
    IReadOnlyList<TitleDriftRowDto> Titles,
    IReadOnlyList<HierarchyRowDto> Hierarchy,
    IReadOnlyList<OriginRowDto> Origins,
    IReadOnlyList<DuplicateIdRowDto> DuplicateIds,
    IReadOnlyList<IdentityDriftRowDto> IdentityDrift,
    IReadOnlyList<HealthOnNonProjectRowDto> HealthOnNonProject,
    IReadOnlyList<ProjectPointerRowDto> ProjectPointers,
    IReadOnlyList<OrphanProjectFolderRowDto> OrphanProjectFolders,
    IReadOnlyList<OrphanProjectItemRowDto> OrphanProjectItems,
    IReadOnlyList<MalformedMilestoneRowDto> MalformedMilestones,
    IReadOnlyList<MultipleMilestonesRowDto> MultipleMilestones,
    IReadOnlyList<DoneWithOpenChildRowDto> DoneWithOpenChildren,
    IReadOnlyList<StaleBlockerRowDto> StaleBlockers,
    IReadOnlyList<CrossProjectMilestoneRowDto> CrossProjectMilestones,
    IReadOnlyList<UnregisteredSlateRowDto> UnregisteredSlates,
    IReadOnlyList<ProseRefRowDto> ProseRefs,
    IReadOnlyList<ProseLinkRowDto> ProseLinks,
    IReadOnlyList<SourcePathRowDto> SourcePaths,
    IReadOnlyList<SvgFileRowDto> SvgFiles);

/// <summary>Every mount, not only the broken ones — `GET /api/mounts` is the
/// SPA's read-only view of mount config. Its own endpoint rather than a
/// field on the verify result: a source panel needs one mount's state without
/// paying for a whole-repo scan.</summary>
public sealed record MountListDto(IReadOnlyList<MountRowDto> Mounts);

/// <summary>One config mount with its effective path and state.
/// <see cref="Source"/> names the config file the value came from, or is null
/// when a KB source references a mount nothing declares; <see cref="Reason"/>
/// is null when the mount is fine. Read-only: mounts are edited with
/// <c>knit mount</c>, never through the unauthenticated loopback API — a web
/// form silently choosing config.json over config.local.json is a footgun, and
/// the local file is per-machine by definition.</summary>
public sealed record MountRowDto(
    string Name,
    string? DeclaredPath,
    string EffectivePath,
    string? Source,
    bool Resolves,
    bool Portable,
    int SourceCount,
    string? Reason);

/// <summary>An entry under <c>.knit/items/</c> that won't load as a valid item
/// (bad id name, missing index.md, missing / mismatched frontmatter id, …), so
/// it silently vanishes from list / tree. <see cref="Reason"/> names the defect.
/// The Doctor screen surfaces it so the file stops being invisible.</summary>
public sealed record InvalidItemRowDto(string Source, string Reason);

/// <summary>A file under <c>designs/</c> or <c>kb/</c> that exists but is
/// invisible to <c>design list</c> / the KB browser (wrong name/location).
/// <see cref="Reason"/> names the defect. The Doctor screen surfaces it so no
/// authored file silently vanishes.</summary>
public sealed record StrayFileRowDto(string Source, string Reason);

public sealed record LinkTargetRowDto(
    string Source,
    string Rel,
    string TargetText,
    string TargetPath,
    string ExpectedPath);

/// <summary>A <c>## Links</c> row that looks like an item link but whose id
/// isn't a valid NanoID — the target exists yet the link can't resolve. No
/// auto-fix; the Doctor screen surfaces it for manual repair.</summary>
public sealed record MalformedLinkRowDto(
    string Source,
    string Rel,
    string TargetText,
    string TargetPath,
    string ExtractedId);

public sealed record TitleDriftRowDto(string Source, string FrontmatterTitle, string BodyH1);

public sealed record HierarchyRowDto(
    string Source,
    string ChildId,
    string ChildType,
    string? ParentId,
    string? ParentType);

public sealed record OriginRowDto(string Source, string Ref);

/// <summary>Two or more KB artifacts in one bucket declaring the same
/// frontmatter <c>id</c> — an identity collision so refs resolve ambiguously.
/// <see cref="Paths"/> lists every colliding file (repo-relative). The Doctor
/// screen surfaces it so the duplicate stops silently shadowing.</summary>
public sealed record DuplicateIdRowDto(string Bucket, string Id, IReadOnlyList<string> Paths);

/// <summary>A document whose declared identity disagrees with its filename:
/// a KB entry's <c>id:</c> or a design doc's <c>slug:</c>. Refs resolve
/// by filename, so the declared value resolves nowhere while being the only
/// one a reader sees in the file. Repaired with <c>knit relink --ids</c> — the
/// filename is authoritative.</summary>
public sealed record IdentityDriftRowDto(string Source, string Field, string Declared, string Expected);

/// <summary>A <c>health:</c> field on a non-project item — health is a
/// project-only signal, so it's a no-op noise on anything
/// else. Clear it or retype the item to project. Hard-severity.</summary>
public sealed record HealthOnNonProjectRowDto(string Source, string ItemId, string Type, string Health);

/// <summary>A bad <c>project:</c> doc-folder pointer: the field sits on a
/// non-project item, or the slug has no <c>projects/&lt;slug&gt;/readme.md</c>
/// behind it. <see cref="Reason"/> names which. Hard-severity.</summary>
public sealed record ProjectPointerRowDto(string Source, string ItemId, string Type, string Slug, string Reason);

/// <summary>A project doc folder with no item pointing at it via <c>project:</c>
/// — the folder-side orphan of the item↔folder join. Soft-severity (warning).</summary>
public sealed record OrphanProjectFolderRowDto(string Folder, string Slug);

/// <summary>A <c>project</c> item with no <c>project:</c> pointer, so it has no
/// coexisting doc folder — the item-side orphan of the join. Soft-severity
/// (warning).</summary>
public sealed record OrphanProjectItemRowDto(string Source, string ItemId);

/// <summary>An entry under a project's <c>milestones/</c> folder that doesn't
/// parse as a milestone sub-doc (bad frontmatter, non-markdown file, or a
/// sub-folder). Surfaced so nothing there silently vanishes. Hard-severity.</summary>
public sealed record MalformedMilestoneRowDto(string Source, string Reason);

/// <summary>An item carrying more than one <c>Milestone</c> row. One milestone
/// per item is a convention, not a parser rule — verify warns rather than
/// erroring. Warning-severity.</summary>
public sealed record MultipleMilestonesRowDto(string Source, string ItemId, int Count);

/// <summary>An item linking a milestone that belongs to another project.
/// Legitimate (and what <c>knit milestone move</c> deliberately produces) —
/// informational, never a failure.</summary>
public sealed record CrossProjectMilestoneRowDto(
    string Source, string ItemId, string ItemProject, string MilestoneProject, string MilestoneSlug);

/// <summary>A <c>done</c> item with an open direct child (<c>todo</c> /
/// <c>in-progress</c>) — the subtree claims complete while live work hangs
/// under it. Closing does not cascade, so this is reachable through normal use;
/// dismissed parents are exempt. Warning-severity. Fix by closing the child or
/// reopening the parent.</summary>
public sealed record DoneWithOpenChildRowDto(
    string Source,
    string ParentId,
    string ChildId,
    string ChildType,
    string ChildState);

/// <summary>An open item whose <c>Blocked by</c> row names a closed item —
/// closing a blocker resolves nothing. Warning-severity: the block may stand.
/// <see cref="Sole"/>: the row is the item's only impediment (unblock), else
/// remove the link.</summary>
public sealed record StaleBlockerRowDto(
    string Source,
    string ItemId,
    string BlockerId,
    string BlockerState,
    bool Sole);

/// <summary>A <c>slate:</c> value with no registry row; <see cref="Paths"/>
/// lists the items carrying it (repo-relative). Warning-severity — a
/// <c>--force</c> forward reference is deliberate.</summary>
public sealed record UnregisteredSlateRowDto(string Slate, IReadOnlyList<string> Paths);

public sealed record ProseRefRowDto(string Source, string Ref, string Reason, IReadOnlyList<string> Candidates);

/// <param name="Reason"><c>missing</c>, or <c>outside_repo</c>: the target leaves the repo root and no mount covers it.</param>
public sealed record ProseLinkRowDto(string Source, string Kind, string Target, string ExpectedPath, string Reason);

/// <summary>A KB source whose stored <c>path</c> names nothing; <see cref="Path"/>
/// is the stored, mount-relative value.</summary>
public sealed record SourcePathRowDto(string Source, string? Mount, string Path, string Reason);

/// <summary>An SVG under <c>.knit/</c> that won't render safely or read well
/// (forbidden element, no viewBox, dangling edge end, …). Warning-severity.</summary>
public sealed record SvgFileRowDto(string Source, string Reason);

/// <summary>Repair one broken <c>## Links</c> target. <see cref="Source"/> is
/// the repo-relative path of the file holding the row; <see cref="TargetPath"/>
/// is the broken target string as authored.</summary>
public sealed record RelinkRequestDto(string Source, string TargetPath);

public sealed record RelinkResultDto(bool Repaired, string? NewTarget);

