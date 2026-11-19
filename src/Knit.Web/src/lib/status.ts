import type { StatusSummary } from '../api/status'

/**
 * Filled cells of a `width`-wide progress bar. Rounds to the nearest cell, but
 * never rounds a partial percentage down to an empty bar or up to a full one —
 * "1% done" and "99% done" must not read as "nothing" and "finished".
 */
export function barFilled(percent: number, width: number): number {
  const clamped = Math.min(100, Math.max(0, percent))
  const exact = (clamped / 100) * width
  const cells = Math.round(exact)
  if (clamped > 0 && cells === 0) return 1
  if (clamped < 100 && cells === width) return width - 1
  return cells
}

/**
 * Zero renders as a dash: a `0` still pulls the eye on a row whose whole job
 * is to show where attention is needed. Mirrors the CLI `status` rendering.
 */
export function countLabel(n: number): string {
  return n === 0 ? '—' : String(n)
}

/** The `N items · N done · N in progress · N todo` line, review and dismissed only when nonzero. */
export function headlineParts(s: StatusSummary): string[] {
  const parts = [
    `${s.total} item${s.total === 1 ? '' : 's'}`,
    `${s.done} done`,
    `${s.inProgress} in progress`,
    `${s.todo} todo`,
  ]
  if (s.review > 0) parts.push(`${s.review} in review`)
  if (s.dismissed > 0) parts.push(`${s.dismissed} dismissed`)
  return parts
}
