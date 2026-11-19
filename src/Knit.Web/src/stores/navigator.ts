import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { ItemState, ItemType, Priority } from '../api/types'
import { api, type BlockedFilter, type FilterMode, type SortDir, type SortKey, type TreeSort } from '../api/client'
import { configApi, configQk } from '../api/config'
import { slatesApi, slatesQk } from '../api/slates'
import { repoKey } from './treeOpen'
import { withoutHistory } from '../lib/urlHistory'

export type NavView = 'tree' | 'list'

// Audience lens: a single exclusive preset, styled
// like the tree/list view toggle — a *view* selector, not a filter. Projects
// (and milestones) are always the structural frame; the lens only picks which
// leaf band shows. An explicit per-type chip include still overrides (see
// buildItemQuery).
export type LensPreset = 'pm' | 'dev' | 'all'

// Leaf types each preset HIDES. Projects are never hidden by the lens — hiding
// them would detach the tree (dev/todo items nest under their project).
const LENS_HIDDEN: Record<LensPreset, ItemType[]> = {
  dev: ['todo'], // engineering work; todos are PM noise
  pm: ['epic', 'feature', 'bug', 'task'], // planning leaves; dev items hidden
  all: [],
}

export interface NavSort {
  key: SortKey
  dir: SortDir
}

export interface NavigatorState {
  // Persistence key: the effective knit root, from /api/root. Null
  // until it resolves — nothing is stored or restored before that, which is
  // why consumers gate on `useNavigatorRepo().ready`.
  repo: string | null
  byRepo: Record<string, PersistedNavigator>
  // Keys this session took from the URL. They outrank whatever the repo had
  // stored: a link someone pasted says what to show, a remembered filter only
  // says what was shown last time.
  urlKeys: string[]

  view: NavView
  filterMode: FilterMode
  sort: NavSort
  // Tree sibling order; null follows the repo's `tree_sort` (see useTreeSort).
  treeSort: TreeSort | null
  // Filters
  types: ItemType[]
  states: ItemState[]
  excludeTypes: ItemType[]
  excludeStates: ItemState[]
  open: boolean
  query: string
  assignee: string | null
  mine: boolean
  unassigned: boolean
  ready: boolean
  unparented: boolean
  blocked: BlockedFilter | null
  overdue: boolean
  dueBefore: string | null
  dueAfter: string | null
  minPriority: Priority | null
  // Free-form tag filter — AND semantics (item must carry every selected tag).
  tags: string[]
  // Slate filter — matches on the effective (inherited) slate. `slate` and
  // `noSlate` are mutually exclusive; the setters clear the other side.
  slate: string | null
  noSlate: boolean
  // Milestone ref filter (<slug> or <project>/<slug>); single-select.
  milestone: string | null
  // Project doc-folder slug filter (containment scope); single-select.
  project: string | null
  under: string | null
  // Audience lens — default 'dev' (dev items + projects framed, todos hidden),
  // matching the dev/items view. The Projects nav ignores this; it has its own
  // fixed sections. An explicit per-type chip include overrides (buildItemQuery).
  lens: LensPreset

  setView: (v: NavView) => void
  setFilterMode: (m: FilterMode) => void
  setSort: (s: NavSort) => void
  setTreeSort: (t: TreeSort) => void
  toggleType: (t: ItemType) => void
  cycleState: (s: ItemState) => void
  setOpen: (v: boolean) => void
  setQuery: (q: string) => void
  setAssignee: (a: string | null) => void
  setMine: (v: boolean) => void
  setUnassigned: (v: boolean) => void
  setReady: (v: boolean) => void
  setUnparented: (v: boolean) => void
  setBlocked: (v: BlockedFilter | null) => void
  setOverdue: (v: boolean) => void
  setDueBefore: (d: string | null) => void
  setDueAfter: (d: string | null) => void
  setMinPriority: (p: Priority | null) => void
  toggleTag: (t: string) => void
  clearTags: () => void
  setSlate: (name: string | null) => void
  setNoSlate: (v: boolean) => void
  setMilestone: (m: string | null) => void
  setProject: (slug: string | null) => void
  setUnder: (id: string | null) => void
  setLens: (lens: LensPreset) => void
  clearFilters: () => void

