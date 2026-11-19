// Keyboard semantics for the inline field editors, kept pure so
// the listbox contract is tested without a DOM. The components own focus and
// rendering; this owns *what a key means*.

/** What a key press does to an open listbox. */
export type ListboxAction =
  | { kind: 'none' }
  /** Move the visual option focus. Nothing is committed yet. */
  | { kind: 'move'; index: number }
  /** Commit the active option and close. */
  | { kind: 'commit'; index: number }
  /** Close with the value unchanged. */
  | { kind: 'cancel' }
  /** Commit the active option, then let the browser move focus on (Tab). */
  | { kind: 'commitAndLeave'; index: number }

/**
 * Arrow / Home / End move option focus only — a listbox must not commit on
 * every arrow, or holding ↓ would write once per row. Enter and Space commit;
 * Tab commits and moves on (the field is left, so the choice stands); Esc
 * leaves the value alone.
 */
export function listboxKey(key: string, active: number, count: number): ListboxAction {
  if (count === 0) return key === 'Escape' ? { kind: 'cancel' } : { kind: 'none' }
  const clamp = (i: number) => Math.min(count - 1, Math.max(0, i))
  switch (key) {
    case 'ArrowDown':
      return { kind: 'move', index: active >= count - 1 ? 0 : clamp(active + 1) }
    case 'ArrowUp':
      return { kind: 'move', index: active <= 0 ? count - 1 : clamp(active - 1) }
    case 'Home':
      return { kind: 'move', index: 0 }
    case 'End':
      return { kind: 'move', index: count - 1 }
    case 'Enter':
    case ' ':
      return { kind: 'commit', index: clamp(active) }
    case 'Tab':
      return { kind: 'commitAndLeave', index: clamp(active) }
    case 'Escape':
      return { kind: 'cancel' }
    default:
      return { kind: 'none' }
  }
}

/**
 * Keys that open a closed editor from its trigger. ArrowDown / ArrowUp open
 * too (ARIA-conventional for a collapsed listbox) so a keyboard user never has
 * to guess that the value is actionable.
 */
export function opensEditor(key: string): boolean {
  return key === 'Enter' || key === ' ' || key === 'ArrowDown' || key === 'ArrowUp'
}

/** What a key press does to an open single-line text editor. */
export type TextAction =
  | { kind: 'none' }
  /** Write the draft (subject to the field's own required / unchanged checks). */
  | { kind: 'commit' }
  /** Discard the draft and close. */
  | { kind: 'revert' }

export function textKey(key: string): TextAction {
  if (key === 'Enter') return { kind: 'commit' }
  if (key === 'Escape') return { kind: 'revert' }
  return { kind: 'none' }
}
