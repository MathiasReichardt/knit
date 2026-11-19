import { ApiError, apiWrite, getJson, sendJson } from './http'
import { fetchPaged } from './paging'
import type { ItemHealth, ItemSummary } from './types'

// Mirrors the WebApi project DTOs (camelCase over the wire). A project has two
// halves: the doc *folder* under .knit/projects/<slug>/ (readme + optional
// roadmap + milestone sub-docs + free-form files) exposed here, and the project
// *item* (type=project) that carries the subtree and points at the folder via
// its `project` frontmatter slug. The Projects nav joins the two on that slug.

export interface Milestone {
  project: string
  slug: string
  title: string
  // ISO due date, or null when the milestone carries no target.
  target: string | null
  // planned | reached | dropped.
  state: string
  // Number of items linking this milestone (Milestone relation).
  linkedCount: number
  // Fractional rank (a-z) for manual ordering in the roadmap spine; null for a
  // milestone that predates ranking (sorts last, by slug).
  rank: string | null
}

export interface ProjectFolder {
  slug: string
  hasReadme: boolean
  hasRoadmap: boolean
  milestones: Milestone[]
  // Folder-relative paths (forward slashes), excluding readme/roadmap and the
  // milestones/ sub-docs — the free-form documents.
  files: string[]
}

// One card in the landing projects row: the folder, the project item and the
// status rollup, joined server-side. `slug` is null on the synthesized
// "All items" stand-in the endpoint returns when the repo has no projects —
// that row carries no health and no next milestone, so it can't be mistaken
// for a project.
export interface ProjectLanding {
  slug: string | null
  title: string
  itemId: string | null
  health: ItemHealth | null
  total: number
  done: number
  percent: number
  blocked: number
  overdue: number
  nextMilestoneSlug: string | null
  nextMilestoneTitle: string | null
  nextMilestoneTarget: string | null
}

export interface MilestoneDetail {
  project: string
  slug: string
  title: string
  target: string | null
  state: string
  body: string
  // Items carrying a Milestone row that resolves to this sub-doc — the
  // reverse index. May span projects (cross-project links).
  backlinks: ItemSummary[]
  // Absolute filesystem path to the milestone .md (for the copy-path button).
  filePath: string
  // Fractional rank (a-z) for manual ordering; null when unranked.
  rank: string | null
}

export interface MilestoneCreateRequest {
  slug: string
  title?: string | null
  // ISO date (YYYY-MM-DD) or omitted for no target.
  target?: string | null
  // planned (default) | reached | dropped.
  state?: string | null
}

// Frontmatter edits for a milestone PATCH. A null/omitted field is left
// untouched; an empty-string `target` clears the target date (the server's
// clear sentinel — JSON can't tell null-the-value from omitted).
export interface MilestonePatchFrontmatter {
  title?: string
  state?: string
  target?: string
}

export interface MilestonePatchRequest {
  frontmatter?: MilestonePatchFrontmatter
  body?: string
}

// Result of reordering a project's milestones — the applied slug order.
export interface MilestoneReorderResult {
  project: string
  order: string[]
}

// Result of renaming a milestone — back-links are re-pointed, so
// repointedItemIds lists the items whose Milestone rows changed.
export interface MilestoneRenameResult {
  project: string
  oldSlug: string
  newSlug: string
  oldPath: string
  newPath: string
  repointedItemIds: string[]
  filesRelinked: string[]
}

// Result of moving a milestone into another project. Back-links are
// re-pointed, never dropped — crossProjectItemIds is the subset that now
// points across a project boundary (informational, not an error).
export interface MilestoneMoveResult {
  fromProject: string
  toProject: string
  oldSlug: string
  newSlug: string
  oldPath: string
  newPath: string
  repointedItemIds: string[]
  crossProjectItemIds: string[]
  filesRelinked: string[]
}

// Result of DELETE (also the ?dryRun preview). Back-links are left dangling by
// default (danglingItemIds) or scrubbed with ?scrubLinks (scrubbedItemIds).
export interface MilestoneDeleteResult {
  project: string
  slug: string
  dryRun: boolean
  deletedPath: string | null
  danglingItemIds: string[]
  scrubbedItemIds: string[]
}

// Result of DELETE /api/projects/{slug} — also the shape of its ?dryRun impact
// preview. Removing a project deletes the project item + doc folder but
// re-roots (does not cascade) its direct children; danglingItemIds are items
// whose back-links point here and are left dangling by default, or moved into
// scrubbedItemIds when scrubLinks is set.
export interface ProjectDeleteResult {
  slug: string
  dryRun: boolean
  removedItemId: string | null
  reRootedChildIds: string[]
  scrubbedItemIds: string[]
  danglingItemIds: string[]
  milestoneCount: number
  fileCount: number
  folderDeleted: boolean
}

