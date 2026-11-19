import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import type { ActivityCategory, ActivityEntry } from '../api/types'

const CAP = 500

/** Categories that have a filter chip. `other` has no chip — always shown. */
export const ACTIVITY_CHIPS: { cat: ActivityCategory; label: string }[] = [
  { cat: 'created', label: 'Creates' },
  { cat: 'state', label: 'State changes' },
  { cat: 'comment', label: 'Comments' },
  { cat: 'kb', label: 'KB edits' },
]

type Filters = Record<ActivityCategory, boolean>

const ALL_ON: Filters = {
  created: true,
  state: true,
  comment: true,
  kb: true,
  other: true,
}

interface ActivityState {
  entries: ActivityEntry[] // newest first
  open: boolean
  filters: Filters
  setOpen: (v: boolean) => void
  toggleOpen: () => void
  toggleFilter: (cat: ActivityCategory) => void
  push: (entry: ActivityEntry) => void
  hydrate: (entries: ActivityEntry[]) => void
}

/** Insert/merge keeping the list newest-first and deduped by `seq`. */
function merge(existing: ActivityEntry[], incoming: ActivityEntry[]): ActivityEntry[] {
  const bySeq = new Map<number, ActivityEntry>()
  for (const e of existing) bySeq.set(e.seq, e)
  for (const e of incoming) bySeq.set(e.seq, e)
  return [...bySeq.values()].sort((a, b) => b.seq - a.seq).slice(0, CAP)
}

export const useActivity = create<ActivityState>()(
  persist(
    (set, get) => ({
      entries: [],
      open: true,
      filters: { ...ALL_ON },
      setOpen: (open) => set({ open }),
      toggleOpen: () => set({ open: !get().open }),
      toggleFilter: (cat) =>
        set((s) => ({ filters: { ...s.filters, [cat]: !s.filters[cat] } })),
      push: (entry) => set((s) => ({ entries: merge(s.entries, [entry]) })),
      // Merge (not replace) so a live entry that arrived during the fetch
      // isn't dropped by a slightly-stale buffer snapshot.
      hydrate: (entries) => set((s) => ({ entries: merge(s.entries, entries) })),
    }),
    {
      name: 'knit.activity',
      storage: createJSONStorage(() => localStorage),
      // Entries are session-only; persist only the UI prefs.
      partialize: (s) => ({ open: s.open, filters: s.filters }),
    },
  ),
)

/** True when this entry's category is currently shown. `other` is always on. */
export function isVisible(filters: Filters, cat: ActivityCategory): boolean {
  return cat === 'other' ? true : filters[cat]
}
