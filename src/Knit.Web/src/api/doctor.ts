import { getJson, postJson } from './http'

// Mirrors the WebApi Doctor DTOs (camelCase over the wire). The
// verify scan returns all four classes the CLI `knit verify` reports;
// `source` / `expectedPath` are repo-relative with forward slashes so
// the screen can route them through resolveLink.

// An entry under .knit/items/ that won't load as a valid item (bad id name,
// missing index.md, missing / mismatched frontmatter id, …) — it silently
// vanishes from list/tree. `reason` names the defect; no auto-fix.
export interface InvalidItemRow {
  source: string
  reason: string
}

// A file under designs/ or kb/ that exists but is invisible to `design list` /
// the KB browser because its name or location doesn't match the addressable
// shape. `reason` names the defect; no auto-fix.
export interface StrayFileRow {
  source: string
  reason: string
}

export interface LinkTargetRow {
  source: string
  rel: string
  targetText: string
  targetPath: string
  expectedPath: string
}

// A `## Links` row shaped like an item link but with a malformed (non-NanoID)
// id — the target exists yet the link can't resolve. No auto-fix (relink
// leaves it alone); surfaced for manual repair.
export interface MalformedLinkRow {
  source: string
  rel: string
  targetText: string
  targetPath: string
  extractedId: string
}

export interface TitleDriftRow {
  source: string
  frontmatterTitle: string
  bodyH1: string
}

export interface HierarchyRow {
  source: string
  childId: string
  childType: string
  parentId: string | null
  parentType: string | null
}

export interface OriginRow {
  source: string
  ref: string
}

// Two or more KB artifacts in one bucket declaring the same frontmatter `id`
// — an identity collision, so refs resolve ambiguously. `paths` lists every
// colliding file (repo-relative). No auto-fix; merge + delete the stale one.
export interface DuplicateIdRow {
  bucket: string
  id: string
  paths: string[]
}

// A `health:` field on a non-project item — health is project-only, so it's
// inert noise anywhere else. Clear it or retype the item to project.
export interface HealthOnNonProjectRow {
  source: string
  itemId: string
  type: string
  health: string
}

// A bad `project:` doc-folder pointer: the field sits on a non-project item, or
// its slug has no projects/<slug>/readme.md behind it. `reason` names which.
export interface ProjectPointerRow {
  source: string
  itemId: string
  type: string
  slug: string
  reason: string
}

// An entry under a project's milestones/ folder that doesn't parse as a
// milestone sub-doc (bad frontmatter, non-md file, or a sub-folder).
export interface MalformedMilestoneRow {
  source: string
  reason: string
}

// An item carrying more than one Milestone row — one-per-item is a convention,
// so this is a warning, not a hard error.
export interface MultipleMilestonesRow {
  source: string
  itemId: string
  count: number
}

// A `done` item with an open direct child (todo / in-progress) — the subtree
// claims complete while live work hangs under it. Closing does not cascade, so
// this is reachable through normal use; dismissed parents are exempt. Warning.
export interface DoneWithOpenChildRow {
  source: string
  parentId: string
  childId: string
  childType: string
  childState: string
}

// An open item whose `Blocked by` row names a closed item — closing a blocker
// resolves nothing. Warning: the block may stand. `sole`: the row is the item's
// only impediment (unblock it), else remove the link.
export interface StaleBlockerRow {
  source: string
  itemId: string
  blockerId: string
  blockerState: string
  sole: boolean
}

// An item whose Milestone row points into another project. Legitimate — an
// item may serve another project's milestone, and `knit milestone move`
// produces this on purpose — so it is informational and excluded from the
// issue count.
export interface CrossProjectMilestoneRow {
  source: string
  itemId: string
  itemProject: string
  milestoneProject: string
  milestoneSlug: string
}

// A slate: value with no registry row, with the items carrying it. Warning —
// a `set slate=… --force` forward reference is deliberate.
export interface UnregisteredSlateRow {
  slate: string
  paths: string[]
}

export interface ProseRefRow {
  source: string
  ref: string
  // unresolved | short_id (one candidate — `knit relink` expands it) | ambiguous_id
  // | missing_section | suffixed_anchor | reserved_anchor (the ref is `<doc>#mermaid-<n>`)
  reason: string
  candidates: string[]
}

export interface ProseLinkRow {
  source: string
  kind: string
  target: string
  expectedPath: string
  // missing | outside_repo (leaves the repo root, no mount covers it)
  reason: string
}

// A KB source whose `path` names nothing. Sources on a mount that doesn't
// resolve here aren't listed — that is one mount finding, not one per source.
export interface SourcePathRow {
  source: string
  mount: string | null
  path: string
  reason: string
}

// An SVG under .knit/ that won't render safely or read well (forbidden
// element, no viewBox, dangling edge end, …). Warning-severity.
export interface SvgFileRow {
  source: string
  reason: string
}

// A document whose declared identity disagrees with its own filename: a KB
// entry's `id:` or a design doc's `slug:`. Refs resolve by filename, so
// the declared value resolves nowhere while being the only one visible in the
// file. Read-only here — repaired with `knit relink --ids`.
export interface IdentityDriftRow {
  source: string
  field: string
  declared: string
  expected: string
}

export interface VerifyResult {
  invalidItems: InvalidItemRow[]
  strayFiles: StrayFileRow[]
  linkTargets: LinkTargetRow[]
  malformedItemLinks: MalformedLinkRow[]
  titles: TitleDriftRow[]
  hierarchy: HierarchyRow[]
  origins: OriginRow[]
  duplicateIds: DuplicateIdRow[]
  identityDrift: IdentityDriftRow[]
  healthOnNonProject: HealthOnNonProjectRow[]
  projectPointers: ProjectPointerRow[]
  malformedMilestones: MalformedMilestoneRow[]
  multipleMilestones: MultipleMilestonesRow[]
  doneWithOpenChildren: DoneWithOpenChildRow[]
  staleBlockers: StaleBlockerRow[]
  crossProjectMilestones: CrossProjectMilestoneRow[]
  unregisteredSlates: UnregisteredSlateRow[]
  proseRefs: ProseRefRow[]
  proseLinks: ProseLinkRow[]
  sourcePaths: SourcePathRow[]
  svgFiles: SvgFileRow[]
}

export interface RelinkResult {
  repaired: boolean
  newTarget: string | null
}

// Total rows across all classes — drives the global chip's badge and
// the "all clear" state. Informational classes (cross-project milestone
// links) are excluded: they are listed, not something to fix.
export function verifyIssueCount(v: VerifyResult): number {
  return (
    v.invalidItems.length +
    v.strayFiles.length +
    v.linkTargets.length +
    v.malformedItemLinks.length +
    v.titles.length +
    v.hierarchy.length +
    v.origins.length +
    v.duplicateIds.length +
    v.identityDrift.length +
    v.healthOnNonProject.length +
    v.projectPointers.length +
    v.malformedMilestones.length +
    v.multipleMilestones.length +
    v.doneWithOpenChildren.length +
    v.staleBlockers.length +
    v.unregisteredSlates.length +
    v.proseRefs.length +
    v.proseLinks.length +
    v.sourcePaths.length +
    v.svgFiles.length
  )
}

export const doctorApi = {
  verify: () => getJson<VerifyResult>('/api/doctor/verify'),
  relink: (source: string, targetPath: string) =>
    postJson<RelinkResult>('/api/doctor/relink', { source, targetPath }),
}

export const doctorQk = {
  verify: ['doctor', 'verify'] as const,
}
