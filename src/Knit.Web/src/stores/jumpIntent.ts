import { create } from 'zustand'

/** Which entity region the search match landed in (mirrors SearchHit.field). */
export type JumpField = 'title' | 'body' | 'comment' | 'tags' | 'source' | 'link'

/** Which detail pane should consume the intent. */
export type JumpKind = 'item' | 'design' | 'kb'

export interface JumpIntent {
  kind: JumpKind
  /** Item id for `item`, design slug for `design`, `bucket:slug` ref for `kb`. */
  targetId: string
  field: JumpField
  /** Char offset of the match within the body — present only for body hits. */
  bodyOffset: number | null
  /** The search text, re-found in the rendered region to highlight the word. */
  query: string
  /** Bumped on every request so re-picking the same hit replays the flash. */
  nonce: number
}

interface JumpIntentState {
  intent: JumpIntent | null
  request: (target: Omit<JumpIntent, 'nonce'>) => void
  consume: () => void
}

/**
 * Carries a search pick's matched region from the global SearchBox to
 * the item / design details pane, which scrolls + flashes the region on
 * arrival. The URL only holds the entity id/slug; the region lands here instead
 * so it survives a remount, doesn't pollute shareable links, and — via the
 * nonce — replays the flash when the same hit is re-picked. The pane clears it
 * with `consume()` after jumping, so navigating back later doesn't re-flash.
 */
export const useJumpIntent = create<JumpIntentState>((set) => ({
  intent: null,
  request: (target) =>
    set((s) => ({ intent: { ...target, nonce: (s.intent?.nonce ?? 0) + 1 } })),
  consume: () => set({ intent: null }),
}))
