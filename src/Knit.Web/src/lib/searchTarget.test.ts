import { describe, expect, it } from 'vitest'
import { isNewTabKey, searchHitTarget } from './searchTarget'

describe('searchHitTarget', () => {
  it('opens an item by id', () => {
    expect(searchHitTarget({ kind: 'item', id: 'G7H8K2M4P9Q1R5T3VX' })).toEqual({
      to: '/items',
      search: { id: 'G7H8K2M4P9Q1R5T3VX' },
    })
  })

  it('splits a KB ref at the first colon, keeping a nested slug', () => {
    expect(searchHitTarget({ kind: 'kb', id: 'feature:assembly/line' })).toEqual({
      to: '/kb',
      search: { bucket: 'feature', slug: 'assembly/line' },
    })
  })

  it('leaves the design tab to the route', () => {
    expect(searchHitTarget({ kind: 'design', id: 'web-ui' })).toEqual({
      to: '/designs',
      search: { slug: 'web-ui' },
    })
  })

  it('opens a milestone panel or a project doc', () => {
    expect(searchHitTarget({ kind: 'project', id: 'shop/milestones/beta' })).toEqual({
      to: '/projects',
      search: { mp: 'shop', ms: 'beta' },
    })
    expect(searchHitTarget({ kind: 'project', id: 'shop/notes/pricing' })).toEqual({
      to: '/projects',
      search: { doc: 'shop/notes/pricing.md' },
    })
  })

  it('has no target for a malformed id', () => {
    expect(searchHitTarget({ kind: 'kb', id: 'no-colon' })).toBeNull()
    expect(searchHitTarget({ kind: 'project', id: 'no-slash' })).toBeNull()
  })
})

describe('isNewTabKey', () => {
  it('is Enter with Ctrl or Cmd only', () => {
    expect(isNewTabKey({ key: 'Enter', ctrlKey: true, metaKey: false })).toBe(true)
    expect(isNewTabKey({ key: 'Enter', ctrlKey: false, metaKey: true })).toBe(true)
    expect(isNewTabKey({ key: 'Enter', ctrlKey: false, metaKey: false })).toBe(false)
    expect(isNewTabKey({ key: 'k', ctrlKey: true, metaKey: false })).toBe(false)
  })
})
