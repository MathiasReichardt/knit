/**
 * At most one inline editor is open at a time (22).
 *
 * Relying on blur alone to close the previous one is not enough: whether a
 * click on another field blurs the open input first depends on the browser, so
 * clicking straight from one field to the next could leave *both* editors live
 * — and then Esc only closed the one that happened to have focus. Opening an
 * editor therefore explicitly displaces whichever was open.
 *
 * A module-level singleton rather than context: the invariant is global to the
 * page, and every editor would otherwise have to be wrapped in a provider it
 * has no other use for.
 */
export interface EditorHandle {
  /** Leave edit mode. Must be safe to call when already closed. */
  close: () => void
}

let current: EditorHandle | null = null

/** Close whichever editor is open, then take its place. */
export function claimEditor(handle: EditorHandle): void {
  if (current && current !== handle) current.close()
  current = handle
}

/** Give up the claim, if this editor still holds it. */
export function releaseEditor(handle: EditorHandle): void {
  if (current === handle) current = null
}

/** Test seam — the singleton would otherwise leak between specs. */
export function resetActiveEditor(): void {
  current = null
}
