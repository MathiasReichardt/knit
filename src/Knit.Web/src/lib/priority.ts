import { ChevronDown, ChevronsUp, ChevronUp, Minus } from 'lucide-react'
import type { Priority } from '../api/types'
import type { IconCmp } from './glyphs'

// Low→high, matching the server enum order. Also the display order for
// dropdowns / threshold selectors.
export const PRIORITY_LEVELS: Priority[] = ['someday', 'normal', 'important', 'critical']

/** Ordinal for sort/threshold (someday=0 … critical=3), mirrors the server. */
export const PRIORITY_ORDER: Record<Priority, number> = {
  someday: 0,
  normal: 1,
  important: 2,
  critical: 3,
}

// lucide glyphs per level: direction encodes magnitude. `normal`
// is the implicit default and renders WITHOUT a glyph (see hasPriorityIcon) so
// the signal levels stand out instead of a dash sitting on every row. The
// `normal` entry stays mapped for type-completeness but is never rendered.
// Coloring by priority is a separate opt-in toggle — these render neutral here.
export const PRIORITY_ICON: Record<Priority, IconCmp> = {
  someday: ChevronDown,
  normal: Minus,
  important: ChevronUp,
  critical: ChevronsUp,
}

/** Whether a level shows a glyph. `normal` (the implicit default) does not. */
export function hasPriorityIcon(p: Priority): boolean {
  return p !== 'normal'
}

export const PRIORITY_LABEL: Record<Priority, string> = {
  someday: 'Someday',
  normal: 'Normal',
  important: 'Important',
  critical: 'Critical',
}

/**
 * Resolve an item's priority, treating a missing / unknown value as `normal`
 * — the field is implicit-normal by design (unset ⇒ normal), and this keeps
 * the UI robust against a payload that predates the field.
 */
export function priorityOf(p: Priority | null | undefined): Priority {
  return p && p in PRIORITY_ORDER ? p : 'normal'
}

// Color-by-priority row tint (opt-in toggle). Semantic, theme-aware
// row-background classes (base + hover) that tint the WHOLE item in tree/list
// — not a left bar + dot, which would add a third glyph to already busy rows
// and be too faint for an opt-in signal. Tuned to selection's
// intensity (`bg-*-100` light / `dark:bg-*-900/…`) so an on-toggle reads
// clearly, while selection's blue still wins (applied only when unselected).
// Only levels ABOVE normal tint — the tint is an *attention* signal, so
// `normal` (most items) and `someday` (below normal — deliberately parked)
// stay calm; tinting them would pull the eye toward the least-urgent rows.
//
// Dark mode carries `critical` at red-800/45 rather than the red-900/30 that
// mirrored `important`: at 30% alpha over zinc-950 both 900-shades
// composite to near-identical muddy browns — the low alpha destroys exactly
// the hue difference that carries the signal in light mode. The separation is
// held to a measured floor by `priorityTint.test.ts`, not to the eye.
const PRIORITY_ROW_TINT: Record<Priority, string | null> = {
  someday: null,
  normal: null,
  important: 'bg-amber-100 hover:bg-amber-200 dark:bg-amber-900/30 dark:hover:bg-amber-900/45',
  critical: 'bg-red-100 hover:bg-red-200 dark:bg-red-800/45 dark:hover:bg-red-800/60',
}

/** Row-background tint classes (base + hover) for the color-by-priority
 * toggle, or `null` for `normal` (no tint — the default stays calm). */
export function priorityRowTint(p: Priority | null | undefined): string | null {
  return PRIORITY_ROW_TINT[priorityOf(p)]
}

// The timeline marker reads the same signal as a row tint, so it lives beside
// it — opaque fills there (a small shape can't rely on a wash) but the same
// hue pairing, and the same measured separation floor.
const PRIORITY_MARKER_FILL: Record<Priority, string | null> = {
  someday: null,
  normal: null,
  important: 'fill-amber-200 dark:fill-amber-900',
  critical: 'fill-red-200 dark:fill-red-800',
}

/** Timeline-marker fill classes for the color-by-priority toggle, or `null`
 * for the levels that stay untinted (caller supplies the neutral fill). */
export function priorityMarkerFill(p: Priority | null | undefined): string | null {
  return PRIORITY_MARKER_FILL[priorityOf(p)]
}