// A readme / roadmap / free-form doc body (GET/POST/PATCH
// /api/projects/{slug}/docs/...). `path` is project-folder-relative with
// forward slashes (readme.md, notes.md, sub/readme.md). Milestones have their
// own endpoints and never surface here.
export interface ProjectDoc {
  project: string
  path: string
  body: string
  // Absolute filesystem path to the doc .md (for the copy-path button).
  filePath: string
}

export interface ProjectDocRenameResult {
  project: string
  oldPath: string
  newPath: string
  wasFolder: boolean
  filesRelinked: string[]
}

// Result of DELETE (also the 409 "blocked" body): a folder-form doc with
// sibling files is not removed unless deleteFolder is set — blockingFiles then
// lists what forced the choice.
export interface ProjectDocDeleteResult {
  project: string
  path: string
  deleted: boolean
  isFolderForm: boolean
  blockingFiles: string[]
}

// Result of the two-halves co-create (POST /api/projects): the joined project.
// createdItem is false when an existing project item was linked.
export interface ProjectCreateResult {
  slug: string
  itemId: string
  folderPath: string
  createdItem: boolean
  createdRoadmap: boolean
}

export interface ProjectRoadmapResult {
  project: string
  path: string
}

function milestoneUrl(project: string, slug: string): string {
  return `/api/projects/${encodeURIComponent(project)}/milestones/${encodeURIComponent(slug)}`
}

// Fetch a milestone plus its concurrency etag. The etag rides into the
// next PATCH's If-Match so a save landing after another writer touched the
// milestone gets a 412 instead of silently clobbering.
async function fetchMilestoneWithEtag(
  project: string,
  slug: string,
): Promise<{ detail: MilestoneDetail; etag: string }> {
  const r = await fetch(milestoneUrl(project, slug))
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  const detail = (await r.json()) as MilestoneDetail
  return { detail, etag: r.headers.get('ETag') ?? '' }
}

async function createMilestone(
  project: string,
  body: MilestoneCreateRequest,
): Promise<MilestoneDetail> {
  const r = await apiWrite(`/api/projects/${encodeURIComponent(project)}/milestones`, 'POST', { body })
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  return (await r.json()) as MilestoneDetail
}

async function patchMilestone(
  project: string,
  slug: string,
  body: MilestonePatchRequest,
  ifMatch: string,
): Promise<{ detail: MilestoneDetail; etag: string }> {
  const r = await apiWrite(milestoneUrl(project, slug), 'PATCH', { body, ifMatch })
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem, r.headers.get('ETag'))
  }
  const detail = (await r.json()) as MilestoneDetail
  return { detail, etag: r.headers.get('ETag') ?? '' }
}

// The catch-all doc route keeps '/' as a separator (folder form) but each
// segment is escaped so a name can't smuggle in a query/hash.
function docUrl(project: string, path: string): string {
  const encPath = path.split('/').map(encodeURIComponent).join('/')
  return `/api/projects/${encodeURIComponent(project)}/docs/${encPath}`
}

async function fetchDocWithEtag(
  project: string,
  path: string,
): Promise<{ doc: ProjectDoc; etag: string }> {
  const r = await fetch(docUrl(project, path))
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem)
  }
  const doc = (await r.json()) as ProjectDoc
  return { doc, etag: r.headers.get('ETag') ?? '' }
}

async function patchDoc(
  project: string,
  path: string,
  body: string,
  ifMatch: string,
): Promise<{ doc: ProjectDoc; etag: string }> {
  const r = await apiWrite(docUrl(project, path), 'PATCH', { body: { body }, ifMatch })
  if (!r.ok) {
    let problem = null
    try { problem = await r.json() } catch { /* noop */ }
    throw new ApiError(r.status, problem, r.headers.get('ETag'))
  }
  const doc = (await r.json()) as ProjectDoc
  return { doc, etag: r.headers.get('ETag') ?? '' }
}

