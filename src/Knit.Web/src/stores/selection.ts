import { create } from 'zustand'

export interface SelectionState {
  selected: Set<string>
  /** Anchor id for shift-click range selection. */
  anchor: string | null
  /** Last contiguous order observed (used to resolve shift-click ranges). */
  order: string[]
  setOrder: (ids: string[]) => void
  click: (id: string, mods: { shift: boolean; meta: boolean }) => void
  set: (ids: Iterable<string>) => void
  clear: () => void
}

export const useSelection = create<SelectionState>((set, get) => ({
  selected: new Set(),
  anchor: null,
  order: [],
  setOrder: (ids) => set({ order: ids }),
  click: (id, mods) => {
    const s = get()
    if (mods.shift && s.anchor) {
      const a = s.order.indexOf(s.anchor)
      const b = s.order.indexOf(id)
      if (a >= 0 && b >= 0) {
        const [lo, hi] = a < b ? [a, b] : [b, a]
        const range = new Set(s.order.slice(lo, hi + 1))
        set({ selected: range })
        return
      }
    }
    if (mods.meta) {
      const next = new Set(s.selected)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      set({ selected: next, anchor: id })
      return
    }
    set({ selected: new Set([id]), anchor: id })
  },
  set: (ids) => set({ selected: new Set(ids) }),
  clear: () => set({ selected: new Set(), anchor: null }),
}))
