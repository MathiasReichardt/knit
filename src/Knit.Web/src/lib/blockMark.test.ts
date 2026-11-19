import { describe, expect, it } from 'vitest'
import { blockMark } from './blockMark'

const holder = { id: 'P', shortId: 'P', title: 'Phase 2' }

describe('blockMark', () => {
  it('is null when neither the item nor an ancestor blocks', () => {
    expect(blockMark({ blocked: false, blockedReason: null })).toBeNull()
  })

  it('names the own block first, even when a parent also holds it', () => {
    expect(blockMark({ blocked: true, blockedReason: 'vendor', heldBy: holder })).toEqual({
      held: false,
      label: 'Blocked: vendor',
    })
  })

  it('marks an item held by a blocked parent', () => {
    expect(blockMark({ blocked: false, blockedReason: null, heldBy: holder })).toEqual({
      held: true,
      label: 'Blocked by parent: Phase 2',
    })
  })
})
