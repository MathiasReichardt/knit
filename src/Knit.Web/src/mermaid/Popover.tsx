import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'

const GAP = 4
const MARGIN = 8

/** The part of the viewport `el` can show: clipped by every scrolling or clipping ancestor. */
function visibleBounds(el: Element) {
  let top = 0
  let bottom = window.innerHeight
  for (let p = el.parentElement; p; p = p.parentElement) {
    if (!/auto|scroll|hidden|clip/.test(getComputedStyle(p).overflowY)) continue
    const r = p.getBoundingClientRect()
    top = Math.max(top, r.top)
    bottom = Math.min(bottom, r.bottom)
  }
  return { top, bottom }
}

/** A click on the diagram that dismisses a popover only closes it: no pick, no maximize. */
function swallowNextClick() {
  const swallow = (e: MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
  }
  window.addEventListener('click', swallow, { capture: true, once: true })
  // A drag (pan) ends without a click; drop the listener once this press is over.
  window.addEventListener(
    'pointerup',
    () => window.setTimeout(() => window.removeEventListener('click', swallow, true)),
    { capture: true, once: true },
  )
}

/**
 * A toolbar button and its popover. The popover hangs below the toolbar it
 * sits in, or above it when there's more room there, so it opens in view.
 */
export function Popover({
  label,
  icon,
  open,
  onOpenChange,
  buttonClassName,
  width,
  children,
}: {
  /** The button's tooltip and the popover's accessible name. */
  label: string
  icon: ReactNode
  open: boolean
  onOpenChange: (open: boolean) => void
  buttonClassName: string
  /** Tailwind width class. */
  width: string
  children: ReactNode
}) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const [place, setPlace] = useState<{ up: boolean; maxHeight: number } | null>(null)

  useLayoutEffect(() => {
    const panel = panelRef.current
    const toolbar = panel?.parentElement
    if (!open || !panel || !toolbar) {
      setPlace(null)
      return
    }
    const t = toolbar.getBoundingClientRect()
    const b = visibleBounds(toolbar)
    const below = b.bottom - t.bottom - GAP - MARGIN
    const above = t.top - b.top - GAP - MARGIN
    const up = panel.scrollHeight > below && above > below
    setPlace({ up, maxHeight: Math.max(120, up ? above : below) })
  }, [open])

  useEffect(() => {
    if (!open) return
    panelRef.current?.focus()
    const close = (refocus: boolean) => {
      onOpenChange(false)
      if (refocus) buttonRef.current?.focus()
    }
    // Capture phase, like the overlay's: Esc closes the popover only.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.preventDefault()
      e.stopPropagation()
      close(true)
    }
    const onPointerDown = (e: PointerEvent) => {
      const t = e.target as Node
      if (panelRef.current?.contains(t) || buttonRef.current?.contains(t)) return
      close(false)
      if (t instanceof Element && t.closest('[data-diagram-viewport]')) swallowNextClick()
    }
    window.addEventListener('keydown', onKey, true)
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
      document.removeEventListener('pointerdown', onPointerDown, true)
    }
  }, [open, onOpenChange])

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={buttonClassName}
        title={label}
        aria-label={label}
        aria-expanded={open}
        onClick={() => onOpenChange(!open)}
      >
        {icon}
      </button>
      {open && (
        <div
          ref={panelRef}
          data-diagram-popover
          role="dialog"
          aria-label={label}
          tabIndex={-1}
          className={
            `absolute right-0 z-30 ${width} max-w-[calc(100vw-2rem)] rounded border border-zinc-200 ` +
            (place?.up ? 'bottom-full mb-1 ' : 'top-full mt-1 ') +
            'overflow-y-auto dark:border-zinc-700 bg-white dark:bg-zinc-900 p-2 text-xs text-zinc-800 dark:text-zinc-200 ' +
            'shadow-lg outline-none cursor-auto'
          }
          // Hidden until placed, so it never flashes on the wrong side.
          style={{ maxHeight: place?.maxHeight, visibility: place ? undefined : 'hidden' }}
        >
          {children}
        </div>
      )}
    </>
  )
}
