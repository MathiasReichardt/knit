// Due-date proximity classification, mirroring the server's DueDates.Status.
// Pure + time-injected (`today` is a param) so it's deterministic to test.
// ISO 'YYYY-MM-DD' strings compare lexically == chronologically.

export type DueStatus = 'none' | 'overdue' | 'soon' | 'future'

/** Local calendar date as 'YYYY-MM-DD' (timezone-free, matches the CLI). */
export function todayIso(now: Date = new Date()): string {
  const y = now.getFullYear()
  const m = String(now.getMonth() + 1).padStart(2, '0')
  const d = String(now.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d))
  dt.setUTCDate(dt.getUTCDate() + days)
  return dt.toISOString().slice(0, 10)
}

/**
 * Classify a due date. Closed items (done/dismissed) are never overdue or
 * due-soon — finished work isn't actionable.
 */
export function dueStatus(
  due: string | null | undefined,
  today: string,
  dueSoonDays: number,
  closed: boolean,
): DueStatus {
  if (!due) return 'none'
  if (closed) return 'none'
  if (due < today) return 'overdue'
  if (due <= addDaysIso(today, Math.max(0, dueSoonDays))) return 'soon'
  return 'future'
}

/** Whether the `due` view pref shows a due date of this status. */
export function dueShown(status: DueStatus, display: 'all' | 'overdue' | 'none'): boolean {
  return display === 'all' || (display === 'overdue' && status === 'overdue')
}

/**
 * Format a date-only 'YYYY-MM-DD' as a localized date without a clock, so it
 * matches the Created / Last-edited fields minus the time. Parsed as a *local*
 * date (not UTC) so it never slips a day across timezones. Falls back to the
 * raw input if it isn't a plain ISO date.
 */
export function formatDueDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return iso
  const dt = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return dt.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

/**
 * Compact countdown for a date-only target — `in 12d`, `today`, `4d late`.
 * The landing project card is ~190px wide, so the milestone line needs the
 * short form; the exact date rides in the element's `title`.
 */
export function countdown(target: string, today: string): string {
  const to = Date.parse(`${target}T00:00:00Z`)
  const from = Date.parse(`${today}T00:00:00Z`)
  if (Number.isNaN(to) || Number.isNaN(from)) return target
  const days = Math.round((to - from) / 86_400_000)
  if (days === 0) return 'today'
  return days > 0 ? `in ${days}d` : `${-days}d late`
}

/** Tailwind text-color classes for a due status (theme-aware). Empty for
 * none/future — only overdue/soon get emphasis, matching the CLI. */
export function dueStatusColor(status: DueStatus): string {
  switch (status) {
    case 'overdue':
      return 'text-red-600 dark:text-red-400'
    case 'soon':
      return 'text-amber-600 dark:text-amber-400'
    default:
      return ''
  }
}
