import { create } from 'zustand'
import type { Item } from '../api/types'
import { closedBlockers, type ClosedBlocker } from '../lib/blockedDependents'

interface BlockedDependentsState {
  groups: ClosedBlocker[]
  /** Outcome per resolved row, keyed `<blocker>/<dependent>`. */
  resolved: Record<string, string>
  /** Open the dialog for what a close call returned; a no-op when nothing depends. */
  showFor: (items: Item[]) => void
  markResolved: (key: string, outcome: string) => void
  close: () => void
}

export const useBlockedDependents = create<BlockedDependentsState>((set) => ({
  groups: [],
  resolved: {},
  showFor: (items) => {
    const groups = closedBlockers(items)
    if (groups.length > 0) set({ groups, resolved: {} })
  },
  markResolved: (key, outcome) => set((s) => ({ resolved: { ...s.resolved, [key]: outcome } })),
  close: () => set({ groups: [], resolved: {} }),
}))
