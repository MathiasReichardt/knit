import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import { addMonthsIso, type TimelineDepth, type TimelineRange } from '../lib/timeline'
import { todayIso } from '../lib/dueStatus'

// The timeline's own view controls: depth-by-type rollup,
// named time range, hide-done (default on). Persisted like the theme toggle;
// the item filters live in the timelineFilters store.
interface TimelinePrefsState {
  depth: TimelineDepth
  range: TimelineRange
  hideDone: boolean
  // Todos show by default here — deadline triage wins over the
  // dev-view default-hide; the toggle turns them off.
  hideTodos: boolean
  // Chain roots too fine for the selected depth (an item whose parent chain
  // never reaches the rank) show by default; the toggle hides them
  // for a clean "real <rank> only" view. No effect at depth 'all'.
  hideDisconnected: boolean
  // Milestone overlay: on by default so the roadmap context shows.
  showMilestones: boolean
  // The pick behind range 'custom'. Kept beside `range` rather than in
  // it, so re-selecting 'custom' restores the last window instead of an empty
  // form — and so picking a preset is all it takes to clear it.
  customFrom: string
  customTo: string
  // The overdue back-extension: on by default — overdue work must not
  // be invisible — but off is the escape hatch when one long-finished item
  // drags a 'week' view back over four months.
  showLookBack: boolean
  setDepth: (d: TimelineDepth) => void
  setRange: (r: TimelineRange) => void
  setCustomWindow: (from: string, to: string) => void
  setShowLookBack: (v: boolean) => void
  setHideDone: (v: boolean) => void
  setHideTodos: (v: boolean) => void
  setHideDisconnected: (v: boolean) => void
  setShowMilestones: (v: boolean) => void
}

export type TimelineView = Omit<TimelinePrefsState, `set${string}`>

/** The unconfigured view; the custom window defaults relative to today. */
export function timelineViewDefaults(): TimelineView {
  return {
    depth: 'all',
    range: 'month',
    hideDone: true,
    hideTodos: false,
    hideDisconnected: false,
    showMilestones: true,
    customFrom: todayIso(),
    customTo: addMonthsIso(todayIso(), 3),
    showLookBack: true,
  }
}

export const useTimelinePrefs = create<TimelinePrefsState>()(
  persist(
    (set) => ({
      ...timelineViewDefaults(),
      setDepth: (depth) => set({ depth }),
      setRange: (range) => set({ range }),
      setCustomWindow: (customFrom, customTo) => set({ customFrom, customTo }),
      setShowLookBack: (showLookBack) => set({ showLookBack }),
      setHideDone: (hideDone) => set({ hideDone }),
      setHideTodos: (hideTodos) => set({ hideTodos }),
      setHideDisconnected: (hideDisconnected) => set({ hideDisconnected }),
      setShowMilestones: (showMilestones) => set({ showMilestones }),
    }),
    {
      name: 'knit.timelinePrefs',
      storage: createJSONStorage(() => localStorage),
    },
  ),
)
