/** Translate in px, then scale, around the viewport's top-left corner. */
export interface View {
  x: number
  y: number
  scale: number
}

/** Identity: the diagram as mermaid sized it, fitted to its container. */
export const FIT: View = { x: 0, y: 0, scale: 1 }
export const MIN_SCALE = 0.25
export const MAX_SCALE = 8
export const ZOOM_STEP = 1.25
export const DRAG_THRESHOLD = 4

const clamp = (s: number) => Math.min(MAX_SCALE, Math.max(MIN_SCALE, s))

/** Zoom by `factor`, keeping the viewport point (px, py) over the same diagram point. */
export function zoomAt(v: View, factor: number, px: number, py: number): View {
  const scale = clamp(v.scale * factor)
  const k = scale / v.scale
  return { x: px - (px - v.x) * k, y: py - (py - v.y) * k, scale }
}

/** Scale and centre a w × h diagram into a vw × vh viewport. */
export function fitView(vw: number, vh: number, w: number, h: number): View {
  if (w <= 0 || h <= 0) return FIT
  const scale = clamp(Math.min(vw / w, vh / h))
  return { x: (vw - w * scale) / 2, y: (vh - h * scale) / 2, scale }
}

/** Ctrl+wheel and touchpad pinch (which browsers report as Ctrl+wheel). */
export function wheelFactor(deltaY: number, deltaMode: number): number {
  const px = deltaMode === 1 ? deltaY * 16 : deltaMode === 2 ? deltaY * 400 : deltaY
  return Math.exp(-px * 0.002)
}

export const transformOf = (v: View) => `translate(${v.x}px, ${v.y}px) scale(${v.scale})`

/**
 * Click vs drag: a press that stays within the threshold is a click; one that
 * leaves it pans, and the click the browser fires after it is swallowed.
 */
export function createGesture(threshold = DRAG_THRESHOLD) {
  let start: { x: number; y: number } | null = null
  let last = { x: 0, y: 0 }
  let dragging = false
  let swallowClick = false
  return {
    down(x: number, y: number) {
      start = { x, y }
      last = { x, y }
      dragging = false
      swallowClick = false
    },
    /** The pan delta since the last move, or null while still a click. */
    move(x: number, y: number): { dx: number; dy: number } | null {
      if (!start) return null
      if (!dragging) {
        if (Math.hypot(x - start.x, y - start.y) <= threshold) return null
        dragging = true
      }
      const d = { dx: x - last.x, dy: y - last.y }
      last = { x, y }
      return d
    },
    up() {
      if (dragging) swallowClick = true
      start = null
      dragging = false
    },
    /** Whether the click that follows the press is a pick. */
    click(): boolean {
      const ok = !swallowClick
      swallowClick = false
      return ok
    },
    get dragging() {
      return dragging
    },
  }
}

export type Gesture = ReturnType<typeof createGesture>
