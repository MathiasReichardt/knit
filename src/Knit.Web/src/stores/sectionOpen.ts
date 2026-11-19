import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface SectionOpenState {
  byKey: Record<string, boolean>
  setOpen: (key: string, open: boolean) => void
}

/**
 * Per-section collapse state, keyed by a stable string (e.g. "details.links").
 * Global, not per-item — the user's preference for "links collapsed" sticks
 * across items and reloads.
 */
export const useSectionOpen = create<SectionOpenState>()(
  persist(
    (set) => ({
      byKey: {},
      setOpen: (key, open) =>
        set((s) => ({ byKey: { ...s.byKey, [key]: open } })),
    }),
    { name: 'knit.sectionOpen', storage: createJSONStorage(() => localStorage) },
  ),
)
