import { create } from 'zustand'

export interface ItemRevealRequest {
  id: string
  /** Bumped on every request, so jumping to the already-selected item still reveals it. */
  nonce: number
  at: number
}

// Long enough for a route change to mount the items view; the view then holds
// the target itself while its data loads.
const FRESH_MS = 2000

interface ItemRevealState {
  target: ItemRevealRequest | null
  request: (id: string) => void
  /** Clears the request, returning it only if it is still fresh. */
  take: () => ItemRevealRequest | null
}

/**
 * An explicit jump to an item (jump-to-ref, a search pick, a body ref) asks the
 * items view to scroll to it and flash its row; the tree also opens the target
 * and its ancestors. Separate from the tree's selection-driven reveal, which is
 * keyed on the selection *changing* and so skips a jump to the item already
 * selected.
 *
 * Never queued: a body ref followed on /projects or /timeline has no items view
 * to take it, and a request that waited for the next /items visit would flash
 * an item nobody just asked for — so a stale request is dropped on take.
 */
export const useItemReveal = create<ItemRevealState>((set, get) => ({
  target: null,
  request: (id) =>
    set((s) => ({ target: { id, nonce: (s.target?.nonce ?? 0) + 1, at: Date.now() } })),
  take: () => {
    const t = get().target
    if (t) set({ target: null })
    return t && Date.now() - t.at <= FRESH_MS ? t : null
  },
}))
