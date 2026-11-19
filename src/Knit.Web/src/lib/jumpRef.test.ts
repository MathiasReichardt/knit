import { describe, expect, it } from 'vitest'
import { classifyRef, isLinkRef } from './jumpRef'

describe('classifyRef', () => {
  it('returns null for blank input', () => {
    expect(classifyRef('')).toBeNull()
    expect(classifyRef('   ')).toBeNull()
  })

  it('classifies a known bucket head as KB', () => {
    expect(classifyRef('adr:0002-foo')).toEqual({ kind: 'kb', bucket: 'adr', slug: '0002-foo' })
    expect(classifyRef('feature:user-mgmt/sub')).toEqual({
      kind: 'kb',
      bucket: 'feature',
      slug: 'user-mgmt/sub',
    })
  })

  it('trims surrounding whitespace', () => {
    expect(classifyRef('  doc:guide  ')).toEqual({ kind: 'kb', bucket: 'doc', slug: 'guide' })
  })

  it('classifies a design ref into its own address space', () => {
    expect(classifyRef('design:web-ui')).toEqual({ kind: 'design', slug: 'web-ui' })
    expect(classifyRef('  design:m1c-web-ui  ')).toEqual({ kind: 'design', slug: 'm1c-web-ui' })
  })

  it('treats an unknown colon head as an item id', () => {
    expect(classifyRef('foo:bar')).toEqual({ kind: 'item', id: 'foo:bar' })
  })

  it('classifies a uuid or short-id prefix as an item', () => {
    expect(classifyRef('0190abcd')).toEqual({ kind: 'item', id: '0190abcd' })
    expect(classifyRef('0190abcd-1234-7000-8000-000000000000')).toEqual({
      kind: 'item',
      id: '0190abcd-1234-7000-8000-000000000000',
    })
  })
})

describe('isLinkRef', () => {
  it('takes section refs and the refs the lookups do not', () => {
    expect(isLinkRef('design:web#goals')).toBe(true)
    expect(isLinkRef(' doc:guide#intro ')).toBe(true)
    expect(isLinkRef('item:G7H8K2M4P9Q1R5T3VX')).toBe(true)
    expect(isLinkRef('project:shop')).toBe(true)
    expect(isLinkRef('.knit/projects/shop/milestones/m1.md#scope')).toBe(true)
  })

  it('leaves plain KB / design refs, ids and titles to the lookups', () => {
    expect(isLinkRef('design:web')).toBe(false)
    expect(isLinkRef('adr:0002-foo')).toBe(false)
    expect(isLinkRef('a3f9')).toBe(false)
    expect(isLinkRef('robot #2')).toBe(false)
  })
})
