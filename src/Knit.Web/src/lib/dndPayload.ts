import type { ItemSummary } from '../api/types'
import type { TreeSort } from '../api/client'
import { dropRefusal, parentAllowed } from './hierarchy'
import { STATE_LABEL } from './glyphs'
import { DROP_NOOP, DROP_OK, type DropVerdict } from './dropVerdict'
import type { DndPlacement } from './applyDnd'

/**
 * Source of a drag: which view triggered it. Carried in the dnd-kit
 * active.data.current payload so droppables in mixed views can decide
 * whether to accept the drag.
 */
export type DragSource = 'tree' | 'list'

export interface DragPayload {
  source: DragSource
  /** Ordered ids in drag — single-select drag is [id], multi-select is
   * the selection in current view order. Only these need PATCHes;
   * descendants follow implicitly via the parent reference. */
  ids: string[]
  /** The actual id under the cursor (anchor of the drag). */
  primaryId: string
  /** Visible total = ids + descendants found in the current view's items
   * map. Used only for the drag-overlay count badge so the user sees
   * how many items will visually move. */
  totalCount: number
}

/**
 * Count the dragged ids plus every descendant reachable in `itemsById`.
 * Descendants visible in the current view contribute to the displayed
 * count; descendants hidden by the current filter aren't counted —
 * acceptable approximation.
 */
export function countWithDescendants(
  ids: string[],
  itemsById: Map<string, ItemSummary>,
): number {
  const childrenOf = new Map<string, string[]>()
  for (const i of itemsById.values()) {
    const p = i.parent
    if (!p) continue
    const arr = childrenOf.get(p)
    if (arr) arr.push(i.id)
    else childrenOf.set(p, [i.id])
  }
  const seen = new Set<string>()
  const stack = [...ids]
  while (stack.length) {
    const id = stack.pop()!
    if (seen.has(id)) continue
    seen.add(id)
    const kids = childrenOf.get(id)
    if (kids) for (const k of kids) stack.push(k)
  }
  return seen.size
}

/** Drop kinds the DnD overlay recognises. A gap names its neighbour **rows**,
 * not their ranks — the server places the drop, so the client only has to say
 * where it landed (30). */
export type DropKind =
  | { kind: 'tree-gap'; parentId: string | null; prevId: string | null; nextId: string | null }
  | { kind: 'tree-node'; nodeId: string }
  | { kind: 'list-gap'; parentId: string | null; prevId: string | null; nextId: string | null }

export interface DropContext {
  /** Lookup an item by id from the current view's data. */
  itemsById: Map<string, ItemSummary>
  /** How the tree orders siblings; absent = rank (the list never sorts by band). */
  treeSort?: TreeSort
}

/**
 * Decide whether a drag onto a node would be a self-or-descendant drop
 * (cycle) — never allowed.
 */
export function isSelfOrDescendant(
  ids: string[],
  targetId: string | null,
  itemsById: Map<string, ItemSummary>,
): boolean {
  if (targetId === null) return false
  if (ids.includes(targetId)) return true
  // Walk targetId upward; if it passes through any dragged id, that's a cycle.
  let cur: string | null = targetId
  const seen = new Set<string>()
  while (cur && !seen.has(cur)) {
    seen.add(cur)
    if (ids.includes(cur)) return true
    cur = itemsById.get(cur)?.parent ?? null
  }
  return false
}

function resolveTarget(target: DropKind, ctx: DropContext) {
  const parentId = target.kind === 'tree-node' ? target.nodeId : target.parentId
  return {
    parentId,
    parentType: parentId ? (ctx.itemsById.get(parentId)?.type ?? null) : null,
    // Drop onto a row: becomes its last child. No neighbour — the server
    // appends to the end of that group.
    prevId: target.kind === 'tree-node' ? null : target.prevId,
    nextId: target.kind === 'tree-node' ? null : target.nextId,
  }
}

/**
 * Whether dropping `payload` on `target` would land. Self-or-descendant drops
 * are a no-op — the user almost certainly released over their own row by
 * accident, and dropping back is the way out. Hierarchy violations are
 * refused with the rule and a way around it.
 */
export function checkItemDrop(payload: DragPayload, target: DropKind, ctx: DropContext): DropVerdict {
  const draggedItems = payload.ids
    .map((id) => ctx.itemsById.get(id))
    .filter((x): x is ItemSummary => !!x)
  if (draggedItems.length === 0) return DROP_NOOP

  const { parentId, parentType } = resolveTarget(target, ctx)
  if (draggedItems.some((it) => isSelfOrDescendant([it.id], parentId, ctx.itemsById))) return DROP_NOOP

  const refused = draggedItems.filter((it) => !parentAllowed(it.type, parentType))
  if (refused.length === 0) return target.kind === 'tree-gap' && ctx.treeSort === 'state'
    ? checkStateGap(payload, draggedItems, target, ctx)
    : DROP_OK

  const first = refused[0]!
  const childTypes = [...ctx.itemsById.values()].filter((i) => i.parent === first.id).map((i) => i.type)
  const { reason, hint } = dropRefusal(first.type, parentType, childTypes)
  return {
    kind: 'refused',
    reason: draggedItems.length > 1 ? `${refused.length} of ${draggedItems.length} can't move: ${reason}` : reason,
    hint,
  }
}

