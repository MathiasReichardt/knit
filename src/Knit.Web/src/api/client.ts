import type {
  ActivityEntry,
  Health,
  Identity,
  Item,
  ItemContext,
  ItemSummary,
  ItemType,
  Timeline,
  TreeNode,
} from './types'
import { ApiError, apiWrite, parseProblem } from './http'
import { fetchPaged, type Paged } from './paging'
import type { VersionInfo } from '../lib/formatVersion'

async function getJson<T>(url: string): Promise<T> {
  const r = await fetch(url)
  if (!r.ok) throw new Error(`HTTP ${r.status} ${r.statusText}`)
  return r.json() as Promise<T>
}

export { ApiError, type ProblemDetails } from './http'

export interface NewItemRequest {
  type: ItemType
  title: string
  parent?: string | null
  template?: string | null
}

export interface ItemPatch {
  state?: string
  assignee?: string | null
  title?: string
  effort?: string | null
  agentSkills?: string[]
  tags?: string[]
  blocked?: string | null
  parent?: string | null
  body?: string
  // Date-only YYYY-MM-DD. '' clears; null omits (no change).
  due?: string | null
  // someday|normal|important|critical. '' or 'normal' resets; null omits.
  priority?: string | null
  // Slate name (slugified server-side). '' clears; null omits (no change).
  slate?: string | null
  // Project-only. 'on-track'|'at-risk'|'off-track'; '' clears; null omits.
  health?: string | null
}

export type LinkRel =
  | 'parent'
  | 'blocks'
  | 'blocked-by'
  | 'related'
  | 'kb-ref'
  | 'milestone'
  | 'branch'
  | 'repo'
  | 'pr'
  | 'commit'

export interface CreateLinkRequest {
  rel: LinkRel
  target: string
  notes?: string | null
}

export interface CreateCommentRequest {
  body: string
}

export interface CommentMutationResult {
  item: Item
  comment: { id: string; author: string; created: string; body: string }
}

export interface RetypeRequest {
  to: string
  parent?: string | null
}

export interface BlockRequest {
  by?: string | null
  reason?: string | null
}

export interface DismissResult {
  item: Item
  cascaded: Item[]
}

