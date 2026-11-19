// Shared keyboard-navigation core for every tree/list control (items
// TreeView/ListView, DesignsTree, KbTree). The four surfaces converged on the
// same ↑↓←→ semantics; this is the single implementation they all drive.
//
// A control flattens its visible rows into `NavRow[]` in render order — a node
// row is immediately followed by its expanded children. Because the rows are
// pre-flattened in order, ↑/↓ are just index steps.

export interface NavRow<T = unknown> {
  /** Unique within the row list. */
  key: string
  /** Key of the row this hangs under, or null at the root. */
  parentKey: string | null
  /** Can be expanded/collapsed (folders, folder-form entries). */
  expandable: boolean
  expanded: boolean
  /** Control-specific payload, handed back verbatim on activate. */
  data: T
}

export type ArrowKey = 'ArrowUp' | 'ArrowDown' | 'ArrowLeft' | 'ArrowRight'

export type NavAction =
  | { type: 'activate'; key: string }
  | { type: 'toggle'; key: string }
  | { type: 'none' }

const ARROW_KEYS: ReadonlySet<string> = new Set([
  'ArrowUp',
  'ArrowDown',
  'ArrowLeft',
  'ArrowRight',
])

export function isArrowKey(key: string): key is ArrowKey {
  return ARROW_KEYS.has(key)
}

/** A row whose target is a text input — arrow keys belong to it, not the tree. */
export function isTyping(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null
  return (
    !!el &&
    (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)
  )
}

/**
 * Compute the action for one arrow keypress given the flattened rows and the
 * current cursor. Pure — the React hook applies the result.
 *
 * - ↑/↓ step to the previous/next visible row (clamped).
 * - → expands a collapsed expandable row, else steps into its first child.
 * - ← collapses an expanded expandable row, else hops to the parent.
 */
export function arrowNavStep(
  rows: NavRow[],
  cursorKey: string | null,
  key: ArrowKey,
): NavAction {
  if (rows.length === 0) return { type: 'none' }
  // `!= null`, not truthy: the KB tree's root folder row has key '' (a valid
  // cursor), which a truthy test would misread as "no cursor".
  const idx = cursorKey != null ? rows.findIndex((r) => r.key === cursorKey) : -1
  // No active cursor (none yet, or it's gone): the first arrow *enters* the list
  // at the top rather than treating row 0 as the cursor and stepping past it —
  // which would skip the first item on the very first keypress.
  if (idx < 0) return { type: 'activate', key: rows[0]!.key }
  const cur = rows[idx]!

  if (key === 'ArrowDown') {
    return { type: 'activate', key: rows[Math.min(rows.length - 1, idx + 1)]!.key }
  }
  if (key === 'ArrowUp') {
    return { type: 'activate', key: rows[Math.max(0, idx - 1)]!.key }
  }
  if (key === 'ArrowRight') {
    if (cur.expandable && !cur.expanded) return { type: 'toggle', key: cur.key }
    // An expanded expandable row, or a flat (non-collapsible) row that still
    // has a child present — step into the first child. Tree leaves have no
    // child rows, so they fall through to `none`; ListView's flat parent/child
    // hop rides this branch.
    for (let i = idx + 1; i < rows.length; i++) {
      if (rows[i]!.parentKey === cur.key) return { type: 'activate', key: rows[i]!.key }
    }
    return { type: 'none' }
  }
  // ArrowLeft
  if (cur.expandable && cur.expanded) return { type: 'toggle', key: cur.key }
  if (cur.parentKey !== null) return { type: 'activate', key: cur.parentKey }
  return { type: 'none' }
}
