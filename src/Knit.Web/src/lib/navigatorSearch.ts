import { z } from 'zod'
import type { ItemState, ItemType } from '../api/types'
import { NAVIGATOR_DEFAULTS, type NavigatorState } from '../stores/navigator'

const TYPES = ['project', 'epic', 'feature', 'bug', 'task', 'todo'] as const
const STATES = ['draft', 'todo', 'in-progress', 'review', 'done', 'dismissed'] as const
const BLOCKED_FILTERS = ['only', 'hide'] as const
const VIEWS = ['tree', 'list'] as const
const MODES = ['context', 'flat'] as const
const SORT_KEYS = ['tree', 'rank', 'type', 'state', 'assignee', 'created', 'due'] as const
const SORT_DIRS = ['asc', 'desc'] as const

export const boolish = z.preprocess(
  (v) => {
    if (v === undefined || v === null) return undefined
    if (typeof v === 'boolean') return v
    if (v === '1' || v === 'true') return true
    if (v === '0' || v === 'false' || v === '') return false
    return undefined
  },
  z.boolean().optional(),
)

export const typeArray = z.preprocess(
  (v) => (v === undefined ? undefined : Array.isArray(v) ? v : [v]),
  z.array(z.enum(TYPES)).optional(),
)
const stateArray = z.preprocess(
  (v) => (v === undefined ? undefined : Array.isArray(v) ? v : [v]),
  z.array(z.enum(STATES)).optional(),
)
export const stringArray = z.preprocess(
  (v) => (v === undefined ? undefined : Array.isArray(v) ? v : [v]),
  z.array(z.string()).optional(),
)

export const navigatorSearchSchema = z.object({
  view: z.enum(VIEWS).optional(),
  filterMode: z.enum(MODES).optional(),
  sort: z.string().optional(),
  type: typeArray,
  state: stateArray,
  excludeType: typeArray,
  excludeState: stateArray,
  open: boolish,
  mine: boolish,
  unassigned: boolish,
  ready: boolish,
  unparented: boolish,
  blocked: z.enum(BLOCKED_FILTERS).optional(),
  overdue: boolish,
  dueBefore: z.string().optional(),
  dueAfter: z.string().optional(),
  tag: stringArray,
  slate: z.string().optional(),
  noSlate: boolish,
  milestone: z.string().optional(),
  project: z.string().optional(),
  minPriority: z.enum(['someday', 'normal', 'important', 'critical']).optional(),
  query: z.string().optional(),
  assignee: z.string().optional(),
  under: z.string().optional(),
  // Selected item id — not in NavigatorState; lives in the URL so the
  // browser back button moves between previously-viewed items.
  id: z.string().optional(),
})

export type NavigatorSearch = z.infer<typeof navigatorSearchSchema>

/** The navigator state keys the URL carries — all of them, see `searchToState`. */
export type UrlState = Pick<NavigatorState, (typeof URL_STATE_KEYS)[number]>

export const URL_STATE_KEYS = [
  'view', 'filterMode', 'sort', 'types', 'states', 'excludeTypes', 'excludeStates', 'open',
  'mine', 'unassigned', 'ready', 'unparented', 'blocked', 'overdue', 'dueBefore', 'dueAfter',
  'minPriority', 'tags', 'slate', 'noSlate', 'milestone', 'project', 'query', 'assignee', 'under',
] as const satisfies readonly (keyof NavigatorState)[]

/** True when the URL names a view; false for a bare `/items` (or one carrying only `id`). */
export function isSpec(s: NavigatorSearch): boolean {
  return Object.keys(filterPart(s)).length > 0
}

/** The search minus the selected `id` — the part that mirrors the store. */
export function filterPart(s: NavigatorSearch): NavigatorSearch {
  return Object.fromEntries(Object.entries(s).filter(([k, v]) => k !== 'id' && v !== undefined))
}

/**
 * The full state a URL names. A key it leaves out means the default, not
 * whatever is on screen — otherwise Back to an entry without a filter would
 * keep the filter.
 */
