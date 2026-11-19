import { describe, it, expect } from 'vitest'
import { gridNavStep, firstCell, type GridCursor } from './landingGridNav'

const C = (row: number, col: number): GridCursor => ({ row, col })

describe('firstCell', () => {
  it('returns col 0 of the first non-empty row', () => {
    expect(firstCell([3, 5, 2])).toEqual({ row: 0, col: 0 })
  })
  it('skips leading empty rows', () => {
    expect(firstCell([0, 0, 4])).toEqual({ row: 2, col: 0 })
  })
  it('returns null when every row is empty', () => {
    expect(firstCell([0, 0, 0])).toBeNull()
  })
})

describe('gridNavStep — horizontal', () => {
  const rows = [5, 3, 2]
  it('moves right within the row', () => {
    expect(gridNavStep(rows, C(0, 1), 'ArrowRight')).toEqual(C(0, 2))
  })
  it('clamps at the last card (paging is the row’s job)', () => {
    expect(gridNavStep(rows, C(0, 4), 'ArrowRight')).toEqual(C(0, 4))
  })
  it('moves left within the row', () => {
    expect(gridNavStep(rows, C(0, 2), 'ArrowLeft')).toEqual(C(0, 1))
  })
  it('clamps at the first card', () => {
    expect(gridNavStep(rows, C(0, 0), 'ArrowLeft')).toEqual(C(0, 0))
  })
})

describe('gridNavStep — vertical', () => {
  const rows = [5, 3, 2]
  it('moves down keeping the column', () => {
    expect(gridNavStep(rows, C(0, 2), 'ArrowDown')).toEqual(C(1, 2))
  })
  it('clamps the column to the narrower target row', () => {
    expect(gridNavStep(rows, C(0, 4), 'ArrowDown')).toEqual(C(1, 2))
  })
  it('moves up keeping the column', () => {
    expect(gridNavStep(rows, C(2, 1), 'ArrowUp')).toEqual(C(1, 1))
  })
  it('clamps at the top row', () => {
    expect(gridNavStep(rows, C(0, 1), 'ArrowUp')).toEqual(C(0, 1))
  })
  it('clamps at the bottom row', () => {
    expect(gridNavStep(rows, C(2, 0), 'ArrowDown')).toEqual(C(2, 0))
  })
})

describe('gridNavStep — empty rows', () => {
  it('skips an empty row going down', () => {
    expect(gridNavStep([3, 0, 4], C(0, 2), 'ArrowDown')).toEqual(C(2, 2))
  })
  it('skips an empty row going up', () => {
    expect(gridNavStep([3, 0, 4], C(2, 1), 'ArrowUp')).toEqual(C(0, 1))
  })
  it('snaps a stale cursor (its row emptied) to the first cell', () => {
    expect(gridNavStep([0, 0, 4], C(0, 0), 'ArrowRight')).toEqual(C(2, 0))
  })
})

// The landing page is four rows (projects · items · designs · KB),
// and row 0 is always present — it holds a single all-items stand-in card when
// the repo has no projects.
describe('gridNavStep — four-row landing (projects row first)', () => {
  const populated = [3, 5, 2, 4]
  const standIn = [1, 5, 2, 4]

  it('steps from the projects row into items keeping the column', () => {
    expect(gridNavStep(populated, C(0, 2), 'ArrowDown')).toEqual(C(1, 2))
  })
  it('reaches the KB row from the top', () => {
    expect(gridNavStep(populated, C(2, 1), 'ArrowDown')).toEqual(C(3, 1))
  })
  it('clamps onto the single stand-in card coming back up', () => {
    expect(gridNavStep(standIn, C(1, 4), 'ArrowUp')).toEqual(C(0, 0))
  })
  it('has nowhere to go sideways on the stand-in row', () => {
    expect(gridNavStep(standIn, C(0, 0), 'ArrowRight')).toEqual(C(0, 0))
  })
})
