import { describe, expect, it } from 'vitest'
import type { TimelineEdge, TimelineMilestone, TimelineNode } from '../api/types'
import {
  foldedTypes,
  addDaysIso,
  addMonthsIso,
  assignLanes,
  axisEnd,
  computeAxis,
  computeWindow,
  dayNumber,
  depChains,
  isoFromDayNumber,
  milestoneFlagSpans,
  milestonesInWindow,
  isoWeekNumber,
  lookBackCount,
  segmentTicks,
  segmentUnitFor,
  snapWindowOut,
  splitByWindow,
  startOfMonthIso,
  startOfWeekIso,
} from './timeline'

function node(partial: Partial<TimelineNode> & { id: string }): TimelineNode {
  return {
    shortId: partial.id.slice(0, 8),
    title: partial.id,
    type: 'task',
    state: 'todo',
    due: null,
    priority: 'normal',
    effort: null,
    assignee: null,
    parent: null,
    blocked: null,
    milestone: null,
    parentPath: null,
    atRisk: false,
    impliedDeadline: null,
    atRiskBlockers: [],
    spanStart: null,
    spanEnd: null,
    subtreeAtRisk: false,
    rolledUp: [],
    ancestorIds: [],
    remaining: [],
    openBelow: 0,
    ghost: false,
    ...partial,
  }
}

const TODAY = '2026-07-10'

describe('day math', () => {
  it('round-trips day numbers', () => {
    expect(isoFromDayNumber(dayNumber('2026-07-10'))).toBe('2026-07-10')
    expect(addDaysIso('2026-07-10', 7)).toBe('2026-07-17')
    expect(addDaysIso('2026-07-01', -1)).toBe('2026-06-30')
  })

  it('clamps month-end like DateOnly.AddMonths', () => {
    expect(addMonthsIso('2026-01-31', 1)).toBe('2026-02-28')
    expect(addMonthsIso('2026-07-10', 1)).toBe('2026-08-10')
    expect(addMonthsIso('2026-12-15', 1)).toBe('2027-01-15')
  })
})

describe('computeWindow', () => {
  it('defaults future-focused from today', () => {
    const w = computeWindow([], 'month', TODAY)
    expect(w.from).toBe(TODAY)
    expect(w.to).toBe('2026-08-10')
  })

  it('extends the start back to the earliest overdue node', () => {
    const nodes = [
      node({ id: 'overdue', spanStart: '2026-07-01', spanEnd: '2026-07-01' }),
      // Present only because `includeDone` asked for it — so it widens too.
      node({ id: 'closed-overdue', state: 'done', spanStart: '2026-06-01', spanEnd: '2026-06-01' }),
    ]
    const w = computeWindow(nodes, 'week', TODAY)
    expect(w.from).toBe('2026-06-01')
    expect(w.to).toBe('2026-07-17')
  })

  it("range 'all' has no upper bound", () => {
    expect(computeWindow([], 'all', TODAY).to).toBeNull()
  })

  it('a planned past-target milestone pulls the start back; reached/dropped do not', () => {
    const planned = computeWindow([], 'month', TODAY, [
      { project: 'p', slug: 'missed', title: 'x', target: '2026-06-01', state: 'planned' },
    ])
    expect(planned.from).toBe('2026-06-01')
    const reached = computeWindow([], 'month', TODAY, [
      { project: 'p', slug: 'done', title: 'x', target: '2026-06-01', state: 'reached' },
    ])
    expect(reached.from).toBe(TODAY)
  })
})

describe('splitByWindow', () => {
  it('separates undated nodes and trims spans outside the window', () => {
    const nodes = [
      node({ id: 'in', spanStart: '2026-07-15', spanEnd: '2026-07-15' }),
      node({ id: 'far', spanStart: '2027-05-01', spanEnd: '2027-05-01' }),
      node({ id: 'tray' }),
      node({ id: 'overlaps', spanStart: '2026-07-01', spanEnd: '2026-07-20' }),
    ]
    const { dated, undated } = splitByWindow(nodes, { from: TODAY, to: '2026-08-10' })
    expect(dated.map((n) => n.id)).toEqual(['in', 'overlaps'])
    expect(undated.map((n) => n.id)).toEqual(['tray'])
  })

  it('keeps everything dated when the window is unbounded', () => {
    const nodes = [node({ id: 'far', spanStart: '2029-01-01', spanEnd: '2029-01-01' })]
    expect(splitByWindow(nodes, { from: TODAY, to: null }).dated).toHaveLength(1)
  })
})