async function createItem(body: NewItemRequest): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite('/api/items', 'POST', { body })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function patchItem(
  id: string,
  patch: ItemPatch,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}`, 'PATCH', { body: patch, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function blockItem(
  id: string,
  body: BlockRequest,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/block`, 'POST', { body, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function unblockItem(
  id: string,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/unblock`, 'POST', { ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function dismissItem(
  id: string,
  cascade: boolean,
  ifMatch: string,
): Promise<{ item: Item; cascaded: Item[]; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/dismiss`, 'POST', { body: { cascade }, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  if (cascade) {
    const result = (await r.json()) as DismissResult
    return { item: result.item, cascaded: result.cascaded ?? [], etag: r.headers.get('ETag') }
  }
  const item = (await r.json()) as Item
  return { item, cascaded: [], etag: r.headers.get('ETag') }
}

export type ReorderPosition = 'top' | 'bottom' | 'up' | 'down'

async function reorderItem(
  id: string,
  position: ReorderPosition,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/reorder`, 'POST', { body: { position }, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

export interface ReorderItemsRequest {
  /** Rows in the order they should end up under `parent`, each with the etag
   * it was read at — the whole set is placed or nothing is. */
  items: { id: string; ifMatch: string }[]
  parent: string | null
  /** Id of the sibling the set lands before / after. At most one; neither
   * appends to the end of the group. Ranks are the server's business. */
  before?: string
  after?: string
}

async function reorderItems(body: ReorderItemsRequest): Promise<Item[]> {
  const r = await apiWrite('/api/items/reorder', 'POST', { body })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  const payload = (await r.json()) as { items: Item[] }
  return payload.items ?? []
}

export interface ApplySortResult {
  dryRun: boolean
  moved: number
  total: number
  groups: number
  movedIds: string[]
}

/** Bake the state sort into rank: every sibling group under `id` (only its
 * children when `shallow`), or the whole tree without an id. */
async function applySort(opts: { id?: string; shallow?: boolean; dryRun: boolean }): Promise<ApplySortResult> {
  const p = new URLSearchParams({ by: 'state' })
  if (opts.shallow) p.set('shallow', '1')
  if (opts.dryRun) p.set('dryRun', '1')
  const base = opts.id ? `/api/items/${encodeURIComponent(opts.id)}/apply-sort` : '/api/items/apply-sort'
  const r = await apiWrite(`${base}?${p}`, 'POST')
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  return (await r.json()) as ApplySortResult
}

async function retypeItem(
  id: string,
  body: RetypeRequest,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/retype`, 'POST', { body, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function addLink(
  id: string,
  body: CreateLinkRequest,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/links`, 'POST', { body, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

export interface EditLinkRequest {
  // The row as read — matched exactly server-side.
  rel: string
  targetPath: string
  targetText: string
  notes: string | null
  // Only between blocks / blocked-by / related.
  newRel?: LinkRel | null
}

async function editLink(
  id: string,
  body: EditLinkRequest,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/links`, 'PATCH', { body, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function deleteLink(
  id: string,
  rel: LinkRel,
  target: string,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  // Catch-all target route — KB refs contain `:` so don't pre-encode them.
  // `#` and `?` would end the path, though, and a shortened PR target
  // (`org/repo#42`) carries one.
  const safeTarget = target.replace(/#/g, '%23').replace(/\?/g, '%3F')
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/links/${encodeURIComponent(rel)}/${safeTarget}`, 'DELETE', { ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function addComment(
  id: string,
  body: CreateCommentRequest,
  ifMatch: string,
): Promise<{ result: CommentMutationResult; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/comments`, 'POST', { body, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const result = (await r.json()) as CommentMutationResult
  return { result, etag: r.headers.get('ETag') }
}

async function patchComment(
  id: string,
  commentId: string,
  body: string,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/comments/${encodeURIComponent(commentId)}`, 'PATCH', { body: { body }, ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

async function deleteComment(
  id: string,
  commentId: string,
  ifMatch: string,
): Promise<{ item: Item; etag: string | null }> {
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}/comments/${encodeURIComponent(commentId)}`, 'DELETE', { ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r), r.headers.get('ETag'))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') }
}

export interface DeleteResult {
  id: string
  cascaded: string[]
}

async function deleteItem(
  id: string,
  cascade: boolean,
  ifMatch: string,
): Promise<DeleteResult> {
  const q = cascade ? '?cascade=true' : ''
  const r = await apiWrite(`/api/items/${encodeURIComponent(id)}${q}`, 'DELETE', { ifMatch })
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  return (await r.json()) as DeleteResult
}

async function fetchItemWithEtag(id: string): Promise<{ item: Item; etag: string }> {
  const r = await fetch(`/api/items/${encodeURIComponent(id)}`)
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  const item = (await r.json()) as Item
  return { item, etag: r.headers.get('ETag') ?? '' }
}

/** Sibling order in tree views (`tree_sort`): the plan, or grouped by state (done on top). */
export type TreeSort = 'rank' | 'state'

export type SortKey = 'tree' | 'rank' | 'type' | 'state' | 'assignee' | 'created' | 'due' | 'priority'
export type SortDir = 'asc' | 'desc'
export type FilterMode = 'context' | 'flat'

export type BlockedFilter = 'only' | 'hide'

export interface ItemQuery {
  type?: string[]
  state?: string[]
  excludeType?: string[]
  excludeState?: string[]
  open?: boolean
  assignee?: string | null
  mine?: boolean
  unassigned?: boolean
  under?: string | null
  ready?: boolean
  query?: string | null
  unparented?: boolean
  blocked?: BlockedFilter
  overdue?: boolean
  dueBefore?: string | null
  dueAfter?: string | null
  minPriority?: string | null
  // AND semantics — item must carry every tag (case-insensitive).
  tag?: string[]
  // Matches on the effective (inherited) slate — mutually exclusive pair.
  slate?: string | null
  noSlate?: boolean
  // Milestone ref (<slug> or <project>/<slug>); server resolves + matches
  // items carrying a Milestone row targeting that sub-doc.
  milestone?: string | null
  // Project doc-folder slug; server matches items in that project's tree
  // (containment — nearest project-type ancestor points at the folder).
  project?: string | null
  sort?: { key: SortKey; dir: SortDir }
  filterMode?: FilterMode
  // Absent = the repo's `tree_sort`. Orders tree siblings and `sort=tree`.
  treeSort?: TreeSort
}

function buildQuery(q: ItemQuery): string {
  const params = new URLSearchParams()
  for (const v of q.type ?? []) params.append('type', v)
  for (const v of q.state ?? []) params.append('state', v)
  for (const v of q.excludeType ?? []) params.append('excludeType', v)
  for (const v of q.excludeState ?? []) params.append('excludeState', v)
  if (q.open) params.set('open', '1')
  if (q.assignee) params.set('assignee', q.assignee)
  if (q.mine) params.set('mine', '1')
  if (q.unassigned) params.set('unassigned', '1')
  if (q.under) params.set('under', q.under)
  if (q.ready) params.set('ready', '1')
  if (q.query) params.set('query', q.query)
  if (q.unparented) params.set('unparented', '1')
  if (q.blocked) params.set('blocked', q.blocked)
  if (q.overdue) params.set('overdue', '1')
  if (q.dueBefore) params.set('dueBefore', q.dueBefore)
  if (q.dueAfter) params.set('dueAfter', q.dueAfter)
  if (q.minPriority) params.set('minPriority', q.minPriority)
  for (const v of q.tag ?? []) params.append('tag', v)
  if (q.slate) params.set('slate', q.slate)
  if (q.noSlate) params.set('noSlate', '1')
  if (q.milestone) params.set('milestone', q.milestone)
  if (q.project) params.set('project', q.project)
  if (q.sort) params.set('sort', `${q.sort.key}:${q.sort.dir}`)
  if (q.filterMode) params.set('filterMode', q.filterMode)
  if (q.treeSort) params.set('treeSort', q.treeSort)
  const s = params.toString()
  return s ? `?${s}` : ''
}

// Shared item filters (the subset the timeline honors) + its own controls.
// Risk always computes over the full graph server-side — these only restrict
// what is rendered; out-of-filter at-risk blockers come back as ghost nodes.
export interface TimelineQuery extends ItemQuery {
  depth?: 'project' | 'epic' | 'feature' | 'all'
  includeDone?: boolean
  hideTodos?: boolean
  hideDisconnected?: boolean
}

function buildTimelineQuery(q: TimelineQuery): string {
  const { depth, includeDone, hideTodos, hideDisconnected, ...itemQuery } = q
  const base = buildQuery(itemQuery)
  const params = new URLSearchParams(base.startsWith('?') ? base.slice(1) : base)
  if (depth && depth !== 'all') params.set('depth', depth)
  if (includeDone) params.set('includeDone', '1')
  if (hideTodos) params.set('hideTodos', '1')
  if (hideDisconnected) params.set('hideDisconnected', '1')
  const s = params.toString()
  return s ? `?${s}` : ''
}

export interface SearchHit {
  kind: 'item' | 'kb' | 'design' | 'project'
  id: string
  title: string
  // Plain-text windowed excerpt around the match; the client highlights it
  // (no markup from the server — highlighting is a UI concern).
  snippet: string
  // Which region matched + the body char offset (when the match is in the
  // body) — consumed by jump-to-occurrence navigation.
  field: 'title' | 'body' | 'comment' | 'tags' | 'source' | 'link'
  bodyOffset: number | null
}

export interface SearchResult {
  // True when more hits matched than were returned (server capped the list).
  clipped: boolean
  hits: SearchHit[]
}

async function search(q: string, kind: 'all' | 'item' | 'kb' | 'design' | 'project' = 'all', limit = 100): Promise<SearchResult> {
  const params = new URLSearchParams({ q, kind, limit: String(limit) })
  const r = await fetch(`/api/search?${params.toString()}`)
  if (!r.ok) throw new ApiError(r.status, await parseProblem(r))
  return (await r.json()) as SearchResult
}

// Landing row: in-progress leaves first, then recency backfill.
// `mine` scopes the population to the caller's assignee. One capped fetch;
// `hasMore` flags items beyond the cap.
function itemsLanding(limit: number, mine: boolean): Promise<Paged<ItemSummary>> {
  const p = new URLSearchParams({ limit: String(limit) })
  if (mine) p.set('mine', '1')
  return fetchPaged<ItemSummary>(`/api/items/landing?${p.toString()}`)
}

/** GET /api/root — where the host's knit state lives. */
export interface RepoRoot {
  rootPath: string
  redirectedFrom?: string | null
  /** This worktree uses its own .knit/ undecided while another checkout has one. */
  unshared?: UnsharedWorktree | null
}

export interface UnsharedWorktree {
  /** The checkout to share, or null when none can be named. */
  target: string | null
  /** A bare repo's worktree — no main checkout, the share is by path. */
  bare: boolean
  /** The command that shares it. */
  command: string
}

export const api = {
  items: (q: ItemQuery = {}) => getJson<ItemSummary[]>(`/api/items${buildQuery(q)}`),
  itemsLanding,
  item: (id: string) => getJson<Item>(`/api/items/${encodeURIComponent(id)}`),
  itemContext: (id: string) => getJson<ItemContext>(`/api/items/${encodeURIComponent(id)}/context`),
  tree: (q: ItemQuery = {}) => getJson<TreeNode[]>(`/api/tree${buildQuery(q)}`),
  timeline: (q: TimelineQuery = {}) => getJson<Timeline>(`/api/timeline${buildTimelineQuery(q)}`),
  health: () => getJson<Health>('/api/health'),
  identity: () => getJson<Identity>('/api/identity'),
  root: () => getJson<RepoRoot>('/api/root'),
  version: () => getJson<VersionInfo>('/api/version'),
  activity: () => getJson<ActivityEntry[]>('/api/activity'),
  createItem,
  patchItem,
  blockItem,
  unblockItem,
  dismissItem,
  deleteItem,
  reorderItem,
  reorderItems,
  applySort,
  retypeItem,
  addLink,
  editLink,
  deleteLink,
  addComment,
  patchComment,
  deleteComment,
  fetchItemWithEtag,
  search,
}

export const qk = {
  items: ['items'] as const,
  itemsFiltered: (q: ItemQuery) => ['items', q] as const,
  // Keyed under 'items' so the SSE item invalidation refreshes the landing row.
  itemsLanding: (mine: boolean) => ['items', 'landing', mine] as const,
  item: (id: string) => ['item', id] as const,
  context: (id: string) => ['context', id] as const,
  tree: ['tree'] as const,
  treeFiltered: (q: ItemQuery) => ['tree', q] as const,
  timeline: ['timeline'] as const,
  timelineFiltered: (q: TimelineQuery) => ['timeline', q] as const,
  health: ['health'] as const,
  identity: ['identity'] as const,
  root: ['root'] as const,
  version: ['version'] as const,
}
