import { describe, it, expect, beforeEach } from 'vitest'
import { useNavigator, buildItemQuery, hasAnyActiveFilter } from './navigator'

beforeEach(() => {
  // Replace the whole store with its initial state between tests; persist
  // rehydrates from an empty localStorage in jsdom, so this is the default.
  useNavigator.setState(useNavigator.getInitialState(), true)
})

const get = () => useNavigator.getState()

describe('buildItemQuery', () => {
  it('projects defaults: dismissed excluded, empty arrays dropped', () => {
    const q = buildItemQuery(get())
    expect(q.excludeState).toEqual(['dismissed'])
    expect(q.type).toBeUndefined()
    expect(q.state).toBeUndefined()
    expect(q.open).toBeUndefined()
    expect(q.sort).toEqual({ key: 'tree', dir: 'asc' })
    expect(q.filterMode).toBe('context')
  })

  it('includes types once toggled', () => {
    get().toggleType('task')
    expect(buildItemQuery(get()).type).toEqual(['task'])
  })

  it('drops the tag param until a tag is selected, then projects it', () => {
    expect(buildItemQuery(get()).tag).toBeUndefined()
    get().toggleTag('api')
    get().toggleTag('security')
    expect(buildItemQuery(get()).tag).toEqual(['api', 'security'])
  })

  it('hides todos by default via excludeType (dev lens default)', () => {
    expect(buildItemQuery(get()).excludeType).toEqual(['todo'])
  })

  it('PM lens hides dev items (projects + todos stay framed)', () => {
    get().setLens('pm')
    expect(buildItemQuery(get()).excludeType).toEqual(['epic', 'feature', 'bug', 'task'])
  })

  it('All lens excludes no type band', () => {
    get().setLens('all')
    expect(buildItemQuery(get()).excludeType).toBeUndefined()
  })

  it('a per-type chip include overrides the lens-hidden band', () => {
    // dev lens hides todo; explicitly including todo cancels that exclusion
    get().toggleType('todo')
    expect(buildItemQuery(get()).excludeType).toBeUndefined()
    expect(buildItemQuery(get()).type).toEqual(['todo'])
  })

  // Regression (React #185): buildItemQuery is read through `useShallow`, so the
  // derived excludeType array MUST be the same reference across calls for one
  // state snapshot — a fresh array each call spins an infinite render loop.
  it('returns a referentially-stable excludeType for the same snapshot', () => {
    const s = get()
    expect(buildItemQuery(s).excludeType).toBe(buildItemQuery(s).excludeType)
  })

  it('projects the milestone ref once set, drops it otherwise', () => {
    expect(buildItemQuery(get()).milestone).toBeUndefined()
    get().setMilestone('relaunch/beta')
    expect(buildItemQuery(get()).milestone).toBe('relaunch/beta')
  })

  it('projects the project slug once set, drops it otherwise', () => {
    expect(buildItemQuery(get()).project).toBeUndefined()
    get().setProject('relaunch')
    expect(buildItemQuery(get()).project).toBe('relaunch')
  })
})

describe('hasAnyActiveFilter', () => {
  it('is false for the default state (only dismissed excluded)', () => {
    expect(hasAnyActiveFilter(get())).toBe(false)
  })

  it('becomes true after a type filter is added', () => {
    get().toggleType('bug')
    expect(hasAnyActiveFilter(get())).toBe(true)
  })

  it('changing the lens is a view choice, not an active filter', () => {
    get().setLens('all')
    expect(hasAnyActiveFilter(get())).toBe(false)
    get().setLens('pm')
    expect(hasAnyActiveFilter(get())).toBe(false)
  })

  it('becomes true when a query is set', () => {
    get().setQuery('foo')
    expect(hasAnyActiveFilter(get())).toBe(true)
  })

  it('becomes true when a tag is selected', () => {
    get().toggleTag('wip')
    expect(hasAnyActiveFilter(get())).toBe(true)
  })

  it('becomes true when a milestone is selected, false once cleared', () => {
    get().setMilestone('relaunch/beta')
    expect(hasAnyActiveFilter(get())).toBe(true)
    get().setMilestone(null)
    expect(hasAnyActiveFilter(get())).toBe(false)
  })

  it('becomes true when a project is selected, false once cleared', () => {
    get().setProject('relaunch')
    expect(hasAnyActiveFilter(get())).toBe(true)
    get().setProject(null)
    expect(hasAnyActiveFilter(get())).toBe(false)
  })
})

