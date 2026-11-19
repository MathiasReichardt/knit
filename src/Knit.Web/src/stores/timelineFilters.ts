import { useEffect } from 'react'
import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { slatesApi, slatesQk } from '../api/slates'
import type { BlockedFilter, TimelineQuery } from '../api/client'
import type { ItemType, Priority } from '../api/types'
import { repoKey } from './treeOpen'
import { withoutHistory } from '../lib/urlHistory'

// The timeline's item filters — independent of the tree/list navigator, so
// each view keeps its own question. Persisted per repo like the navigator
// (a slate or milestone from repo A means nothing in repo B).
export interface TimelineFilters {
  types: ItemType[]
  assignee: string | null
  mine: boolean
  unassigned: boolean
  minPriority: Priority | null
  tags: string[]
  slate: string | null
  noSlate: boolean
  milestone: string | null
  project: string | null
  under: string | null
  overdue: boolean
  blocked: BlockedFilter | null
}

export const TIMELINE_FILTER_DEFAULTS: TimelineFilters = {
  types: [],
  assignee: null,
  mine: false,
  unassigned: false,
  minPriority: null,
  tags: [],
  slate: null,
  noSlate: false,
  milestone: null,
  project: null,
  under: null,
  overdue: false,
  blocked: null,
}

export interface TimelineFiltersState extends TimelineFilters {
  repo: string | null
  byRepo: Record<string, TimelineFilters>
  // A link's filters, applied in place of the remembered ones when the repo
  // binds — the link says what to show.
  linked: TimelineFilters | null

  toggleType: (t: ItemType) => void
  setAssignee: (a: string | null) => void
  setMine: (v: boolean) => void
  setUnassigned: (v: boolean) => void
  setMinPriority: (p: Priority | null) => void
  toggleTag: (t: string) => void
  clearTags: () => void
  setSlate: (name: string | null) => void
  setNoSlate: (v: boolean) => void
  setMilestone: (m: string | null) => void
  setProject: (slug: string | null) => void
  setUnder: (id: string | null) => void
  setOverdue: (v: boolean) => void
  setBlocked: (v: BlockedFilter | null) => void
  clearFilters: () => void

  applyLink: (f: TimelineFilters) => void
  setRepo: (repo: string) => void
  pruneSlate: (known: readonly string[]) => void
}

export function pickFilters(s: TimelineFilters): TimelineFilters {
  const out = {} as Record<string, unknown>
  for (const k of Object.keys(TIMELINE_FILTER_DEFAULTS)) out[k] = (s as unknown as Record<string, unknown>)[k]
  return out as unknown as TimelineFilters
}

export const useTimelineFilters = create<TimelineFiltersState>()(
  persist(
    (set) => ({
      repo: null,
      byRepo: {},
      linked: null,
      ...TIMELINE_FILTER_DEFAULTS,
      toggleType: (t) => set((s) => ({
        types: s.types.includes(t) ? s.types.filter((x) => x !== t) : [...s.types, t],
      })),
      setAssignee: (assignee) => set({ assignee }),
      setMine: (mine) => set({ mine }),
      setUnassigned: (unassigned) => set({ unassigned }),
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
      setOverdue: (overdue) => set({ overdue }),
      setBlocked: (blocked) => set({ blocked }),
      clearFilters: () => set({ ...TIMELINE_FILTER_DEFAULTS }),

      // Before the repo is known the link is parked; once bound it applies
      // directly (browser back/forward between two links).
      applyLink: (f) => set((s) => (s.repo ? { ...f } : { ...f, linked: f })),
      setRepo: (repo) =>
        set((s) => {
          if (s.repo === repo) return s
          const byRepo = s.repo ? { ...s.byRepo, [s.repo]: pickFilters(s) } : s.byRepo
          const next = s.linked ?? byRepo[repo] ?? TIMELINE_FILTER_DEFAULTS
          return { ...TIMELINE_FILTER_DEFAULTS, ...next, byRepo, repo, linked: null }
        }),
      pruneSlate: (known) =>
        set((s) => (s.slate && !known.includes(s.slate) ? { slate: null } : s)),
    }),
    {
      name: 'knit.timelineFilters',
      storage: createJSONStorage(() => localStorage),
      partialize: (s) => ({
        byRepo: s.repo ? { ...s.byRepo, [s.repo]: pickFilters(s) } : s.byRepo,
      }),
    },
  ),
)

/** Bind the timeline filters to the repo on screen — see `useNavigatorRepo`. */
export function useTimelineFiltersRepo() {
  const rootQ = useQuery({ queryKey: ['root'], queryFn: api.root, staleTime: Infinity })
  const key = rootQ.data ? repoKey(rootQ.data.rootPath) : null

  const setRepo = useTimelineFilters((s) => s.setRepo)
  useEffect(() => {
    if (key) setRepo(key)
  }, [key, setRepo])

  const repo = useTimelineFilters((s) => s.repo)
  const ready = key !== null && repo === key

  const slatesQ = useQuery({ queryKey: slatesQk.list, queryFn: slatesApi.list, enabled: ready })
  const slates = slatesQ.data
  useEffect(() => {
    if (slates) withoutHistory(() => useTimelineFilters.getState().pruneSlate(slates.map((s) => s.name)))
  }, [slates])

  return { ready }
}

export function timelineFilterQuery(f: TimelineFilters): TimelineQuery {
  return {
    type: f.types.length > 0 ? f.types : undefined,
    assignee: f.assignee || undefined,
    mine: f.mine || undefined,
    unassigned: f.unassigned || undefined,
    minPriority: f.minPriority ?? undefined,
    tag: f.tags.length > 0 ? f.tags : undefined,
    slate: f.slate || undefined,
    noSlate: f.noSlate || undefined,
    milestone: f.milestone || undefined,
    project: f.project || undefined,
    under: f.under || undefined,
    overdue: f.overdue || undefined,
    blocked: f.blocked ?? undefined,
  }
}

export function hasTimelineFilter(f: TimelineFilters): boolean {
  return Object.values(timelineFilterQuery(f)).some((v) => v !== undefined)
}
