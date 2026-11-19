import { describe, expect, it } from 'vitest'
import { formatShortDate } from './shortDate'

describe('formatShortDate', () => {
  it('returns null for missing input', () => {
    expect(formatShortDate(null)).toBeNull()
    expect(formatShortDate(undefined)).toBeNull()
    expect(formatShortDate('')).toBeNull()
  })

  it('returns null for an unparseable date', () => {
    expect(formatShortDate('not-a-date')).toBeNull()
  })

  it('returns a non-empty short label and the canonical ISO for a valid date', () => {
    const r = formatShortDate('2026-07-03T09:30:00.000Z')
    expect(r).not.toBeNull()
    // `short` is locale-formatted, so assert only that it is present; `full`
    // is the deterministic round-trip used for the hover title.
    expect(r!.short.length).toBeGreaterThan(0)
    expect(r!.full).toBe('2026-07-03T09:30:00.000Z')
  })
})
