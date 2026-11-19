import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

// Shared view controls that shape how items are *displayed* (not filtered),
// persisted like the theme toggle so the choice survives reloads. Applies
// across tree + list today; the timeline reads the same flag.
// Which due dates the tree and list show; closed items' dates count as not overdue.
export type DueDisplay = 'all' | 'overdue' | 'none'

export interface ViewPrefs {
  colorByPriority: boolean
  // Summary strip above the tree/list: completion, blocked and overdue counts.
  showSummary: boolean
  // Short id beside each title in tree + list — for finding the id an agent
  // quoted. The details pane always shows it.
  showIds: boolean
  showTags: boolean
  due: DueDisplay
}

export const VIEW_PREF_DEFAULTS: ViewPrefs = {
  colorByPriority: false,
  showSummary: false,
  showIds: true,
  showTags: true,
  due: 'all',
}

export function isViewCustomized(prefs: ViewPrefs): boolean {
  return (Object.keys(VIEW_PREF_DEFAULTS) as (keyof ViewPrefs)[]).some(
    (k) => prefs[k] !== VIEW_PREF_DEFAULTS[k],
  )
}

interface ViewPrefsState extends ViewPrefs {
  set: <K extends keyof ViewPrefs>(key: K, value: ViewPrefs[K]) => void
  toggleColorByPriority: () => void
}

export const useViewPrefs = create<ViewPrefsState>()(
  persist(
    (set) => ({
      ...VIEW_PREF_DEFAULTS,
      set: (key, value) => set({ [key]: value } as Pick<ViewPrefs, typeof key>),
      toggleColorByPriority: () => set((s) => ({ colorByPriority: !s.colorByPriority })),
    }),
    {
      name: 'knit.viewPrefs',
      storage: createJSONStorage(() => localStorage),
      partialize: (s): ViewPrefs => ({
        colorByPriority: s.colorByPriority,
        showSummary: s.showSummary,
        showIds: s.showIds,
        showTags: s.showTags,
        due: s.due,
      }),
    },
  ),
)
