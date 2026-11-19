// Month-grid arithmetic for the inline date popover. Dates are
// plain ISO 'YYYY-MM-DD' throughout — the same timezone-free form the CLI and
// the frontmatter use, so nothing here ever constructs a Date from a string.

export interface YearMonth {
  year: number
  /** 1-12, not the JS 0-11 month. */
  month: number
}

/** Weeks start Monday, matching the ISO week and the CLI's timeline. */
export const WEEKDAY_LABELS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'] as const

export function isoDate({ year, month }: YearMonth, day: number): string {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

/** Null for anything that isn't a plain ISO date — a malformed stored value
 * must not throw a panel away, it just means "no month to open on". */
export function parseIsoDate(iso: string | null | undefined): (YearMonth & { day: number }) | null {
  if (!iso) return null
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return null
  const year = Number(m[1])
  const month = Number(m[2])
  const day = Number(m[3])
  if (month < 1 || month > 12) return null
  if (day < 1 || day > daysInMonth({ year, month })) return null
  return { year, month, day }
}

export function daysInMonth({ year, month }: YearMonth): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate()
}

export function addMonths(ym: YearMonth, delta: number): YearMonth {
  const zero = ym.year * 12 + (ym.month - 1) + delta
  return { year: Math.floor(zero / 12), month: (zero % 12) + 1 }
}

export function monthLabel({ year, month }: YearMonth): string {
  return new Date(Date.UTC(year, month - 1, 1)).toLocaleDateString(undefined, {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

/**
 * Six weeks of ISO dates (or null for a leading / trailing blank), so the grid
 * never changes height between months — the phase's "no box swaps size" rule
 * applies inside the popover too.
 */
export function monthGrid(ym: YearMonth): (string | null)[][] {
  const firstWeekday = (new Date(Date.UTC(ym.year, ym.month - 1, 1)).getUTCDay() + 6) % 7
  const total = daysInMonth(ym)
  const cells: (string | null)[] = Array.from({ length: firstWeekday }, () => null)
  for (let d = 1; d <= total; d++) cells.push(isoDate(ym, d))
  while (cells.length < 42) cells.push(null)
  return Array.from({ length: 6 }, (_, w) => cells.slice(w * 7, w * 7 + 7))
}

/** The month a picker should open on: the current value, else today. */
export function openMonth(value: string | null | undefined, today: string): YearMonth {
  const parsed = parseIsoDate(value) ?? parseIsoDate(today)
  return parsed ? { year: parsed.year, month: parsed.month } : { year: 1970, month: 1 }
}
