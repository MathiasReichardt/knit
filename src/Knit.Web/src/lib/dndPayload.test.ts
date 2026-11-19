import { describe, expect, it } from 'vitest'
import { buildDndPlacement, checkItemDrop, type DropKind } from './dndPayload'
import type { ItemSummary } from '../api/types'

function item(
  id: string,
  rank: string | null,
  parent: string | null = null,
  type: ItemSummary['type'] = 'task',
): ItemSummary {
  return {
    id,
    shortId: id,
    title: id,
    type,
    state: 'todo',
    assignee: null,
    parent,
    rank,
    effort: null,
    due: null,
    priority: 'normal',
    created: '2026-09-16T00:00:00Z',
    blocked: false,
    blockedReason: null,
    hasChildren: false,
    tags: [],
    filePath: `items/${id}/index.md`,
  }
}

function ctx(...items: ItemSummary[]) {
  return { itemsById: new Map(items.map((i) => [i.id, i])) }
}

const drag = (...ids: string[]) => ({
  source: 'tree' as const,
  ids,
  primaryId: ids[0]!,
  totalCount: ids.length,
})

describe('buildDndPlacement', () => {
  it('anchors a drop on the row above it', () => {
    const target: DropKind = { kind: 'tree-gap', parentId: null, prevId: 'a', nextId: 'b' }
    const built = buildDndPlacement(drag('x'), target, ctx(item('x', 't'), item('a', 'g'), item('b', 'n')))
    expect('placement' in built && built.placement).toEqual({ ids: ['x'], parent: null, after: 'a' })
  })

  it('anchors on the row below when the drop is at the top of a group', () => {
    const target: DropKind = { kind: 'tree-gap', parentId: 'p', prevId: null, nextId: 'b' }
    const built = buildDndPlacement(
      drag('x'),
      target,
      ctx(item('x', 't'), item('p', 'n'), item('b', 'g', 'p')),
    )
    expect('placement' in built && built.placement).toEqual({ ids: ['x'], parent: 'p', before: 'b' })
  })

  it('sends no neighbour when both are in the drag — the server appends', () => {
    const target: DropKind = { kind: 'tree-gap', parentId: null, prevId: 'a', nextId: 'b' }
    const built = buildDndPlacement(drag('a', 'b'), target, ctx(item('a', 'g'), item('b', 'n')))
    expect('placement' in built && built.placement).toEqual({ ids: ['a', 'b'], parent: null })
  })

  it('appends onto a node dropped on, with no neighbour', () => {
    const target: DropKind = { kind: 'tree-node', nodeId: 'p' }
    const built = buildDndPlacement(drag('x'), target, ctx(item('x', 't'), item('p', 'n')))
    expect('placement' in built && built.placement).toEqual({ ids: ['x'], parent: 'p' })
  })

  it('keeps the drag order by default and the rank order on request', () => {
    const c = ctx(item('x', 'w'), item('y', 'g'), item('a', 'b'))
    const target: DropKind = { kind: 'tree-gap', parentId: null, prevId: 'a', nextId: null }
    const asDragged = buildDndPlacement(drag('x', 'y'), target, c)
    expect('placement' in asDragged && asDragged.placement?.ids).toEqual(['x', 'y'])
    const byRank = buildDndPlacement(drag('x', 'y'), target, c, { keepRelativeOrder: true })
    expect('placement' in byRank && byRank.placement?.ids).toEqual(['y', 'x'])
  })

  it('refuses an illegal parent without asking the server', () => {
    const target: DropKind = { kind: 'tree-node', nodeId: 'p' }
    const built = buildDndPlacement(
      drag('x'),
      target,
      ctx(item('x', 't', null, 'epic'), item('p', 'n', null, 'task')),
    )
    expect('error' in built).toBe(true)
  })

  it('treats a drop onto the dragged row itself as a silent no-op', () => {
    const target: DropKind = { kind: 'tree-node', nodeId: 'x' }
    const built = buildDndPlacement(drag('x'), target, ctx(item('x', 't')))
    expect('placement' in built && built.placement).toBeNull()
  })
})

