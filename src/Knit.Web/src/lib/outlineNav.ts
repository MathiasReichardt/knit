export type OutlineKey = 'ArrowUp' | 'ArrowDown' | 'Home' | 'End'

const KEYS: ReadonlySet<string> = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End'])

export function isOutlineKey(key: string): key is OutlineKey {
  return KEYS.has(key)
}

/**
 * The heading a key moves the outline focus to, or null when there is none.
 * ↑/↓ step and clamp at the ends; from no focus they enter at the first
 * heading.
 */
export function outlineStep(ids: readonly string[], current: string | null, key: OutlineKey): string | null {
  if (ids.length === 0) return null
  if (key === 'Home') return ids[0]!
  if (key === 'End') return ids[ids.length - 1]!
  const i = current === null ? -1 : ids.indexOf(current)
  if (i < 0) return ids[0]!
  return ids[key === 'ArrowDown' ? Math.min(ids.length - 1, i + 1) : Math.max(0, i - 1)]!
}

/** Below this pane width a pinned outline would squeeze the text; it falls back to the overlay. */
export const PIN_MIN_WIDTH = 720

export function outlinePinned(pinned: boolean, paneWidth: number): boolean {
  return pinned && paneWidth >= PIN_MIN_WIDTH
}

/** The id of the last heading at or above source line `line`, or null above the first one. */
export function headingAtLine(items: readonly { id: string; line: number }[], line: number): string | null {
  let active: string | null = null
  for (const it of items) {
    if (it.line > line) break
    active = it.id
  }
  return active
}

type ShortcutEvent = Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'shiftKey' | 'altKey' | 'metaKey' | 'getModifierState'>

/** The editor's outline key: Ctrl+Shift+O, or Alt+O where the browser keeps the former. */
export function isEditorOutlineKey(e: ShortcutEvent): boolean {
  // AltGr reports as Ctrl+Alt on Windows and types letters (ó on Polish layouts).
  if (e.metaKey || e.getModifierState('AltGraph')) return false
  if (e.ctrlKey && e.shiftKey && !e.altKey) return e.key.toLowerCase() === 'o'
  // By position: Option+O types ø on a Mac.
  if (e.altKey && !e.ctrlKey && !e.shiftKey) return e.code === 'KeyO'
  return false
}

/** The rendered view's outline key: a bare `o`. */
export function isViewOutlineKey(e: ShortcutEvent): boolean {
  return !e.ctrlKey && !e.metaKey && !e.altKey && e.key === 'o'
}
