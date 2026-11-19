import { describe, expect, it } from 'vitest'
import type { Priority } from '../api/types'
import { PRIORITY_ICON, PRIORITY_LABEL, PRIORITY_LEVELS, PRIORITY_ORDER, priorityOf, priorityRowTint } from './priority'

describe('priority metadata', () => {
  it('orders someday→critical ascending', () => {
    expect(PRIORITY_LEVELS).toEqual(['someday', 'normal', 'important', 'critical'])
    const ordinals = PRIORITY_LEVELS.map((p) => PRIORITY_ORDER[p])
    expect(ordinals).toEqual([0, 1, 2, 3])
  })

  it('sorts critical-first when ordered by descending ordinal', () => {
    const shuffled: Priority[] = ['normal', 'critical', 'someday', 'important']
    const sorted = [...shuffled].sort((a, b) => PRIORITY_ORDER[b] - PRIORITY_ORDER[a])
    expect(sorted).toEqual(['critical', 'important', 'normal', 'someday'])
  })

  it('has an icon and label for every level', () => {
    for (const p of PRIORITY_LEVELS) {
      expect(PRIORITY_ICON[p]).toBeTruthy()
      expect(PRIORITY_LABEL[p]).toBeTruthy()
    }
  })

  it('resolves missing / unknown priority to normal (implicit default)', () => {
    expect(priorityOf(undefined)).toBe('normal')
    expect(priorityOf(null)).toBe('normal')
    expect(priorityOf('bogus' as unknown as Priority)).toBe('normal')
    expect(priorityOf('critical')).toBe('critical')
  })
})

describe('priorityRowTint (color-by-priority toggle)', () => {
  it('gives no tint for normal/someday (and unset, which resolves to normal)', () => {
    // tint is an attention signal — only levels above normal get it
    expect(priorityRowTint('someday')).toBeNull()
    expect(priorityRowTint('normal')).toBeNull()
    expect(priorityRowTint(null)).toBeNull()
    expect(priorityRowTint(undefined)).toBeNull()
  })

  it('gives a theme-aware row-background tint with base + hover for each above-normal level', () => {
    for (const p of ['important', 'critical'] as const) {
      const cls = priorityRowTint(p)
      expect(cls).toBeTruthy()
      // row-background classes (whole-item tint); carries a hover
      // variant for feedback and a dark: variant so it reads in both themes.
      expect(cls).toMatch(/(^|\s)bg-/)
      expect(cls).toContain('hover:bg-')
      expect(cls).toContain('dark:')
    }
  })
})
