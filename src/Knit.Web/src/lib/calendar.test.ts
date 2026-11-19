import { describe, expect, it } from 'vitest'
import { addMonths, daysInMonth, isoDate, monthGrid, openMonth, parseIsoDate } from './calendar'

describe('parseIsoDate', () => {
  it('reads a plain ISO date', () => {
    expect(parseIsoDate('2026-09-15')).toEqual({ year: 2026, month: 9, day: 15 })
  })

  it('rejects anything that is not a plain date', () => {
    for (const bad of [null, undefined, '', 'tomorrow', '2026-9-1', '2026-09-15T10:00:00Z'])
      expect(parseIsoDate(bad)).toBeNull()
  })

  it('rejects an out-of-range month or day', () => {
    expect(parseIsoDate('2026-13-01')).toBeNull()
    expect(parseIsoDate('2026-02-30')).toBeNull()
    // …but a real leap day parses.
    expect(parseIsoDate('2028-02-29')).toEqual({ year: 2028, month: 2, day: 29 })
  })
})

describe('daysInMonth', () => {
  it('handles the short months and leap years', () => {
    expect(daysInMonth({ year: 2026, month: 2 })).toBe(28)
    expect(daysInMonth({ year: 2028, month: 2 })).toBe(29)
    expect(daysInMonth({ year: 2026, month: 4 })).toBe(30)
    expect(daysInMonth({ year: 2026, month: 12 })).toBe(31)
  })

  it('treats 1900 as the non-leap year it is', () => {
    expect(daysInMonth({ year: 1900, month: 2 })).toBe(28)
  })
})

describe('addMonths', () => {
  it('rolls the year forward and back', () => {
    expect(addMonths({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 })
    expect(addMonths({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 })
  })

  it('crosses several years at once', () => {
    expect(addMonths({ year: 2026, month: 9 }, 25)).toEqual({ year: 2028, month: 10 })
    expect(addMonths({ year: 2026, month: 9 }, -25)).toEqual({ year: 2024, month: 8 })
  })
})

describe('monthGrid', () => {
  it('always renders six weeks so the popover never resizes', () => {
    for (const ym of [{ year: 2026, month: 2 }, { year: 2026, month: 8 }, { year: 2026, month: 3 }]) {
      const grid = monthGrid(ym)
      expect(grid).toHaveLength(6)
      for (const week of grid) expect(week).toHaveLength(7)
    }
  })

  it('starts the week on Monday', () => {
    // 2026-09-01 is a Tuesday, so Monday's cell leads and is blank.
    const grid = monthGrid({ year: 2026, month: 9 })
    expect(grid[0]).toEqual([null, '2026-09-01', '2026-09-02', '2026-09-03', '2026-09-04', '2026-09-05', '2026-09-06'])
  })

  it('needs no leading blank when the 1st is a Monday', () => {
    // 2026-06-01 is a Monday.
    expect(monthGrid({ year: 2026, month: 6 })[0]![0]).toBe('2026-06-01')
  })

  it('lists every day of the month exactly once', () => {
    const days = monthGrid({ year: 2028, month: 2 }).flat().filter(Boolean)
    expect(days).toHaveLength(29)
    expect(new Set(days).size).toBe(29)
    expect(days.at(-1)).toBe('2028-02-29')
  })
})

describe('isoDate', () => {
  it('zero-pads', () => {
    expect(isoDate({ year: 2026, month: 3 }, 7)).toBe('2026-03-07')
  })
})

describe('openMonth', () => {
  it('opens on the value when there is one', () => {
    expect(openMonth('2025-01-20', '2026-09-15')).toEqual({ year: 2025, month: 1 })
  })

  it('falls back to today when the field is empty', () => {
    expect(openMonth(null, '2026-09-15')).toEqual({ year: 2026, month: 9 })
  })

  it('falls back to today when the stored value is malformed', () => {
    // A hand-edited frontmatter date must not leave the picker with no month.
    expect(openMonth('soon', '2026-09-15')).toEqual({ year: 2026, month: 9 })
  })
})
