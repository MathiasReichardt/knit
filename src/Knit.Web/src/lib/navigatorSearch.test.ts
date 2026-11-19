import { describe, it, expect } from 'vitest'
import { useNavigator } from '../stores/navigator'
import {
  URL_STATE_KEYS,
  filterPart,
  isSpec,
  navigatorSearchSchema,
  searchToState,
  stateToSearch,
} from './navigatorSearch'

const initial = () => useNavigator.getInitialState()

describe('navigatorSearch tag round-trip', () => {
  it('omits tag from the URL when no tag is selected', () => {
    expect(stateToSearch(initial()).tag).toBeUndefined()
  })

  it('mirrors selected tags to and from the URL search', () => {
    const state = { ...initial(), tags: ['api', 'security'] }
    const search = stateToSearch(state)
    expect(search.tag).toEqual(['api', 'security'])
    expect(searchToState(search).tags).toEqual(['api', 'security'])
  })

  it('coerces a single ?tag= value into a one-element array', () => {
    const parsed = navigatorSearchSchema.parse({ tag: 'api' })
    expect(searchToState(parsed).tags).toEqual(['api'])
  })
})

describe('navigatorSearch slate round-trip', () => {
  it('omits slate keys from the URL when no slate filter is set', () => {
    const search = stateToSearch(initial())
    expect(search.slate).toBeUndefined()
    expect(search.noSlate).toBeUndefined()
  })

  it('mirrors a selected slate to and from the URL search', () => {
    const state = { ...initial(), slate: 'q1-push' }
    const search = stateToSearch(state)
    expect(search.slate).toBe('q1-push')
    expect(searchToState(search).slate).toBe('q1-push')
  })

  it('mirrors noSlate and parses ?noSlate=1', () => {
    const search = stateToSearch({ ...initial(), noSlate: true })
    expect(search.noSlate).toBe(true)
    const parsed = navigatorSearchSchema.parse({ noSlate: '1' })
    expect(searchToState(parsed).noSlate).toBe(true)
  })
})

describe('navigatorSearch state round-trip', () => {
  it('mirrors the review state through the URL search', () => {
    const search = stateToSearch({ ...initial(), states: ['review'] })
    expect(search.state).toEqual(['review'])
    expect(searchToState(navigatorSearchSchema.parse({ state: 'review' })).states).toEqual(['review'])
  })
})

describe('navigatorSearch as a complete view', () => {
  it('resets a key the URL leaves out to its default', () => {
    const state = searchToState(navigatorSearchSchema.parse({ slate: 's1' }))
    expect(state.under).toBeNull()
    expect(state.excludeStates).toEqual(['dismissed'])
    expect(state.sort).toEqual({ key: 'tree', dir: 'asc' })
  })

  it('round-trips every URL-carried key', () => {
    const state = {
      ...initial(),
      view: 'list' as const,
      sort: { key: 'due' as const, dir: 'desc' as const },
      excludeStates: [],
      minPriority: 'important' as const,
      project: 'p',
      under: 'abc',
      query: 'foo bar',
    }
    const search = navigatorSearchSchema.parse(
      JSON.parse(JSON.stringify(stateToSearch(state))),
    )
    expect(searchToState(search)).toEqual(
      Object.fromEntries(URL_STATE_KEYS.map((k) => [k, state[k]])),
    )
  })

  it('writes the view even when unfiltered, so a synced URL is never bare', () => {
    const search = stateToSearch(initial())
    expect(search).toEqual({ view: 'tree' })
    expect(isSpec(search)).toBe(true)
  })

  it('treats a URL carrying only the selected id as bare', () => {
    expect(isSpec({ id: 'x' })).toBe(false)
    expect(filterPart({ id: 'x', slate: 's' })).toEqual({ slate: 's' })
  })
})

describe('navigatorSearch open filter', () => {
  it('drops state filters for an open URL, so the query never pairs them', () => {
    const state = searchToState(navigatorSearchSchema.parse({ view: 'list', open: 'true' }))
    expect(state.open).toBe(true)
    expect(state.excludeStates).toEqual([])
    expect(state.states).toEqual([])
  })

  it('writes an open view without state keys', () => {
    expect(stateToSearch({ ...initial(), open: true, excludeStates: [] })).toEqual({ view: 'tree', open: true })
  })
})
