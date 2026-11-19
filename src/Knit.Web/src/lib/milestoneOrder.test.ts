import { describe, it, expect } from 'vitest'
import { reorderMilestones } from './milestoneOrder'

describe('reorderMilestones', () => {
  const base = ['a', 'b', 'c', 'd']

  it('moves a milestone before a target', () => {
    expect(reorderMilestones(base, 'd', 'b', 'before')).toEqual(['a', 'd', 'b', 'c'])
  })

  it('moves a milestone after a target', () => {
    expect(reorderMilestones(base, 'a', 'c', 'after')).toEqual(['b', 'c', 'a', 'd'])
  })

  it('moves a milestone to the very top', () => {
    expect(reorderMilestones(base, 'c', 'a', 'before')).toEqual(['c', 'a', 'b', 'd'])
  })

  it('moves a milestone to the very bottom', () => {
    expect(reorderMilestones(base, 'a', 'd', 'after')).toEqual(['b', 'c', 'd', 'a'])
  })

  it('is a no-op when dropped onto itself', () => {
    expect(reorderMilestones(base, 'b', 'b', 'before')).toBeNull()
    expect(reorderMilestones(base, 'b', 'b', 'after')).toBeNull()
  })

  it('is a no-op when the resulting order is unchanged', () => {
    // 'b' after 'a' is where 'b' already sits.
    expect(reorderMilestones(base, 'b', 'a', 'after')).toBeNull()
    // 'b' before 'c' is likewise the status quo.
    expect(reorderMilestones(base, 'b', 'c', 'before')).toBeNull()
  })

  it('returns null for an unknown target', () => {
    expect(reorderMilestones(base, 'a', 'zzz', 'before')).toBeNull()
  })
})
