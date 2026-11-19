const TYPING_IDLE_MS = 1000

let quietDepth = 0

/**
 * Run a store change that is housekeeping, not a user step — dropping a slate
 * the repo no longer has. The URL sync still mirrors it, but by rewriting the
 * current history entry instead of adding one Back would land on.
 */
export function withoutHistory(fn: () => void): void {
  quietDepth++
  try {
    fn()
  } finally {
    quietDepth--
  }
}

/**
 * Decides whether a filter change mirrored into the URL becomes a browser
 * history step. A discrete change (a slate, a chip, a view toggle) pushes. A
 * change to a text key pushes once per typing burst and then rewrites that
 * entry until the typing goes idle, so Back undoes a whole search, not one
 * keystroke. Decide per store change, not per debounced write.
 */
export function createHistoryPolicy(textKeys: readonly string[] = [], idleMs = TYPING_IDLE_MS) {
  let typingUntil = 0
  return {
    shouldPush(changed: readonly string[], now = Date.now()): boolean {
      if (quietDepth > 0 || changed.length === 0) return false
      if (!changed.every((k) => textKeys.includes(k))) {
        typingUntil = 0
        return true
      }
      const push = now >= typingUntil
      typingUntil = now + idleMs
      return push
    },
    /** Back/forward ends any burst — the next keystroke starts a new step. */
    reset(): void {
      typingUntil = 0
    },
  }
}

/** The keys whose values differ between two search objects. */
export function changedKeys(a: object, b: object): string[] {
  const ra = a as Record<string, unknown>
  const rb = b as Record<string, unknown>
  return [...new Set([...Object.keys(ra), ...Object.keys(rb)])].filter(
    (k) => JSON.stringify(ra[k]) !== JSON.stringify(rb[k]),
  )
}
