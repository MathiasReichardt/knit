import { describe, it, expect } from 'vitest'
import type { CollisionDetection } from '@dnd-kit/core'
import { dropOnlyWhereHovered } from './dndCollision'

type Args = Parameters<CollisionDetection>[0]

const rect = (top: number, left = 0, width = 200, height = 30) => ({
  top,
  left,
  width,
  height,
  right: left + width,
  bottom: top + height,
})

// Two stacked rows, as the tree renders them: x 0..200, y 0..30 and 30..60.
const ROWS = new Map([
  ['row-a', rect(0)],
  ['row-b', rect(30)],
])

const args = (pointer: { x: number; y: number } | null): Args =>
  ({
    active: { id: 'dragged' },
    collisionRect: rect(0),
    droppableRects: ROWS,
    droppableContainers: [...ROWS.keys()].map((id) => ({ id })),
    pointerCoordinates: pointer,
  }) as unknown as Args

describe('dropOnlyWhereHovered', () => {
  it('reports the droppable under the pointer', () => {
    expect(dropOnlyWhereHovered(args({ x: 100, y: 45 })).map((c) => c.id)).toEqual(['row-b'])
  })

  it('reports nothing when the pointer is past the last row', () => {
    expect(dropOnlyWhereHovered(args({ x: 100, y: 400 }))).toEqual([])
  })

  it('reports nothing when the pointer is beside the rows', () => {
    expect(dropOnlyWhereHovered(args({ x: 600, y: 15 }))).toEqual([])
  })

  it('falls back to distance ranking for keyboard drags, which have no pointer', () => {
    expect(dropOnlyWhereHovered(args(null)).map((c) => c.id)).toEqual(['row-a', 'row-b'])
  })
})
