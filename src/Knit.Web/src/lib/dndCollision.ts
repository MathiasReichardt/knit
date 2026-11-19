import { closestCenter, pointerWithin, type CollisionDetection } from '@dnd-kit/core'

/**
 * Pointer-precise collisions with no distance fallback: a release outside
 * every droppable is a drop on the floor, not a move to the nearest row.
 * `closestCenter` ranks every droppable by distance with no cutoff, so
 * releasing anywhere on the page — over the details pane, the nav rail, off
 * the window — silently landed the drag on whichever row happened to be
 * closest.
 *
 * Keyboard drags carry no pointer, so they keep the distance ranking; there is
 * nothing else to resolve a target from.
 */
export const dropOnlyWhereHovered: CollisionDetection = (args) =>
  args.pointerCoordinates ? pointerWithin(args) : closestCenter(args)
