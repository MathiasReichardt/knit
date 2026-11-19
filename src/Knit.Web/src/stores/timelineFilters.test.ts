import { describe, it, expect, beforeEach } from 'vitest'
import { useNavigator } from './navigator'
import { TIMELINE_FILTER_DEFAULTS, hasTimelineFilter, timelineFilterQuery, useTimelineFilters } from './timelineFilters'

beforeEach(() => {
  useTimelineFilters.setState(useTimelineFilters.getInitialState(), true)
  useNavigator.setState(useNavigator.getInitialState(), true)
})

const get = () => useTimelineFilters.getState()

describe('timeline filters', () => {
  it('are independent of the tree/list navigator', () => {
    get().toggleType('bug')
    get().setMilestone('p/m1')
    useNavigator.getState().setSlate('s1')
    expect(useNavigator.getState().types).toEqual([])
    expect(useNavigator.getState().milestone).toBeNull()
    expect(get().slate).toBeNull()
  })

  it('remember filters per repo', () => {
    get().setRepo('a')
    get().setOverdue(true)
    get().setRepo('b')
    expect(get().overdue).toBe(false)
    get().setRepo('a')
    expect(get().overdue).toBe(true)
  })

  it('let a link outrank what the repo remembered', () => {
    useTimelineFilters.setState({ byRepo: { a: { ...TIMELINE_FILTER_DEFAULTS, mine: true } } })
    get().applyLink({ ...TIMELINE_FILTER_DEFAULTS, blocked: 'only' })
    get().setRepo('a')
    expect(get().mine).toBe(false)
    expect(get().blocked).toBe('only')
    expect(get().linked).toBeNull()
  })

  it('project into the timeline query, dropping unset filters', () => {
    expect(hasTimelineFilter(get())).toBe(false)
    get().setBlocked('hide')
    get().toggleTag('ui')
    expect(timelineFilterQuery(get())).toMatchObject({ blocked: 'hide', tag: ['ui'] })
    get().clearFilters()
    expect(hasTimelineFilter(get())).toBe(false)
  })
})
