import { useEffect, useMemo, useRef, type ReactNode, type RefObject } from 'react'
import { flushSync } from 'react-dom'
import type { LucideIcon } from 'lucide-react'
import { extractOutline } from '../lib/outline'
import { activeHeadingId, holdHeading, scrollParent, scrollerTop } from '../lib/editPlace'
import { flashIn } from '../lib/headingFlash'
import { isViewOutlineKey } from '../lib/outlineNav'
import { EditBodyButton } from './EditBodyButton'
import { OutlinePanel, type OutlineDriver, type OutlineShortcut } from './OutlinePanel'
import type { OutlineEntry } from './OutlineRows'

const VIEW_SHORTCUT: OutlineShortcut = { aria: 'o', label: 'o', matches: isViewOutlineKey }

/** A section after the body (an item's Links, Comments): the element with `id`, its header the first child. */
export interface OutlineTail {
  id: string
  text: string
  icon: LucideIcon
  /** Opens the section if collapsed; runs before the jump. */
  reveal(): void
}

interface Props {
  /** The markdown the children render — the outline's source. */
  body: string
  docRef: string
  /** Off while the body is edited. */
  enabled?: boolean
  /** Counts the tail's sections too. */
  minHeadings?: number
  /** Trailing entries, listed after the body's headings. */
  tail?: OutlineTail[]
  /** Shows the edit pen: above the rail, or at the text's edge without an outline. */
  onEdit?: () => void
  className?: string
  children: ReactNode
}

/** The rendered body with its h2/h3 outline at the right edge (`OutlinePanel`). */
export function DocOutline({ body, docRef, enabled = true, minHeadings = 1, tail, onEdit, className = '', children }: Props) {
  const headings = useMemo(() => extractOutline(body), [body])
  const items = useMemo<OutlineEntry[]>(
    () => [...headings, ...(tail ?? []).map((t) => ({ depth: 2 as const, text: t.text, id: t.id, icon: t.icon }))],
    [headings, tail],
  )
  const contentRef = useRef<HTMLDivElement>(null)
  const tailRef = useRef<OutlineTail[]>([])
  useEffect(() => {
    tailRef.current = tail ?? []
  })
  const driver = useRenderedDriver(contentRef, tailRef)
  const outline = enabled && items.length >= minHeadings
  return (
    <div className={`flex ${className}`}>
      <div ref={contentRef} className="relative flex-1 min-w-0 flex flex-col">
        {!outline && onEdit && <EditBodyButton onClick={onEdit} />}
        {children}
      </div>
      {outline && (
        <OutlinePanel
          items={items}
          docRef={docRef}
          driver={driver}
          shortcut={VIEW_SHORTCUT}
          // The edit pen sits above the rail, so the overlay never covers it.
          header={onEdit && <div className="h-6 mb-2 flex justify-end"><EditBodyButton inline onClick={onEdit} /></div>}
          reserve={onEdit ? 48 : 8}
        />
      )}
    </div>
  )
}

function useRenderedDriver(
  contentRef: RefObject<HTMLDivElement | null>,
  tailRef: RefObject<OutlineTail[]>,
): OutlineDriver {
  const holdCleanup = useRef<(() => void) | null>(null)
  useEffect(() => () => holdCleanup.current?.(), [])
  return useMemo<OutlineDriver>(() => {
    const heading = (id: string) => contentRef.current?.querySelector<HTMLElement>(`#${CSS.escape(id)}`) ?? null
    const activeId = (content: HTMLElement) => {
      const sections = tailRef.current.map((t) => `#${CSS.escape(t.id)}`)
      return activeHeadingId(content, scrollParent(content), ['h2[id]', 'h3[id]', ...sections].join(', '))
    }
    return {
      scroller: () => (contentRef.current ? scrollParent(contentRef.current) : null),
      watch(onScroll) {
        const content = contentRef.current
        if (!content) return () => {}
        const scroller = scrollParent(content)
        const target: HTMLElement | Window = scroller === document.scrollingElement ? window : scroller
        target.addEventListener('scroll', onScroll, { passive: true })
        return () => target.removeEventListener('scroll', onScroll)
      },
      activeId: () => (contentRef.current ? activeId(contentRef.current) : null),
      jump(id, flash) {
        const content = contentRef.current
        const section = tailRef.current.find((t) => t.id === id)
        if (section) flushSync(() => section.reveal())
        const h = heading(id)
        if (!content || !h) return
        holdCleanup.current?.()
        holdCleanup.current = holdHeading(content, h, () => {
          holdCleanup.current = null
        })
        if (flash) flashIn(content, `#${CSS.escape(id)}${section ? ' > :first-child' : ''}`)
      },
      anchor() {
        const content = contentRef.current
        const id = content && activeId(content)
        const h = id && heading(id)
        if (!content || !h) return null
        const scroller = scrollParent(content)
        const offset = h.getBoundingClientRect().top - scrollerTop(scroller)
        return () => {
          const again = heading(h.id)
          if (again) scroller.scrollTop += again.getBoundingClientRect().top - scrollerTop(scroller) - offset
        }
      },
    }
  }, [contentRef, tailRef])
}
