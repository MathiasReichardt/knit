import { useEffect, useRef } from 'react'
import { create } from 'zustand'

interface EditIntentState {
  nonce: number
  /** Request that the active screen's editor enter edit mode (`e` key). */
  request: () => void
}

export const useEditIntent = create<EditIntentState>((set) => ({
  nonce: 0,
  request: () => set((s) => ({ nonce: s.nonce + 1 })),
}))

/**
 * Run `onEdit` whenever an edit-intent is requested. Each screen's editor
 * (item body, KB entry, design) subscribes; since only the active route's
 * editor is mounted, bumping the shared nonce makes exactly that editor
 * enter edit mode — no prop threading from the global key handler.
 *
 * Subscribes imperatively (not via a nonce-watching effect) so the
 * setState fires from a store callback, not synchronously inside an
 * effect body.
 */
export function useEditIntentEffect(onEdit: () => void) {
  const cb = useRef(onEdit)
  useEffect(() => {
    cb.current = onEdit
  })
  useEffect(() => {
    let prev = useEditIntent.getState().nonce
    return useEditIntent.subscribe((s) => {
      if (s.nonce !== prev) {
        prev = s.nonce
        cb.current()
      }
    })
  }, [])
}
