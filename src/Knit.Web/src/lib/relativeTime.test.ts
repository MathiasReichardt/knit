import { describe, it, expect } from 'vitest'
import { relativeAgo, exactTime } from './relativeTime'

const NOW = Date.parse('2026-06-23T12:00:00Z')
const ago = (ms: number) => new Date(NOW - ms).toISOString()

describe('relativeAgo', () => {
  it('reads "just now" under 45 s', () => {
    expect(relativeAgo(ago(10_000), NOW)).toBe('just now')
  })

  it('rounds to minutes with a leading ~', () => {
    expect(relativeAgo(ago(2 * 60_000), NOW)).toBe('~2 min ago')
  })

  it('rounds to hours', () => {
    expect(relativeAgo(ago(2 * 3_600_000), NOW)).toBe('~2 h ago')
  })

  it('rounds to days', () => {
    expect(relativeAgo(ago(3 * 86_400_000), NOW)).toBe('~3 d ago')
  })

  it('rolls up to weeks/months/years', () => {
    expect(relativeAgo(ago(14 * 86_400_000), NOW)).toBe('~2 w ago')
    expect(relativeAgo(ago(60 * 86_400_000), NOW)).toBe('~2 mo ago')
    expect(relativeAgo(ago(730 * 86_400_000), NOW)).toBe('~2 y ago')
  })

  it('returns "" for an unparseable timestamp', () => {
    expect(relativeAgo('not-a-date', NOW)).toBe('')
  })
})

describe('exactTime', () => {
  it('is non-empty for a valid timestamp and empty otherwise', () => {
    expect(exactTime(ago(0))).not.toBe('')
    expect(exactTime('nope')).toBe('')
  })
})
