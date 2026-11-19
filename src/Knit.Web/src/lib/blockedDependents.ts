import type { BlockedDependent, Item } from '../api/types'

/** A closed item and the open items still blocked by it. */
export interface ClosedBlocker {
  id: string
  shortId: string
  dependents: BlockedDependent[]
}

/**
 * The closed items worth a dialog, from the items a close call returned (the
 * item plus any cascaded ones). Empty ⇔ no dialog: the call closed nothing,
 * or nothing was blocked by what it closed.
 */
export function closedBlockers(items: Item[]): ClosedBlocker[] {
  return items
    .filter((i) => (i.blockedDependents?.length ?? 0) > 0)
    .map((i) => ({ id: i.id, shortId: i.shortId, dependents: i.blockedDependents! }))
}

/**
 * Unblock when the closed item was the dependent's only impediment; else
 * remove just that link — the item stays blocked by what remains.
 */
export function dependentAction(d: BlockedDependent): 'unblock' | 'remove-link' {
  return d.remaining === null ? 'unblock' : 'remove-link'
}