  setRepo: (repo: string) => void
  noteUrlKeys: (keys: string[]) => void
  /** Drop a remembered slate the repo no longer has — see `useNavigatorRepo`. */
  pruneSlate: (known: readonly string[]) => void
}

/** Everything persisted for one repo: the whole store minus the transients. */
export type PersistedNavigator = Partial<Omit<NavigatorState, keyof NavigatorTransient>>

interface NavigatorTransient {
  repo: NavigatorState['repo']
  byRepo: NavigatorState['byRepo']
  urlKeys: NavigatorState['urlKeys']
}

// `partialize` is inverted on purpose: persist everything except what is
// explicitly transient, so the *next* filter someone adds is remembered by
// default. The whitelist this replaced held six of twenty keys, which is how
// "pick a slate, reload, every item is back" shipped.
const TRANSIENT = new Set<string>(['repo', 'byRepo', 'urlKeys'])

const persistable = (s: NavigatorState): PersistedNavigator =>
  Object.fromEntries(
    Object.entries(s).filter(([k, v]) => typeof v !== 'function' && !TRANSIENT.has(k)),
  ) as PersistedNavigator

const defaultSort: NavSort = { key: 'tree', dir: 'asc' }

/** The unfiltered navigator — also what binding to a new repo falls back to. */
export const NAVIGATOR_DEFAULTS = {
  view: 'tree',
  filterMode: 'context',
  sort: defaultSort,
  treeSort: null,
  types: [],
  states: [],
  excludeTypes: [],
  excludeStates: ['dismissed'],
  open: false,
  query: '',
  assignee: null,
  mine: false,
  unassigned: false,
  ready: false,
  unparented: false,
  blocked: null,
  overdue: false,
  dueBefore: null,
  dueAfter: null,
  minPriority: null,
  tags: [],
  slate: null,
  noSlate: false,
  milestone: null,
  project: null,
  under: null,
  lens: 'dev',
} satisfies PersistedNavigator

