import type { ActivityEntry } from '../api/types'

/** Human verb for an activity row, derived from category + op. */
export function activityVerb(e: ActivityEntry): string {
  switch (e.category) {
    case 'created':
      return 'created'
    case 'state':
      return `${e.fromState ?? '?'} → ${e.toState ?? '?'}`
    case 'comment':
      return 'commented on'
    case 'kb':
      if (e.op === 'created') return 'created KB'
      if (e.op === 'deleted') return 'deleted KB'
      return 'edited KB'
    case 'other':
    default:
      return e.op === 'deleted' ? 'deleted' : 'edited'
  }
}

/** Compact relative time ("just now", "3m", "2h", "5d"). */
export function relativeTime(ts: string, now: number = Date.now()): string {
  const then = Date.parse(ts)
  if (Number.isNaN(then)) return ''
  const secs = Math.max(0, Math.round((now - then) / 1000))
  if (secs < 45) return 'just now'
  const mins = Math.round(secs / 60)
  if (mins < 60) return `${mins}m`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `${hours}h`
  const days = Math.round(hours / 24)
  return `${days}d`
}

export interface ActivityNavTarget {
  to: '/items' | '/kb'
  search: Record<string, string>
}

/**
 * Deep-link target for a row. Items open the navigator with the id
 * selected; KB entries open the KB browser at `bucket:slug`.
 */
export function activityNavTarget(e: ActivityEntry): ActivityNavTarget {
  if (e.kind === 'kb') {
    const idx = e.id.indexOf(':')
    const bucket = idx >= 0 ? e.id.slice(0, idx) : e.id
    const slug = idx >= 0 ? e.id.slice(idx + 1) : ''
    return { to: '/kb', search: { bucket, slug } }
  }
  return { to: '/items', search: { id: e.id } }
}