// DELETE returns 200 (removed) or 409 (folder-form blocked, body carries
// blockingFiles) — both are real results the dialog inspects, so neither
// throws. Any other status is a genuine error.
async function removeDoc(
  project: string,
  path: string,
  opts?: { deleteFolder?: boolean },
): Promise<ProjectDocDeleteResult> {
  const q = opts?.deleteFolder ? '?deleteFolder=true' : ''
  const r = await apiWrite(`${docUrl(project, path)}${q}`, 'DELETE')
  if (r.status === 200 || r.status === 409) {
    return (await r.json()) as ProjectDocDeleteResult
  }
  let problem = null
  try { problem = await r.json() } catch { /* noop */ }
  throw new ApiError(r.status, problem)
}

export const projectsApi = {
  list: () => getJson<ProjectFolder[]>('/api/projects'),
  // Landing row — attention-ordered, never empty (the server
  // synthesizes the all-items row when there are no projects).
  landing: (limit: number) =>
    fetchPaged<ProjectLanding>(`/api/projects/landing?limit=${limit}`),
  // Two-halves co-create: scaffold the doc folder + mint-or-link a project
  // item in one action (never a bare orphan half). linkItemId links an
  // existing project item instead of minting a fresh one.
  createProject: (body: { slug: string; title?: string; roadmap?: boolean; linkItemId?: string }) =>
    sendJson<ProjectCreateResult>('/api/projects', 'POST', body),
  scaffoldRoadmap: (project: string) =>
    sendJson<ProjectRoadmapResult>(`/api/projects/${encodeURIComponent(project)}/roadmap`, 'POST'),
  createDoc: (project: string, body: { slug?: string; title?: string; asFolder?: boolean }) =>
    sendJson<ProjectDoc>(`/api/projects/${encodeURIComponent(project)}/docs`, 'POST', { slug: body.slug, title: body.title, asFolder: body.asFolder ?? false }),
  doc: (project: string, path: string) => getJson<ProjectDoc>(docUrl(project, path)),
  fetchDocWithEtag,
  patchDoc,
  renameDoc: (project: string, oldPath: string, newPath: string) =>
    sendJson<ProjectDocRenameResult>(`/api/projects/${encodeURIComponent(project)}/docs/rename`, 'POST', { oldPath, newPath }),
  removeDoc,
  milestone: (project: string, slug: string) =>
    getJson<MilestoneDetail>(milestoneUrl(project, slug)),
  fetchMilestoneWithEtag,
  createMilestone,
  patchMilestone,
  renameMilestone: (project: string, slug: string, newSlug: string) =>
    sendJson<MilestoneRenameResult>(`${milestoneUrl(project, slug)}/rename`, 'POST', { newSlug }),
  // Move into another project; newSlug is only needed when the destination
  // already holds this slug (a collision without it is an error).
  moveMilestone: (project: string, slug: string, toProject: string, newSlug?: string) =>
    sendJson<MilestoneMoveResult>(`${milestoneUrl(project, slug)}/move`, 'POST', { toProject, newSlug: newSlug ?? null }),
  // Persist a new milestone order (full slug list, top to bottom). The server
  // rewrites fractional ranks — clients never construct a rank string.
  reorderMilestone: (project: string, order: string[]) =>
    sendJson<MilestoneReorderResult>(`/api/projects/${encodeURIComponent(project)}/milestones/reorder`, 'POST', { order }),
  removeMilestone: (
    project: string,
    slug: string,
    opts?: { dryRun?: boolean; scrubLinks?: boolean },
  ) => {
    const q = new URLSearchParams()
    if (opts?.dryRun) q.set('dryRun', 'true')
    if (opts?.scrubLinks) q.set('scrubLinks', 'true')
    const qs = q.toString()
    return sendJson<MilestoneDeleteResult>(
      `${milestoneUrl(project, slug)}${qs ? `?${qs}` : ''}`,
      'DELETE',
    )
  },
  remove: (slug: string, opts?: { dryRun?: boolean; scrubLinks?: boolean }) => {
    const q = new URLSearchParams()
    if (opts?.dryRun) q.set('dryRun', 'true')
    if (opts?.scrubLinks) q.set('scrubLinks', 'true')
    const qs = q.toString()
    return sendJson<ProjectDeleteResult>(
      `/api/projects/${encodeURIComponent(slug)}${qs ? `?${qs}` : ''}`,
      'DELETE',
    )
  },
}

export const projectsQk = {
  // Rooted on 'projects' so the SSE `projects` invalidation (sse.ts) refreshes
  // every sub-query in one call.
  list: ['projects', 'list'] as const,
  landing: ['projects', 'landing'] as const,
  milestone: (project: string, slug: string) =>
    ['projects', 'milestone', project, slug] as const,
  doc: (project: string, path: string) =>
    ['projects', 'doc', project, path] as const,
}