export const useNavigator = create<NavigatorState>()(
  persist(
    (set) => ({
      repo: null,
      byRepo: {},
      urlKeys: [],
      ...NAVIGATOR_DEFAULTS,
      setView: (view) => set({ view }),
      setFilterMode: (filterMode) => set({ filterMode }),
      setSort: (sort) => set({ sort }),
      setTreeSort: (treeSort) => set({ treeSort }),
      toggleType: (t) => set((s) => ({
        types: s.types.includes(t) ? s.types.filter((x) => x !== t) : [...s.types, t],
      })),
      cycleState: (st) => set((s) => {
        // tri-state cycle: unfiltered → include → exclude → unfiltered
        const isIncluded = s.states.includes(st)
        const isExcluded = s.excludeStates.includes(st)
        if (!isIncluded && !isExcluded) {
          return {
            states: [...s.states, st],
            open: false,
          }
        }
        if (isIncluded) {
          return {
            states: s.states.filter((x) => x !== st),
            excludeStates: [...s.excludeStates, st],
            open: false,
          }
        }
        return {
          excludeStates: s.excludeStates.filter((x) => x !== st),
        }
      }),
      setOpen: (open) => set((s) => ({
        open,
        // turning `open` on clears state include/exclude (mutually exclusive)
        states: open ? [] : s.states,
        excludeStates: open ? [] : s.excludeStates,
      })),
      setQuery: (query) => set({ query }),
      setAssignee: (assignee) => set({ assignee }),
      setMine: (mine) => set({ mine }),
      setUnassigned: (unassigned) => set({ unassigned }),
      setReady: (ready) => set({ ready }),
      setUnparented: (unparented) => set({ unparented }),
      setBlocked: (blocked) => set({ blocked }),
      setOverdue: (overdue) => set({ overdue }),
      setDueBefore: (dueBefore) => set({ dueBefore }),
      setDueAfter: (dueAfter) => set({ dueAfter }),
      setMinPriority: (minPriority) => set({ minPriority }),
      toggleTag: (t) => set((s) => ({
        tags: s.tags.includes(t) ? s.tags.filter((x) => x !== t) : [...s.tags, t],
      })),
      clearTags: () => set({ tags: [] }),
      setSlate: (slate) => set({ slate, noSlate: false }),
      setNoSlate: (noSlate) => set((s) => ({ noSlate, slate: noSlate ? null : s.slate })),
      setMilestone: (milestone) => set({ milestone }),
      setProject: (project) => set({ project }),
      setUnder: (under) => set({ under }),
      setLens: (lens) => set({ lens }),
      clearFilters: () => set({
        types: [],
        states: [],
        excludeTypes: [],
        excludeStates: ['dismissed'],
        open: false,
        query: '',
        assignee: null,
        mine: false,
        unassigned: false,
        ready: false,
        unparented: false,
        blocked: null,
        overdue: false,
        dueBefore: null,
        dueAfter: null,
        minPriority: null,
        tags: [],
        slate: null,
        noSlate: false,
        milestone: null,
        project: null,
        under: null,
        // lens is a view preference (like view/filterMode/sort), not a filter —
        // clearing filters leaves the chosen lens untouched.
      }),

      // Binding to a repo swaps the whole filter set: a slate or milestone
      // from repo A means nothing in repo B, and a shared key would carry it
      // across. Anything the URL supplied this session is re-applied on top.
      setRepo: (repo) =>
        set((s) => {
          if (s.repo === repo) return s
          // Fold what is on screen back into the repo it belongs to before
          // swapping: `partialize` writes to storage but never back into
          // state, so without this a return to the previous repo would restore
          // whatever was last rehydrated instead of what was just used.
          const byRepo = s.repo ? { ...s.byRepo, [s.repo]: persistable(s) } : s.byRepo
          const fromUrl = Object.fromEntries(
            s.urlKeys.map((k) => [k, (s as unknown as Record<string, unknown>)[k]]),
          )
          return {
            ...NAVIGATOR_DEFAULTS,
            ...(byRepo[repo] ?? {}),
            ...fromUrl,
            byRepo,
            repo,
          }
        }),
      noteUrlKeys: (keys) =>
        set((s) => (keys.length === 0 ? s : { urlKeys: [...new Set([...s.urlKeys, ...keys])] })),
      pruneSlate: (known) =>
        set((s) => (s.slate && !known.includes(s.slate) ? { slate: null } : s)),
    }),
    {
      name: 'knit.navigator',
      storage: createJSONStorage(() => localStorage),
      // Per repo, and everything but the transients — see TRANSIENT above.
      partialize: (s) => ({
        byRepo: s.repo ? { ...s.byRepo, [s.repo]: persistable(s) } : s.byRepo,
      }),
    },
  ),
)

/**
 * Bind the navigator to the repo it is looking at, the way `useTreeOpen` binds
 * the trees. Two things happen here and nowhere else:
 *
 * - **The key arrives late.** `/api/root` has to resolve before anything can
 *   be restored, so `ready` is false until it has — views hold their queries
 *   until then rather than fetching unfiltered and snapping.
 * - **A remembered slate can be gone.** The slate may have been renamed or
 *   deleted since, and a filter that silently matches nothing reads as an
 *   empty repo, so it is dropped on restore instead of left set.
 */
export function useNavigatorRepo() {
  const rootQ = useQuery({ queryKey: ['root'], queryFn: api.root, staleTime: Infinity })
  const key = rootQ.data ? repoKey(rootQ.data.rootPath) : null

  const setRepo = useNavigator((s) => s.setRepo)
  useEffect(() => {
    if (key) setRepo(key)
  }, [key, setRepo])

  const repo = useNavigator((s) => s.repo)
  const ready = key !== null && repo === key

  const slatesQ = useQuery({ queryKey: slatesQk.list, queryFn: slatesApi.list, enabled: ready })
  const slates = slatesQ.data
  useEffect(() => {
    if (slates) withoutHistory(() => useNavigator.getState().pruneSlate(slates.map((s) => s.name)))
  }, [slates])

  return { ready }
}