describe('milestonesInWindow', () => {
  const ms = (slug: string, target: string): TimelineMilestone => ({
    project: 'p', slug, title: slug, target, state: 'planned',
  })

  it('keeps only milestones whose target is inside [from, to] inclusive', () => {
    const all = [ms('before', '2026-06-30'), ms('edge-from', '2026-07-10'),
      ms('mid', '2026-07-20'), ms('edge-to', '2026-08-10'), ms('after', '2026-08-11')]
    const got = milestonesInWindow(all, '2026-07-10', '2026-08-10')
    expect(got.map((m) => m.slug)).toEqual(['edge-from', 'mid', 'edge-to'])
  })
})

describe('axisEnd', () => {
  it('uses the window bound when present', () => {
    expect(axisEnd([], { from: TODAY, to: '2026-08-10' }, TODAY)).toBe('2026-08-10')
  })

  it("for 'all' extends to the latest span end, minimum two weeks", () => {
    expect(axisEnd([], { from: TODAY, to: null }, TODAY)).toBe('2026-07-24')
    const far = [node({ id: 'x', spanStart: '2027-01-05', spanEnd: '2027-01-05' })]
    expect(axisEnd(far, { from: TODAY, to: null }, TODAY)).toBe('2027-01-05')
  })

  it("for 'all' a far milestone extends the axis past the last span", () => {
    const far: TimelineMilestone = { project: 'p', slug: 'v2', title: 'x', target: '2027-03-01', state: 'planned' }
    expect(axisEnd([], { from: TODAY, to: null }, TODAY, [far])).toBe('2027-03-01')
  })
})

describe('segment units', () => {
  it('uses weeks up to a ~4-month span and months beyond', () => {
    expect(segmentUnitFor(7)).toBe('week')
    expect(segmentUnitFor(120)).toBe('week')
    expect(segmentUnitFor(121)).toBe('month')
    expect(segmentUnitFor(365)).toBe('month')
  })
})

describe('segment boundaries', () => {
  it('snaps back to the Monday on or before', () => {
    // 2026-07-13 is a Monday.
    expect(startOfWeekIso('2026-07-16')).toBe('2026-07-13')
    expect(startOfWeekIso('2026-07-13')).toBe('2026-07-13')
    expect(startOfWeekIso('2026-07-12')).toBe('2026-07-06')
  })

  it('snaps back to the first of the month', () => {
    expect(startOfMonthIso('2026-07-16')).toBe('2026-07-01')
    expect(startOfMonthIso('2026-07-01')).toBe('2026-07-01')
  })

  it('widens both ends to week boundaries', () => {
    expect(snapWindowOut('2026-07-16', '2026-08-04', 'week')).toEqual({
      from: '2026-07-13',
      to: '2026-08-10',
    })
  })

  it('widens both ends to month boundaries', () => {
    expect(snapWindowOut('2026-07-16', '2027-01-04', 'month')).toEqual({
      from: '2026-07-01',
      to: '2027-02-01',
    })
  })

  it('leaves an end that is already a boundary alone', () => {
    expect(snapWindowOut('2026-07-13', '2026-08-10', 'week')).toEqual({
      from: '2026-07-13',
      to: '2026-08-10',
    })
    expect(snapWindowOut('2026-07-01', '2026-09-01', 'month')).toEqual({
      from: '2026-07-01',
      to: '2026-09-01',
    })
  })
})

describe('isoWeekNumber', () => {
  it('numbers by the first-Thursday rule', () => {
    expect(isoWeekNumber('2026-01-01')).toBe(1)
    expect(isoWeekNumber('2026-07-13')).toBe(29)
    // 2027-01-01 is a Friday, so it still belongs to 2026's W53.
    expect(isoWeekNumber('2027-01-01')).toBe(53)
    expect(isoWeekNumber('2027-01-04')).toBe(1)
  })
})

describe('segmentTicks', () => {
  it('emits one tick per week, naming the month-opening week', () => {
    const ticks = segmentTicks('2026-07-13', '2026-08-10', 'week')
    expect(ticks.map((t) => t.iso)).toEqual([
      '2026-07-13', '2026-07-20', '2026-07-27', '2026-08-03', '2026-08-10',
    ])
    expect(ticks[0]!.label).toBe('W29')
    expect(ticks[0]!.major).toBe(false)
    // 2026-08-03 is the first Monday whose week opens August.
    expect(ticks[3]!.label).toBe('Aug · W32')
    expect(ticks[3]!.major).toBe(true)
  })

  it('emits one tick per month, naming the year on January', () => {
    const ticks = segmentTicks('2026-11-01', '2027-02-01', 'month')
    expect(ticks.map((t) => t.label)).toEqual(['Nov', 'Dec', 'Jan 27', 'Feb'])
    expect(ticks.map((t) => t.major)).toEqual([false, false, true, false])
  })

  it('starts at the first boundary inside an unsnapped window', () => {
    expect(segmentTicks('2026-07-14', '2026-07-27', 'week').map((t) => t.iso)).toEqual([
      '2026-07-20', '2026-07-27',
    ])
  })
})

