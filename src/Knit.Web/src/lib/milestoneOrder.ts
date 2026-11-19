// Order math for the roadmap spine's milestone drag-and-drop. Milestones carry
// a manual fractional rank (unlike free-form docs, which are name-sorted), so a
// before/after drop genuinely reorders them. The server owns the rank strings;
// the client only sends the desired slug order, which this computes.

// Move `dragSlug` to sit before or after `targetSlug` in `current`. Returns the
// new full slug order, or null for a no-op (dropping onto itself, an unknown
// target, or a drop that leaves the order unchanged).
export function reorderMilestones(
  current: string[],
  dragSlug: string,
  targetSlug: string,
  pos: 'before' | 'after',
): string[] | null {
  if (dragSlug === targetSlug) return null
  const without = current.filter((s) => s !== dragSlug)
  const targetIdx = without.indexOf(targetSlug)
  if (targetIdx < 0) return null
  const insertAt = pos === 'before' ? targetIdx : targetIdx + 1
  const next = [...without.slice(0, insertAt), dragSlug, ...without.slice(insertAt)]
  if (next.length === current.length && next.every((s, i) => s === current[i])) return null
  return next
}