const STATE_HINT = 'Switch the tree to rank order to place it anywhere'

/**
 * Under the state sort a group shows one band per state, so a gap only takes
 * items of the state on one of its sides (ST6) — anywhere else the row would
 * land at a rank its band never shows. A gap with no band of the dragged state
 * in its group is free: the item opens that band. Dragging never changes state.
 */
function checkStateGap(
  payload: DragPayload,
  draggedItems: ItemSummary[],
  target: Extract<DropKind, { kind: 'tree-gap' }>,
  ctx: DropContext,
): DropVerdict {
  const state = draggedItems[0]!.state
  if (draggedItems.some((i) => i.state !== state))
    return { kind: 'refused', reason: 'Sorted by state — drag items of one state at a time', hint: STATE_HINT }
  const stateOf = (id: string | null) => (id ? ctx.itemsById.get(id)?.state : undefined)
  if (stateOf(target.prevId) === state || stateOf(target.nextId) === state) return DROP_OK
  const dragged = new Set(payload.ids)
  const bandElsewhere = [...ctx.itemsById.values()].some(
    (i) => (i.parent ?? null) === target.parentId && !dragged.has(i.id) && i.state === state,
  )
  return bandElsewhere
    ? { kind: 'refused', reason: `Sorted by state — reorder within ${STATE_LABEL[state]}`, hint: STATE_HINT }
    : DROP_OK
}

/**
 * Given a drag payload + the chosen drop target + the current view data,
 * produce the placement to POST. Returns a null placement when the drop is a
 * no-op, or `{ error }` when {@link checkItemDrop} refuses it.
 *
 * No rank arithmetic here: the request names the target parent and one
 * neighbour row, and the server computes every rank from the group it can see
 * (30). The cycle and hierarchy checks stay client-side anyway — refusing an
 * illegal drop without a round-trip is worth the duplicated rule.
 */
export function buildDndPlacement(
  payload: DragPayload,
  target: DropKind,
  ctx: DropContext,
  opts: { keepRelativeOrder?: boolean } = {},
): { placement: DndPlacement | null } | { error: string } {
  const verdict = checkItemDrop(payload, target, ctx)
  if (verdict.kind === 'noop') return { placement: null }
  if (verdict.kind === 'refused') return { error: verdict.reason }

  const draggedItems = payload.ids
    .map((id) => ctx.itemsById.get(id))
    .filter((x): x is ItemSummary => !!x)
  const { parentId, prevId, nextId } = resolveTarget(target, ctx)
  const ordered = opts.keepRelativeOrder
    ? sortByCurrentRank(draggedItems)
    : draggedItems

  // A neighbour that is itself being dragged is no anchor — it is about to
  // move. Prefer the row above, fall back to the row below, and send neither
  // (= append) when both are in the drag.
  const dragged = new Set(payload.ids)
  const placement: DndPlacement = {
    ids: ordered.map((i) => i.id),
    parent: parentId,
  }
  if (target.kind === 'tree-gap' && ctx.treeSort === 'state') {
    // Anchor on the side of the gap in the dragged state: `after` the last done
    // would rank a todo among the done rows, which the band then hides.
    const state = draggedItems[0]!.state
    const inBand = (id: string | null): id is string => !!id && ctx.itemsById.get(id)?.state === state
    if (inBand(prevId) && !dragged.has(prevId)) placement.after = prevId
    else if (inBand(nextId) && !dragged.has(nextId)) placement.before = nextId
    else if (inBand(prevId) || inBand(nextId)) {
      // The only same-state neighbour is itself in the drag: the rows stay put.
      if (draggedItems.every((i) => (i.parent ?? null) === parentId)) return { placement: null }
    } else if (prevId && !dragged.has(prevId)) placement.after = prevId
    else if (nextId && !dragged.has(nextId)) placement.before = nextId
    return { placement }
  }
  if (prevId && !dragged.has(prevId)) placement.after = prevId
  else if (nextId && !dragged.has(nextId)) placement.before = nextId

  return { placement }
}

function sortByCurrentRank(items: ItemSummary[]): ItemSummary[] {
  return [...items].sort((a, b) => (a.rank ?? '').localeCompare(b.rank ?? ''))
}