describe('computeAxis', () => {
  // 2026-07-10 is a Friday; its week starts Monday 2026-07-06.
  it('starts the week-unit ranges on the Monday of the current week', () => {
    for (const range of ['week', 'month', '3m'] as const) {
      const axis = computeAxis([], range, TODAY)
      expect(axis.unit).toBe('week')
      expect(axis.from).toBe('2026-07-06')
      expect(startOfWeekIso(axis.to)).toBe(axis.to)
    }
  })

  it('starts the year range on the 1st of the current month', () => {
    const axis = computeAxis([], 'year', TODAY)
    expect(axis.unit).toBe('month')
    expect(axis.from).toBe('2026-07-01')
    // A year out (2027-07-10), snapped out to the next month boundary.
    expect(axis.to).toBe('2027-08-01')
  })

  it('extends back past an overdue open item, snapped to the segment', () => {
    const overdue = [node({ id: 'a', due: '2026-06-17', spanStart: '2026-06-17', spanEnd: '2026-06-17' })]
    // Week unit: back to the Monday on or before 2026-06-17 (a Wednesday).
    expect(computeAxis(overdue, 'week', TODAY).from).toBe('2026-06-15')
    // Month unit: back to the 1st of that month.
    expect(computeAxis(overdue, 'year', TODAY).from).toBe('2026-06-01')
    // ...and the item is still inside the drawn axis, which is the point.
    const axis = computeAxis(overdue, 'week', TODAY)
    expect(splitByWindow(overdue, axis).dated).toHaveLength(1)
  })

  it('does not extend back for a ghost overdue item', () => {
    const ghost = [node({ id: 'g', ghost: true, due: '2026-06-17', spanStart: '2026-06-17', spanEnd: '2026-06-17' })]
    expect(computeAxis(ghost, 'week', TODAY).from).toBe('2026-07-06')
  })

  // Done nodes are only present when `includeDone` asked for them, so
  // they widen the window like open ones — otherwise they would be fetched
  // and then dropped behind the start.
  it('extends back for a done item, so it is actually shown', () => {
    const done = [node({ id: 'a', state: 'done', due: '2026-06-17', spanStart: '2026-06-17', spanEnd: '2026-06-17' })]
    const axis = computeAxis(done, 'week', TODAY)
    expect(axis.from).toBe('2026-06-15')
    expect(splitByWindow(done, axis).dated).toHaveLength(1)
  })

  it('extends back for a missed planned milestone', () => {
    const missed = [{ project: 'p', slug: 'm1', title: 'M1', target: '2026-06-17', state: 'planned' as const }]
    expect(computeAxis([], 'week', TODAY, missed).from).toBe('2026-06-15')
  })

  // The unit follows the span actually drawn, not the range asked for.
  it('switches a week range to month segments once the look-back drags it out', () => {
    const old_ = [node({ id: 'a', due: '2026-01-05', spanStart: '2026-01-05', spanEnd: '2026-01-05' })]
    expect(computeAxis(old_, 'week', TODAY).unit).toBe('month')
  })

  it('pins the start to today with the look-back off', () => {
    const overdue = [node({ id: 'a', due: '2026-06-17', spanStart: '2026-06-17', spanEnd: '2026-06-17' })]
    const axis = computeAxis(overdue, 'week', TODAY, [], { lookBack: false })
    expect(axis.from).toBe('2026-07-06')
    // Off means the overdue item leaves the canvas for the at-risk tray.
    expect(splitByWindow(overdue, axis).dated).toHaveLength(0)
  })

  it('draws a custom window verbatim and never extends it', () => {
    const overdue = [node({ id: 'a', due: '2026-06-17', spanStart: '2026-06-17', spanEnd: '2026-06-17' })]
    const custom = { from: '2026-04-06', to: '2026-06-29' }
    const axis = computeAxis(overdue, 'custom', TODAY, [], { custom })
    expect(axis.from).toBe('2026-04-06')
    expect(axis.to).toBe('2026-06-29')
    // 84 days — inside the week threshold, unlike the 'year' preset.
    expect(axis.unit).toBe('week')
  })

  it('collapses a reversed custom pick instead of drawing nothing', () => {
    const axis = computeAxis([], 'custom', TODAY, [], { custom: { from: '2026-06-29', to: '2026-04-06' } })
    expect(axis.from).toBe('2026-06-29')
    expect(axis.to).toBe('2026-06-29')
  })
})

