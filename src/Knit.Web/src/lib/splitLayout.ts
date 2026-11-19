/** Which pane of a split is hidden. Never both: each step moves one notch along
 * `left ← none → right`. */
export type Collapsed = 'none' | 'left' | 'right'

export function parseCollapsed(raw: string | null): Collapsed {
  return raw === 'left' || raw === 'right' ? raw : 'none'
}

/** One click on a divider chevron (or `[` / `]`): toward `dir` collapses that
 * side, unless the opposite side is collapsed — then it reopens it. */
export function stepCollapsed(state: Collapsed, dir: 'left' | 'right'): Collapsed {
  if (state === 'none') return dir
  return state === dir ? state : 'none'
}

export interface DragResult {
  collapsed: Collapsed
  /** Left pane width in px; only meaningful when `collapsed` is `none`. */
  leftWidth: number
}

/** Maps the divider position (px from the container's left edge) to a layout.
 * Dragging a pane below half its minimum snaps it shut; between half and the
 * minimum it sticks at the minimum. */
export function resolveDrag(pos: number, containerWidth: number, min: number): DragResult {
  const rightWidth = containerWidth - pos
  if (pos < min / 2) return { collapsed: 'left', leftWidth: min }
  if (rightWidth < min / 2) return { collapsed: 'right', leftWidth: Math.max(min, containerWidth - min) }
  const leftWidth = Math.max(min, Math.min(pos, containerWidth - min))
  return { collapsed: 'none', leftWidth }
}
