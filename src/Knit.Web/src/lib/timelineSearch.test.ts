import { describe, it, expect } from 'vitest'
import { TIMELINE_FILTER_DEFAULTS } from '../stores/timelineFilters'
import { timelineViewDefaults } from '../stores/timelinePrefs'
import { sameSpec, searchToSpec, specToSearch, timelineSearchSchema, type TimelineSpec } from './timelineSearch'

const spec = (over: { view?: Partial<TimelineSpec['view']>; filters?: Partial<TimelineSpec['filters']> } = {}) => ({
  view: { ...timelineViewDefaults(), ...over.view },
  filters: { ...TIMELINE_FILTER_DEFAULTS, ...over.filters },
})

describe('timeline search', () => {
  it('names no view when only the selection is set', () => {
    expect(searchToSpec({ id: 'x', ms: 'p/m' })).toBeNull()
  })

  it('always writes depth and range, the rest only off-default', () => {
    expect(specToSearch(spec())).toEqual({ depth: 'all', range: 'month' })
  })

  it('round-trips a full view', () => {
    const s = spec({
      view: { depth: 'epic', range: 'custom', customFrom: '2026-01-01', customTo: '2026-03-01', hideDone: false, showMilestones: false },
      filters: { types: ['bug'], mine: true, minPriority: 'important', tags: ['a', 'b'], slate: 's1', milestone: 'p/m1', project: 'p', under: 'abc', overdue: true, blocked: 'only' },
    })
    expect(searchToSpec(specToSearch(s))).toEqual(s)
  })

  it('treats keys a link leaves out as defaults, not remembered values', () => {
    const got = searchToSpec({ range: 'week' })!
    expect(got.view.depth).toBe('all')
    expect(got.filters).toEqual(TIMELINE_FILTER_DEFAULTS)
  })

  it('parses URL strings', () => {
    const parsed = timelineSearchSchema.parse({ range: 'year', overdue: '1', type: 'bug', tag: ['x'] })
    expect(searchToSpec(parsed)!.filters).toMatchObject({ overdue: true, types: ['bug'], tags: ['x'] })
  })

  it('compares specs ignoring selection and key order', () => {
    expect(sameSpec({ range: 'week', depth: 'all', id: '1' }, { depth: 'all', range: 'week' })).toBe(true)
    expect(sameSpec({ range: 'week' }, { range: 'month' })).toBe(false)
  })
})