describe('lookBackCount', () => {
  it('counts the spans the look-back is holding in view', () => {
    const nodes = [
      node({ id: 'past', spanStart: '2026-06-01', spanEnd: '2026-06-17' }),
      node({ id: 'straddling', spanStart: '2026-06-01', spanEnd: '2026-08-01' }),
      node({ id: 'future', spanStart: '2026-08-01', spanEnd: '2026-08-02' }),
      node({ id: 'undated' }),
      node({ id: 'ghost', ghost: true, spanStart: '2026-06-01', spanEnd: '2026-06-17' }),
    ]
    expect(lookBackCount(nodes, TODAY)).toBe(1)
  })
})

describe('assignLanes', () => {
  it('packs non-overlapping spans into one lane', () => {
    const { lanes, laneCount } = assignLanes([
      { id: 'a', start: 0, end: 10 },
      { id: 'b', start: 20, end: 30 },
    ])
    expect(laneCount).toBe(1)
    expect(lanes.get('a')).toBe(0)
    expect(lanes.get('b')).toBe(0)
  })

  it('stacks colliding spans and reuses freed lanes', () => {
    const { lanes, laneCount } = assignLanes([
      { id: 'a', start: 0, end: 100 },
      { id: 'b', start: 50, end: 60 },
      { id: 'c', start: 70, end: 90 },
      { id: 'd', start: 110, end: 120 },
    ])
    expect(lanes.get('a')).toBe(0)
    expect(lanes.get('b')).toBe(1)
    expect(lanes.get('c')).toBe(1) // lane 1 freed at 60
    expect(lanes.get('d')).toBe(0)
    expect(laneCount).toBe(2)
  })
})

describe('milestoneFlagSpans', () => {
  const ms = (slug: string, target: string): TimelineMilestone => ({
    project: 'p', slug, title: slug, target, state: 'planned',
  })
  // 10 px per day from the window start, so a date maps to a round x.
  const x = (iso: string) => (dayNumber(iso) - dayNumber('2026-07-01')) * 10

  it('gives two milestones on one date separate rows', () => {
    const { lanes, laneCount } = assignLanes(
      milestoneFlagSpans([ms('v1', '2026-07-10'), ms('v2', '2026-07-10')], x),
    )
    expect(laneCount).toBe(2)
    expect(lanes.get('p/v1')).toBe(0)
    expect(lanes.get('p/v2')).toBe(1)
  })

  it('gives a third milestone on that date a third row rather than reusing the first', () => {
    const { lanes, laneCount } = assignLanes(
      milestoneFlagSpans([ms('v1', '2026-07-10'), ms('v2', '2026-07-10'), ms('v3', '2026-07-10')], x),
    )
    expect(laneCount).toBe(3)
    expect(lanes.get('p/v3')).toBe(2)
  })

  it('keeps flags far enough apart on one row', () => {
    const { laneCount } = assignLanes(
      milestoneFlagSpans([ms('v1', '2026-07-10'), ms('v2', '2026-08-10')], x),
    )
    expect(laneCount).toBe(1)
  })
})

describe('depChains', () => {
  const edges: TimelineEdge[] = [
    { from: 'a', to: 'b', atRisk: true },
    { from: 'b', to: 'c', atRisk: false },
    { from: 'x', to: 'c', atRisk: false },
  ]

  it('walks transitively in both directions', () => {
    const chains = depChains(edges, 'b')
    expect([...chains.upstream]).toEqual(['a'])
    expect([...chains.downstream]).toEqual(['c'])
    const forC = depChains(edges, 'c')
    expect(forC.upstream).toEqual(new Set(['a', 'b', 'x']))
    expect(forC.downstream.size).toBe(0)
  })

  it('is cycle-safe and excludes the item itself', () => {
    const cyclic: TimelineEdge[] = [
      { from: 'a', to: 'b', atRisk: false },
      { from: 'b', to: 'a', atRisk: false },
    ]
    const chains = depChains(cyclic, 'a')
    expect(chains.upstream).toEqual(new Set(['b']))
    expect(chains.downstream).toEqual(new Set(['b']))
    expect(chains.upstream.has('a')).toBe(false)
  })
})

describe('foldedTypes', () => {
  it('folds nothing at depth all', () => {
    expect(foldedTypes('all')).toEqual([])
  })

  it('folds everything finer than the depth', () => {
    expect(foldedTypes('feature')).toEqual(['task', 'todo'])
    expect(foldedTypes('project')).toEqual(['epic', 'feature', 'bug', 'task', 'todo'])
  })
})