describe('toggleTag', () => {
  it('adds then removes a tag', () => {
    get().toggleTag('api')
    expect(get().tags).toEqual(['api'])
    get().toggleTag('api')
    expect(get().tags).toEqual([])
  })

  it('clearTags resets the selection', () => {
    get().toggleTag('a')
    get().toggleTag('b')
    get().clearTags()
    expect(get().tags).toEqual([])
  })
})

describe('toggleType', () => {
  it('adds then removes a type', () => {
    get().toggleType('feature')
    expect(get().types).toEqual(['feature'])
    get().toggleType('feature')
    expect(get().types).toEqual([])
  })
})

describe('cycleState tri-state', () => {
  it('cycles unfiltered → include → exclude → unfiltered', () => {
    get().cycleState('done')
    expect(get().states).toContain('done')
    expect(get().excludeStates).not.toContain('done')

    get().cycleState('done')
    expect(get().states).not.toContain('done')
    expect(get().excludeStates).toContain('done')

    get().cycleState('done')
    expect(get().states).not.toContain('done')
    expect(get().excludeStates).not.toContain('done')
  })

  it('clears the open flag when a state filter is engaged', () => {
    get().setOpen(true)
    get().cycleState('done')
    expect(get().open).toBe(false)
  })
})

describe('setOpen', () => {
  it('clears include/exclude state filters when turned on', () => {
    get().cycleState('done') // -> states: ['done']
    get().setOpen(true)
    expect(get().open).toBe(true)
    expect(get().states).toEqual([])
    expect(get().excludeStates).toEqual([])
  })
})

describe('clearFilters', () => {
  it('resets everything back to defaults', () => {
    get().toggleType('task')
    get().setQuery('foo')
    get().setMine(true)
    get().toggleTag('api')
    get().setProject('relaunch')
    get().clearFilters()
    const s = get()
    expect(s.types).toEqual([])
    expect(s.query).toBe('')
    expect(s.mine).toBe(false)
    expect(s.tags).toEqual([])
    expect(s.project).toBeNull()
    expect(s.excludeStates).toEqual(['dismissed'])
    expect(hasAnyActiveFilter(s)).toBe(false)
  })
})

describe('per-repo persistence', () => {
  const stored = () => JSON.parse(localStorage.getItem('knit.navigator') ?? '{}').state

  beforeEach(() => {
    localStorage.clear()
  })

  it('persists every filter, not a whitelist, under the repo key', () => {
    get().setRepo('c:/repos/a')
    get().setSlate('sprint-3')
    get().setQuery('api')
    get().setMinPriority('important')
    get().setProject('relaunch')

    const saved = stored().byRepo['c:/repos/a']
    expect(saved.slate).toBe('sprint-3')
    expect(saved.query).toBe('api')
    expect(saved.minPriority).toBe('important')
    expect(saved.project).toBe('relaunch')
    // Transients never reach storage — they are rebound per session.
    expect(saved.repo).toBeUndefined()
    expect(saved.byRepo).toBeUndefined()
  })

  it('does not carry one repo filters into another', () => {
    get().setRepo('c:/repos/a')
    get().setSlate('sprint-3')
    get().setMilestone('m1')

    get().setRepo('c:/repos/b')
    expect(get().slate).toBeNull()
    expect(get().milestone).toBeNull()

    get().setRepo('c:/repos/a')
    expect(get().slate).toBe('sprint-3')
    expect(get().milestone).toBe('m1')
  })

  it('drops a remembered slate the repo no longer has', () => {
    get().setRepo('c:/repos/a')
    get().setSlate('sprint-3')

    get().pruneSlate(['sprint-4'])
    expect(get().slate).toBeNull()
  })

  it('keeps a slate that still exists', () => {
    get().setRepo('c:/repos/a')
    get().setSlate('sprint-3')

    get().pruneSlate(['sprint-3', 'sprint-4'])
    expect(get().slate).toBe('sprint-3')
  })

  it('lets the URL outrank what the repo remembered', () => {
    get().setRepo('c:/repos/a')
    get().setSlate('remembered')
    useNavigator.setState({ repo: null })

    // What NavigatorUrlSync does on mount, before /api/root resolves.
    useNavigator.setState({ slate: 'from-url' })
    get().noteUrlKeys(['slate'])
    get().setRepo('c:/repos/a')

    expect(get().slate).toBe('from-url')
  })
})
