// 2-D card-grid navigation for the resume-work landing page. The tree/
// list surfaces share `arrowNav.ts`, but that core is 1-D (flatten + expand/
// collapse) and can't express a grid where ↑/↓ must preserve the column. This
// is the small grid analogue; the page hook reuses `isArrowKey`/`isTyping` and
// the cursor/roving-focus pattern from the tree core, only the stepping differs.

import type { ArrowKey } from './arrowNav'

export interface GridCursor {
  row: number
  col: number
}

/** First focusable cell: col 0 of the first non-empty row, or null if all empty. */
export function firstCell(rowCounts: number[]): GridCursor | null {
  const row = rowCounts.findIndex((n) => n > 0)
  return row < 0 ? null : { row, col: 0 }
}

function clampCol(rowCounts: number[], row: number, col: number): number {
  return Math.max(0, Math.min(col, rowCounts[row]! - 1))
}

function nextNonEmpty(rowCounts: number[], from: number, dir: 1 | -1): number {
  for (let r = from; r >= 0 && r < rowCounts.length; r += dir) {
    if (rowCounts[r]! > 0) return r
  }
  return -1
}

/**
 * Step the card cursor for one arrow keypress. ←/→ move within a row (clamped
 * to its ends — →past the last card is the carousel's job, handled by the row).
 * ↑/↓ jump to the nearest *non-empty* row above/below, keeping the column
 * (clamped to that row's width). Pure; returns the (possibly unchanged) cursor.
 * A stale cursor (row gone/emptied) snaps to the first cell.
 */
export function gridNavStep(
  rowCounts: number[],
  cursor: GridCursor,
  key: ArrowKey,
): GridCursor {
  const { row } = cursor
  if (row < 0 || row >= rowCounts.length || rowCounts[row]! === 0) {
    return firstCell(rowCounts) ?? cursor
  }
  const col = clampCol(rowCounts, row, cursor.col)
  switch (key) {
    case 'ArrowLeft':
      return { row, col: Math.max(0, col - 1) }
    case 'ArrowRight':
      return { row, col: Math.min(rowCounts[row]! - 1, col + 1) }
    case 'ArrowUp': {
      const r = nextNonEmpty(rowCounts, row - 1, -1)
      return r < 0 ? { row, col } : { row: r, col: clampCol(rowCounts, r, col) }
    }
    case 'ArrowDown': {
      const r = nextNonEmpty(rowCounts, row + 1, 1)
      return r < 0 ? { row, col } : { row: r, col: clampCol(rowCounts, r, col) }
    }
  }
}
