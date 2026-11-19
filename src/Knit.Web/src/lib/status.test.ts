import { describe, it, expect } from 'vitest'
import { barFilled, countLabel, headlineParts } from './status'
import type { StatusSummary } from '../api/status'

function summary(over: Partial<StatusSummary> = {}): StatusSummary {
  return {
    scope: 'all items',
    total: 10,
    done: 4,
    inProgress: 2,
    review: 0,
    todo: 4,
    dismissed: 0,
    blocked: 0,
    overdue: 0,
    unassigned: 0,
    percent: 40,
    health: null,
    ...over,
  }
}

describe('barFilled', () => {
  it('fills nothing at 0 and everything at 100', () => {
    expect(barFilled(0, 24)).toBe(0)
    expect(barFilled(100, 24)).toBe(24)
  })

  it('never rounds a started bar down to empty', () => {
    expect(barFilled(1, 24)).toBe(1)
  })

  it('never rounds an unfinished bar up to full', () => {
    expect(barFilled(99, 24)).toBe(23)
  })

  it('clamps out-of-range percentages', () => {
    expect(barFilled(-5, 24)).toBe(0)
    expect(barFilled(140, 24)).toBe(24)
  })
})

describe('countLabel', () => {
  it('dashes zero so it does not pull the eye', () => {
    expect(countLabel(0)).toBe('—')
    expect(countLabel(3)).toBe('3')
  })
})

describe('headlineParts', () => {
  it('omits review and dismissed when there is none', () => {
    expect(headlineParts(summary())).toEqual([
      '10 items', '4 done', '2 in progress', '4 todo',
    ])
  })

  it('appends review when nonzero', () => {
    expect(headlineParts(summary({ review: 1 }))).toContain('1 in review')
  })

  it('appends dismissed when nonzero', () => {
    expect(headlineParts(summary({ dismissed: 2 }))).toContain('2 dismissed')
  })

  it('singularizes a one-item scope', () => {
    expect(headlineParts(summary({ total: 1 }))[0]).toBe('1 item')
  })
})