describe('checkItemDrop', () => {
  const onto = (nodeId: string): DropKind => ({ kind: 'tree-node', nodeId })

  it('refuses a feature onto a feature with the rule and a fix', () => {
    const v = checkItemDrop(drag('x'), onto('p'), ctx(item('x', 'a', null, 'feature'), item('p', 'b', null, 'feature')))
    expect(v).toEqual({
      kind: 'refused',
      reason: 'A feature cannot be a child of a feature',
      hint: 'A feature belongs under project, epic or the repo root — or retype it to task',
    })
  })

  it('counts the refused share of a multi-drag', () => {
    const v = checkItemDrop(
      drag('x', 'y'),
      onto('p'),
      ctx(item('x', 'a', null, 'feature'), item('y', 'b', null, 'task'), item('p', 'c', null, 'feature')),
    )
    expect(v.kind === 'refused' && v.reason).toBe("1 of 2 can't move: A feature cannot be a child of a feature")
  })

  it('is a no-op onto the dragged row or below it', () => {
    const c = ctx(item('x', 'a', null, 'feature'), item('k', 'b', 'x', 'task'))
    expect(checkItemDrop(drag('x'), onto('x'), c).kind).toBe('noop')
    expect(checkItemDrop(drag('x'), onto('k'), c).kind).toBe('noop')
  })

  it('accepts a legal parent', () => {
    expect(checkItemDrop(drag('x'), onto('p'), ctx(item('x', 'a'), item('p', 'b', null, 'feature'))).kind).toBe('ok')
  })
})

describe('state sort (ST6)', () => {
  // A group under p, shown by state: done d1 · todo t1 t2 · draft r1.
  const st = (id: string, rank: string, state: ItemSummary['state']) => ({ ...item(id, rank, 'p'), state })
  const group = [
    item('p', 'a', null, 'feature'),
    st('d1', 'z', 'done'),
    st('t1', 'b', 'todo'),
    st('t2', 'x', 'todo'),
    st('r1', 'm', 'draft'),
  ]
  const sctx = (...extra: ItemSummary[]) => ({ ...ctx(...group, ...extra), treeSort: 'state' as const })
  const gap = (prevId: string | null, nextId: string | null): DropKind => ({ kind: 'tree-gap', parentId: 'p', prevId, nextId })

  it('reorders within a band', () => {
    expect(checkItemDrop(drag('t2'), gap(null, 't1'), sctx()).kind).toBe('ok')
    const built = buildDndPlacement(drag('t2'), gap('d1', 't1'), sctx())
    expect('placement' in built && built.placement).toEqual({ ids: ['t2'], parent: 'p', before: 't1' })
  })

  it('refuses a drop into another band, naming the band to stay in', () => {
    const v = checkItemDrop(drag('t1'), gap('r1', null), sctx())
    expect(v).toMatchObject({ kind: 'refused', reason: 'Sorted by state — reorder within Todo' })
    expect(v.kind === 'refused' && v.hint).toContain('rank order')
  })

  it('takes a boundary gap only on the dragged state’s side', () => {
    // Between the last todo and the draft: a todo lands after t2, a draft before r1.
    const asTodo = buildDndPlacement(drag('t1'), gap('t2', 'r1'), sctx())
    expect('placement' in asTodo && asTodo.placement).toEqual({ ids: ['t1'], parent: 'p', after: 't2' })
    const draft = st('r2', 'c', 'draft')
    const asDraft = buildDndPlacement(drag('r2'), gap('t2', 'r1'), sctx(draft))
    expect('placement' in asDraft && asDraft.placement).toEqual({ ids: ['r2'], parent: 'p', before: 'r1' })
  })

  it('lets an item open a band its new group lacks', () => {
    const review = { ...item('v', 'q', null), state: 'review' as const }
    expect(checkItemDrop(drag('v'), gap('d1', 't1'), sctx(review)).kind).toBe('ok')
  })

  it('refuses a band drop when its own band is elsewhere in the new group', () => {
    const todo = item('o', 'q', null)
    expect(checkItemDrop(drag('o'), gap('d1', null), sctx(todo)).kind).toBe('refused')
  })

  it('refuses a mixed-state drag onto a gap but not onto a row', () => {
    expect(checkItemDrop(drag('t1', 'r1'), gap('t2', null), sctx()).kind).toBe('refused')
    const onto: DropKind = { kind: 'tree-node', nodeId: 'p' }
    expect(checkItemDrop(drag('t1', 'r1'), onto, sctx()).kind).toBe('ok')
  })

  it('leaves the row in place when its only same-state neighbour is in the drag', () => {
    const built = buildDndPlacement(drag('t1', 't2'), gap('t2', 'r1'), sctx())
    expect('placement' in built && built.placement).toBeNull()
  })

  it('ignores bands under the rank sort', () => {
    expect(checkItemDrop(drag('t1'), gap('r1', null), { ...sctx(), treeSort: 'rank' }).kind).toBe('ok')
  })
})
