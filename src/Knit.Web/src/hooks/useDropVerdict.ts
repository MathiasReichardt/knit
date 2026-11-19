import { createContext, useContext } from 'react'
import { useDndContext, type Announcements } from '@dnd-kit/core'
import { DROP_NOOP, DROP_OK, type DropVerdict } from '../lib/dropVerdict'

/** Judges one drop zone's `data` against the drag in flight. A tree provides
 * it around its `DndContext`; without one, every zone is `ok`. */
export type DropCheck = (target: unknown) => DropVerdict

export const DropCheckContext = createContext<DropCheck | null>(null)

export function useDropCheck(): DropCheck | null {
  return useContext(DropCheckContext)
}

/**
 * The verdict for the zone under the pointer, or null with no drag in flight.
 * Nothing under the pointer is a `noop`: collisions are pointer-precise
 * (`dropOnlyWhereHovered`), so releasing there does nothing.
 *
 * Call it from inside a `DragOverlay` child: it reads the enclosing
 * `DndContext`, and rendering it there keeps the per-move re-render off the
 * row list.
 */
export function useDropVerdict(): DropVerdict | null {
  const { active, over } = useDndContext()
  const check = useDropCheck()
  if (active === null) return null
  if (over === null) return DROP_NOOP
  return check ? check(over.data.current) : DROP_OK
}

/** Screen-reader counterpart of the ghost: a refused zone is read out with its
 * reason and fix, the rest as dnd-kit's defaults would. */
export function dropAnnouncements(check: DropCheck): Announcements {
  return {
    onDragStart: ({ active }) => `Picked up ${active.id}.`,
    onDragOver: ({ active, over }) => {
      if (!over) return `${active.id} is no longer over a drop target.`
      const v = check(over.data.current)
      return v.kind === 'refused'
        ? `Cannot drop here. ${v.reason}.${v.hint ? ` ${v.hint}.` : ''}`
        : `${active.id} is over ${over.id}.`
    },
    onDragEnd: ({ active, over }) =>
      over ? `${active.id} was dropped over ${over.id}.` : `${active.id} was dropped.`,
    onDragCancel: ({ active }) => `Dragging was cancelled. ${active.id} was dropped.`,
  }
}
