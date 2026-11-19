import type { HeldBy } from '../api/types'

interface Blockable {
  blocked: boolean
  blockedReason: string | null
  heldBy?: HeldBy | undefined
}

/**
 * Row marker for a blocked item: its own block, or a blocked ancestor holding
 * it. Null ⇔ no marker. `held` rows render dimmer; the label says which.
 */
export function blockMark(item: Blockable): { held: boolean; label: string } | null {
  if (item.blocked) return { held: false, label: item.blockedReason ? `Blocked: ${item.blockedReason}` : 'Blocked' }
  if (item.heldBy) return { held: true, label: `Blocked by parent: ${item.heldBy.title}` }
  return null
}
