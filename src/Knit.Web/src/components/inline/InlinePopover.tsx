import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

interface Props {
  /** The trigger the panel hangs off. Null while it hasn't mounted. */
  anchor: HTMLElement | null
  onClose: () => void
  children: React.ReactNode
  className?: string
}

const GAP = 4
const MARGIN = 8

/**
 * The floating half of every inline editor: a panel anchored to the
 * value that was clicked. It floats in a portal on `position: fixed` rather
 * than replacing the value in flow — that is what keeps the frontmatter grid
 * from jumping when a field is opened, and what stops a panel from being
 * clipped by the detail pane's `overflow: auto`.
 *
 * Owns only placement and dismissal. Keyboard semantics belong to the content,
 * because a listbox and a calendar disagree about what an arrow key means.
 */
export function InlinePopover({ anchor, onClose, children, className = '' }: Props) {
  const panelRef = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)

  useLayoutEffect(() => {
    if (!anchor || !panelRef.current) return
    const a = anchor.getBoundingClientRect()
    const p = panelRef.current.getBoundingClientRect()
    // Flip above the value when the panel would hang off the bottom, and pull
    // it left when it would hang off the right — a field near either edge of
    // the detail pane is the normal case, not an edge case.
    const below = a.bottom + GAP
    const top = below + p.height > window.innerHeight - MARGIN && a.top - GAP - p.height > MARGIN
      ? a.top - GAP - p.height
      : below
    const left = Math.max(MARGIN, Math.min(a.left, window.innerWidth - p.width - MARGIN))
    setPos({ top, left })
  }, [anchor])

  useEffect(() => {
    const onPointerDown = (e: PointerEvent | MouseEvent) => {
      const t = e.target as Node | null
      if (!t) return
      if (panelRef.current?.contains(t) || anchor?.contains(t)) return
      onClose()
    }
    // Scrolling the pane would leave the panel stranded beside a value that has
    // moved on, so dismiss instead of chasing it.
    const onScroll = () => onClose()
    document.addEventListener('pointerdown', onPointerDown, true)
    window.addEventListener('resize', onScroll)
    window.addEventListener('scroll', onScroll, true)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true)
      window.removeEventListener('resize', onScroll)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [anchor, onClose])

  return createPortal(
    <div
      ref={panelRef}
      style={{ top: pos?.top ?? 0, left: pos?.left ?? 0, visibility: pos ? 'visible' : 'hidden' }}
      className={
        'fixed z-50 rounded-md border border-zinc-200 bg-white shadow-lg dark:border-zinc-700 dark:bg-zinc-900 '
        + className
      }
    >
      {children}
    </div>,
    document.body,
  )
}
