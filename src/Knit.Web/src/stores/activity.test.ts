import { beforeEach, describe, expect, it } from 'vitest'
import type { ActivityEntry } from '../api/types'
import { isVisible, useActivity } from './activity'

function entry(seq: number, over: Partial<ActivityEntry> = {}): ActivityEntry {
  return {
    seq,
    ts: '2026-06-12T10:00:00.000Z',
    category: 'state',
    kind: 'item',
    id: `id-${seq}`,
    title: `Item ${seq}`,
    actor: null,
    fromState: 'todo',
    toState: 'done',
    op: 'changed',
    ...over,
  }
}

beforeEach(() => {
  useActivity.setState({ entries: [] })
})

describe('useActivity', () => {
  it('keeps entries newest-first on push', () => {
    useActivity.getState().push(entry(1))
    useActivity.getState().push(entry(3))
    useActivity.getState().push(entry(2))
    expect(useActivity.getState().entries.map((e) => e.seq)).toEqual([3, 2, 1])
  })

  it('dedupes by seq', () => {
    useActivity.getState().push(entry(5, { title: 'first' }))
    useActivity.getState().push(entry(5, { title: 'second' }))
    const { entries } = useActivity.getState()
    expect(entries).toHaveLength(1)
    expect(entries[0].title).toBe('second')
  })

  it('caps at 500 entries, dropping the oldest', () => {
    useActivity.getState().hydrate(
      Array.from({ length: 600 }, (_, i) => entry(i + 1)),
    )
    const { entries } = useActivity.getState()
    expect(entries).toHaveLength(500)
    expect(entries[0].seq).toBe(600)
    expect(entries[entries.length - 1].seq).toBe(101)
  })

  it('merges a hydrate into live pushes without losing either', () => {
    useActivity.getState().push(entry(10))
    useActivity.getState().hydrate([entry(8), entry(9)])
    expect(useActivity.getState().entries.map((e) => e.seq)).toEqual([10, 9, 8])
  })
})

describe('isVisible', () => {
  const filters = { created: true, state: false, comment: true, kb: false, other: false }
  it('honours the per-category chip', () => {
    expect(isVisible(filters, 'created')).toBe(true)
    expect(isVisible(filters, 'state')).toBe(false)
    expect(isVisible(filters, 'kb')).toBe(false)
  })
  it('always shows other regardless of its flag', () => {
    expect(isVisible(filters, 'other')).toBe(true)
  })
})
