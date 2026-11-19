import { describe, expect, it } from 'vitest'
import type { BlockedDependent, Item } from '../api/types'
import { closedBlockers, dependentAction } from './blockedDependents'

const dep = (id: string, remaining: string | null = null): BlockedDependent => ({
  id,
  shortId: id.slice(0, 4),
  title: `title ${id}`,
  remaining,
})

const item = (id: string, blockedDependents?: BlockedDependent[]): Item =>
  ({ id, shortId: id.slice(0, 4), blockedDependents }) as Item

describe('dependentAction', () => {
  it('unblocks when the closed item was the only impediment', () => {
    expect(dependentAction(dep('a'))).toBe('unblock')
  })

  it('removes only the link when something else still blocks', () => {
    expect(dependentAction(dep('a', 'waiting on vendor'))).toBe('remove-link')
  })
})

describe('closedBlockers', () => {
  it('opens nothing when the call closed nothing or nothing depends', () => {
    expect(closedBlockers([item('x')])).toEqual([])
    expect(closedBlockers([item('x', [])])).toEqual([])
  })

  it('keeps each closed item that has dependents, cascaded ones included', () => {
    const groups = closedBlockers([item('root', []), item('child1', [dep('d1')]), item('child2', [dep('d2'), dep('d3')])])
    expect(groups.map((g) => g.id)).toEqual(['child1', 'child2'])
    expect(groups[1]!.dependents).toHaveLength(2)
  })
})
