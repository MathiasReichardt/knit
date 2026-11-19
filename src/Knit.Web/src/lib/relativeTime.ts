// Rounded relative time for the landing cards. The leading `~` signals
// the value is rounded; the exact timestamp rides in the card's `title=`.
// Distinct from activityFormat's compact `relativeTime` ("2h") — the landing
// cards want the spelled-out "~2 h ago" form with a tooltip.

export function relativeAgo(ts: string, now: number = Date.now()): string {
  const then = Date.parse(ts)
  if (Number.isNaN(then)) return ''
  const secs = Math.max(0, Math.round((now - then) / 1000))
  if (secs < 45) return 'just now'
  const mins = Math.round(secs / 60)
  if (mins < 60) return `~${mins} min ago`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `~${hours} h ago`
  const days = Math.round(hours / 24)
  if (days < 7) return `~${days} d ago`
  const weeks = Math.round(days / 7)
  if (weeks < 5) return `~${weeks} w ago`
  const months = Math.round(days / 30)
  if (months < 12) return `~${months} mo ago`
  const years = Math.round(days / 365)
  return `~${years} y ago`
}

/** Exact local timestamp for the `title=` tooltip; "" when unparseable. */
export function exactTime(ts: string): string {
  const then = Date.parse(ts)
  if (Number.isNaN(then)) return ''
  return new Date(then).toLocaleString()
}