// buildItemQuery is read through `useShallow` (TreeView/ListView), which
// shallow-compares the selector output. Any freshly-built array under a key
// makes that comparison fail every render → infinite re-render loop (React
// #185). So derived arrays must be referentially stable *per immutable state
// snapshot*: cache the result keyed on the snapshot object (zustand replaces it
// on every `set`, so the key changes exactly when the inputs might have).
const excludeTypesCache = new WeakMap<NavigatorState, ItemType[]>()

/**
 * Translate the audience lens into an excludeType set: the active preset
 * contributes the leaf band it hides, an explicit per-type chip include
 * (s.types) overrides it, and any pre-existing excludeTypes are unioned in.
 * Exported for the unit test. Stable per snapshot (see excludeTypesCache).
 */
export function lensExcludeTypes(s: NavigatorState): ItemType[] {
  const cached = excludeTypesCache.get(s)
  if (cached) return cached
  const excluded = new Set<ItemType>(s.excludeTypes)
  for (const t of LENS_HIDDEN[s.lens]) excluded.add(t)
  for (const t of s.types) excluded.delete(t)
  const arr = Array.from(excluded)
  excludeTypesCache.set(s, arr)
  return arr
}

/** Project navigator state into an ItemQuery for /api/items + /api/tree. */
export function buildItemQuery(s: NavigatorState) {
  const excludeType = lensExcludeTypes(s)
  return {
    type: s.types.length > 0 ? s.types : undefined,
    state: s.states.length > 0 ? s.states : undefined,
    excludeType: excludeType.length > 0 ? excludeType : undefined,
    excludeState: s.excludeStates.length > 0 ? s.excludeStates : undefined,
    open: s.open || undefined,
    assignee: s.assignee || undefined,
    mine: s.mine || undefined,
    unassigned: s.unassigned || undefined,
    ready: s.ready || undefined,
    query: s.query || undefined,
    unparented: s.unparented || undefined,
    blocked: s.blocked ?? undefined,
    overdue: s.overdue || undefined,
    dueBefore: s.dueBefore || undefined,
    dueAfter: s.dueAfter || undefined,
    minPriority: s.minPriority ?? undefined,
    tag: s.tags.length > 0 ? s.tags : undefined,
    slate: s.slate || undefined,
    noSlate: s.noSlate || undefined,
    milestone: s.milestone || undefined,
    project: s.project || undefined,
    under: s.under || undefined,
    sort: s.sort,
    filterMode: s.filterMode,
    treeSort: s.treeSort ?? undefined,
  }
}

/**
 * The tree's sibling order as shown: the viewer's pick, else the repo's
 * `tree_sort`. The server applies the same fallback, so this only has to agree
 * with it — drop rules (ST6) depend on which order is on screen.
 */
export function useTreeSort(): TreeSort {
  const chosen = useNavigator((s) => s.treeSort)
  const cfg = useQuery({ queryKey: configQk.get(), queryFn: configApi.get, enabled: chosen === null })
  if (chosen) return chosen
  return cfg.data?.keys.find((k) => k.key === 'tree_sort')?.value === 'state' ? 'state' : 'rank'
}

export function hasAnyActiveFilter(s: NavigatorState): boolean {
  // excludeStates defaults to ['dismissed']; only count deviations from that.
  const defaultExclude =
    s.excludeStates.length === 1 && s.excludeStates[0] === 'dismissed'
  // The lens is a view preference (like tree/list), not a filter — excluded here.
  return (
    s.types.length > 0 ||
    s.states.length > 0 ||
    s.excludeTypes.length > 0 ||
    !defaultExclude ||
    s.open ||
    !!s.query ||
    !!s.assignee ||
    s.mine ||
    s.unassigned ||
    s.ready ||
    s.unparented ||
    s.blocked !== null ||
    s.overdue ||
    !!s.dueBefore ||
    !!s.dueAfter ||
    s.minPriority !== null ||
    s.tags.length > 0 ||
    !!s.slate ||
    s.noSlate ||
    !!s.milestone ||
    !!s.project ||
    !!s.under
  )
}
