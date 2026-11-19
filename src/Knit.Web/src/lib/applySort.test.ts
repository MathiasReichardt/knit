import { describe, expect, it } from 'vitest'
import { applySortSummary } from './applySort'

describe('applySortSummary', () => {
  it('states the counts', () => {
    expect(applySortSummary({ moved: 4, total: 9, groups: 3 })).toBe('Rewrites rank on 4 items in 3 groups.')
  })

  it('uses the singular for one', () => {
    expect(applySortSummary({ moved: 1, total: 2, groups: 1 })).toBe('Rewrites rank on 1 item in 1 group.')
  })

  it('says when nothing moves', () => {
    expect(applySortSummary({ moved: 0, total: 5, groups: 0 })).toMatch(/nothing to rewrite/)
  })

  it('says when the scope is empty', () => {
    expect(applySortSummary({ moved: 0, total: 0, groups: 0 })).toBe('No children to reorder.')
  })
})