export function searchToState(s: NavigatorSearch): UrlState {
  const d = NAVIGATOR_DEFAULTS
  // `open` excludes state filters (the API rejects the pair), so an open URL
  // drops them — including the default dismissed exclusion it leaves out.
  const open = s.open ?? d.open
  return {
    view: s.view ?? d.view,
    filterMode: s.filterMode ?? d.filterMode,
    sort: parseSort(s.sort) ?? d.sort,
    types: (s.type as ItemType[] | undefined) ?? d.types,
    states: open ? [] : (s.state as ItemState[] | undefined) ?? d.states,
    excludeTypes: (s.excludeType as ItemType[] | undefined) ?? d.excludeTypes,
    excludeStates: open ? [] : (s.excludeState as ItemState[] | undefined) ?? d.excludeStates,
    open,
    mine: s.mine ?? d.mine,
    unassigned: s.unassigned ?? d.unassigned,
    ready: s.ready ?? d.ready,
    unparented: s.unparented ?? d.unparented,
    blocked: s.blocked ?? d.blocked,
    overdue: s.overdue ?? d.overdue,
    dueBefore: s.dueBefore || d.dueBefore,
    dueAfter: s.dueAfter || d.dueAfter,
    minPriority: s.minPriority ?? d.minPriority,
    tags: s.tag ?? d.tags,
    slate: s.slate || d.slate,
    noSlate: s.noSlate ?? d.noSlate,
    milestone: s.milestone || d.milestone,
    project: s.project || d.project,
    query: s.query ?? d.query,
    assignee: s.assignee || d.assignee,
    under: s.under || d.under,
  }
}

function parseSort(raw: string | undefined): NavigatorState['sort'] | null {
  if (!raw) return null
  const [key, dir = 'asc'] = raw.split(':')
  if (!(SORT_KEYS as readonly string[]).includes(key) || !(SORT_DIRS as readonly string[]).includes(dir)) {
    return null
  }
  return { key: key as (typeof SORT_KEYS)[number], dir: dir as (typeof SORT_DIRS)[number] }
}

export function stateToSearch(s: NavigatorState): NavigatorSearch {
  // `view` is always written so a synced URL is never bare — bare means "show
  // what is remembered" (see `isSpec`), and an unfiltered view must not read so.
  const out: NavigatorSearch = { view: s.view }
  if (s.filterMode !== 'context') out.filterMode = s.filterMode
  if (s.sort.key !== 'tree' || s.sort.dir !== 'asc') {
    out.sort = `${s.sort.key}:${s.sort.dir}`
  }
  if (s.types.length) out.type = s.types
  if (s.states.length && !s.open) out.state = s.states
  if (s.excludeTypes.length) out.excludeType = s.excludeTypes
  // Off-default only; `[]` is written as such ("show dismissed too").
  if (!s.open && !sameMembers(s.excludeStates, NAVIGATOR_DEFAULTS.excludeStates)) out.excludeState = s.excludeStates
  if (s.open) out.open = true
  if (s.mine) out.mine = true
  if (s.unassigned) out.unassigned = true
  if (s.ready) out.ready = true
  if (s.unparented) out.unparented = true
  if (s.blocked) out.blocked = s.blocked
  if (s.overdue) out.overdue = true
  if (s.dueBefore) out.dueBefore = s.dueBefore
  if (s.dueAfter) out.dueAfter = s.dueAfter
  if (s.minPriority) out.minPriority = s.minPriority
  if (s.tags.length) out.tag = s.tags
  if (s.slate) out.slate = s.slate
  if (s.noSlate) out.noSlate = true
  if (s.milestone) out.milestone = s.milestone
  if (s.project) out.project = s.project
  if (s.query) out.query = s.query
  if (s.assignee) out.assignee = s.assignee
  if (s.under) out.under = s.under
  return out
}

function sameMembers(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((x) => b.includes(x))
}

export function searchEqual(a: NavigatorSearch, b: NavigatorSearch): boolean {
  // Stable JSON: keys sorted, arrays compared by structure.
  return JSON.stringify(canonical(a)) === JSON.stringify(canonical(b))
}

function canonical(s: NavigatorSearch): unknown {
  const keys = Object.keys(s).sort()
  const obj: Record<string, unknown> = {}
  for (const k of keys) {
    const v = (s as Record<string, unknown>)[k]
    if (v === undefined) continue
    obj[k] = v
  }
  return obj
}
