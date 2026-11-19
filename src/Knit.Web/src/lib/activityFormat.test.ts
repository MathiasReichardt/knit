import { describe, expect, it } from 'vitest'
import type { ActivityEntry } from '../api/types'
import { activityNavTarget, activityVerb, relativeTime } from './activityFormat'

function entry(over: Partial<ActivityEntry>): ActivityEntry {
  return {
    seq: 1,
    ts: '2026-06-12T10:00:00.000Z',
    category: 'other',
    kind: 'item',
    id: 'abc',
    title: 'Thing',
    actor: null,
    fromState: null,
    toState: null,
    op: 'changed',
    ...over,
  }
}

describe('activityVerb', () => {
  it('renders a state transition', () => {
    expect(
      activityVerb(entry({ category: 'state', fromState: 'todo', toState: 'in-progress' })),
    ).toBe('todo → in-progress')
  })

  it('labels creates and comments', () => {
    expect(activityVerb(entry({ category: 'created', op: 'created' }))).toBe('created')
    expect(activityVerb(entry({ category: 'comment' }))).toBe('commented on')
  })

  it('distinguishes KB op verbs', () => {
    expect(activityVerb(entry({ category: 'kb', op: 'created' }))).toBe('created KB')
    expect(activityVerb(entry({ category: 'kb', op: 'changed' }))).toBe('edited KB')
    expect(activityVerb(entry({ category: 'kb', op: 'deleted' }))).toBe('deleted KB')
  })

  it('folds generic edits and deletes into other', () => {
    expect(activityVerb(entry({ category: 'other', op: 'changed' }))).toBe('edited')
    expect(activityVerb(entry({ category: 'other', op: 'deleted' }))).toBe('deleted')
  })
})

describe('relativeTime', () => {
  const base = Date.parse('2026-06-12T10:00:00.000Z')
  it('buckets recent times', () => {
    expect(relativeTime('2026-06-12T10:00:00.000Z', base)).toBe('just now')
    expect(relativeTime('2026-06-12T09:57:00.000Z', base)).toBe('3m')
    expect(relativeTime('2026-06-12T08:00:00.000Z', base)).toBe('2h')
    expect(relativeTime('2026-06-09T10:00:00.000Z', base)).toBe('3d')
  })
  it('returns empty for an unparseable timestamp', () => {
    expect(relativeTime('nope', base)).toBe('')
  })
})

describe('activityNavTarget', () => {
  it('routes items to the navigator with the id selected', () => {
    expect(activityNavTarget(entry({ kind: 'item', id: 'item-uuid' }))).toEqual({
      to: '/items',
      search: { id: 'item-uuid' },
    })
  })

  it('splits a kb ref into bucket + slug', () => {
    expect(activityNavTarget(entry({ kind: 'kb', id: 'adr:0002-foo' }))).toEqual({
      to: '/kb',
      search: { bucket: 'adr', slug: '0002-foo' },
    })
  })
})
