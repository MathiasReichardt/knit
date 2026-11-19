import { useEffect, useState, type RefObject } from 'react'

/**
 * Observed content width of an element. Coalesced to the trailing animation
 * frame: a ResizeObserver fires per frame during a sidebar drag, and the
 * timeline's lane packing is far too expensive to redo that often.
 *
 * `initial` seeds the first paint — the observer only fires after layout, so a
 * 0 seed would flash a degenerate canvas.
 */
export function useElementWidth<T extends HTMLElement>(
  ref: RefObject<T | null>,
  initial: number,
): number {
  const [width, setWidth] = useState(initial)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    let frame = 0
    const observer = new ResizeObserver((entries) => {
      const next = entries[0]?.contentRect.width ?? 0
      if (next <= 0) return
      if (frame) cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => {
        frame = 0
        setWidth(next)
      })
    })
    observer.observe(el)
    return () => {
      if (frame) cancelAnimationFrame(frame)
      observer.disconnect()
    }
  }, [ref])
  return width
}
