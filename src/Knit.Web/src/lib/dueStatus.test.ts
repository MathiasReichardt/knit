import { describe, it, expect } from 'vitest'
import { countdown, dueShown, dueStatus, todayIso, dueStatusColor, formatDueDate } from './dueStatus'

const TODAY = '2026-07-08'

describe('dueStatus', () => {
  it('returns none for missing due', () => {
    expect(dueStatus(null, TODAY, 3, false)).toBe('none')
    expect(dueStatus(undefined, TODAY, 3, false)).toBe('none')
  })

  it('flags past dates overdue', () => {
    expect(dueStatus('2026-07-01', TODAY, 3, false)).toBe('overdue')
  })

  it('flags today and within-window dates as soon', () => {
    expect(dueStatus(TODAY, TODAY, 3, false)).toBe('soon')
    expect(dueStatus('2026-07-11', TODAY, 3, false)).toBe('soon')
  })

  it('flags dates beyond the window as future', () => {
    expect(dueStatus('2026-07-12', TODAY, 3, false)).toBe('future')
    expect(dueStatus('2026-09-01', TODAY, 3, false)).toBe('future')
  })

  it('never emphasizes closed items', () => {
    expect(dueStatus('2026-07-01', TODAY, 3, true)).toBe('none')
    expect(dueStatus('2026-07-09', TODAY, 3, true)).toBe('none')
  })

  it('respects the configurable window', () => {
    expect(dueStatus('2026-07-15', TODAY, 3, false)).toBe('future')
    expect(dueStatus('2026-07-15', TODAY, 7, false)).toBe('soon')
  })
})

describe('todayIso', () => {
  it('formats a local date as YYYY-MM-DD', () => {
    expect(todayIso(new Date(2026, 0, 5))).toBe('2026-01-05')
  })
})

describe('formatDueDate', () => {
  it('renders a date-only string without a clock and without a TZ day-shift', () => {
    const out = formatDueDate('2026-07-15')
    expect(out).toContain('15')
    expect(out).toContain('2026')
    // Parsed as local midnight, so it must never slip to the 14th.
    expect(out).not.toContain('14')
    // Date-only: no time component leaks in.
    expect(out).not.toMatch(/\d:\d/)
  })

  it('passes through non-ISO input unchanged', () => {
    expect(formatDueDate('not-a-date')).toBe('not-a-date')
  })
})

describe('dueStatusColor', () => {
  it('colors only overdue and soon', () => {
    expect(dueStatusColor('overdue')).toContain('red')
    expect(dueStatusColor('soon')).toContain('amber')
    expect(dueStatusColor('future')).toBe('')
    expect(dueStatusColor('none')).toBe('')
  })
})

describe('countdown', () => {
  it('renders a compact relative countdown around today', () => {
    expect(countdown('2026-07-20', TODAY)).toBe('in 12d')
    expect(countdown(TODAY, TODAY)).toBe('today')
    expect(countdown('2026-07-04', TODAY)).toBe('4d late')
  })

  it('is DST-proof (UTC arithmetic, whole days)', () => {
    expect(countdown('2026-11-05', '2026-10-29')).toBe('in 7d')
  })
})

describe('dueShown', () => {
  it('shows every date, only overdue ones, or none', () => {
    expect(dueShown('future', 'all')).toBe(true)
    expect(dueShown('overdue', 'overdue')).toBe(true)
    expect(dueShown('soon', 'overdue')).toBe(false)
    expect(dueShown('none', 'overdue')).toBe(false)
    expect(dueShown('overdue', 'none')).toBe(false)
  })
})
