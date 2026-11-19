import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { flushSync } from 'react-dom'
import { Pin, PinOff } from 'lucide-react'
import { isOutlineKey, outlinePinned, outlineStep } from '../lib/outlineNav'
import { isTyping } from '../lib/arrowNav'
import { useOutlinePrefs } from '../stores/outlinePrefs'
import { OutlineRows, type OutlineEntry } from './OutlineRows'
import { useCopySectionLink } from '../hooks/useCopySectionLink'

/** What the panel steers: the rendered body or the editor. */
export interface OutlineDriver {
  /** The element that scrolls the body; its height bounds the panel. */
  scroller(): HTMLElement | null
  /** Calls `onScroll` whenever the body scrolls; returns the unsubscribe. */
  watch(onScroll: () => void): () => void
  /** The heading being read. */
  activeId(): string | null
  /** Brings the heading to the top; `flash` on a pointer pick. */
  jump(id: string, flash: boolean): void
  /** Before the pin re-wraps the text: returns what puts the reading place back. */
  anchor(): (() => void) | null
  /** Where focus goes when nothing else asks for it (the editor). */
  home?(): void
}

export interface OutlineShortcut {
  /** `aria-keyshortcuts` value. */
  aria: string
  /** As shown in tooltips. */
  label: string
  matches(e: KeyboardEvent): boolean
  /** Fires from inside a text field too (the editor's chord). */
  whileTyping?: boolean
}

// Per shortcut, only the most recently mounted outline answers it, should two bodies share a screen.
const mounted = new Map<string, symbol[]>()

const OPEN_DELAY_MS = 40
const CLOSE_DELAY_MS = 250

/**
 * The h2/h3 outline column: a minimap rail that opens as an overlay on hover,
 * click or the shortcut, or a pinned column the text reflows around. ↑/↓ jump
 * straight to a section, `c` copies its link, Esc leaves.
 */
export function OutlinePanel({
  items,
  docRef,
  driver,
  shortcut,
  header,
  reserve = 0,
}: {
  items: OutlineEntry[]
  docRef: string
  driver: OutlineDriver
  shortcut: OutlineShortcut
  /** Sits above the rail (the edit pen); the overlay never covers it. */
  header?: ReactNode
  /** Scroller height the panel leaves free (the header's). */
  reserve?: number
}) {
  const pinnedPref = useOutlinePrefs((s) => s.pinned)
  const setPinned = useOutlinePrefs((s) => s.setPinned)
  const [paneWidth, setPaneWidth] = useState(0)
  const [scrollerHeight, setScrollerHeight] = useState<number | null>(null)
  const pinned = outlinePinned(pinnedPref, paneWidth)
  const [active, setActive] = useState<string | null>(null)
  const [focusId, setFocusId] = useState<string | null>(null)
  const [hover, setHover] = useState(false)
  const [focusWithin, setFocusWithin] = useState(false)
  // Opened by the shortcut or a rail click; holds the panel open until focus leaves it.
  const [forced, setForced] = useState(false)
  const open = pinned || hover || focusWithin || forced

  const rootRef = useRef<HTMLElement>(null)
  const asideRef = useRef<HTMLDivElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const hoverTimer = useRef<number | undefined>(undefined)
  // After a jump the jumped-to heading stays lit until the reader scrolls: a
  // heading near the end can't reach the top, where the scroll-spy looks.
  const heldRef = useRef(false)
  const returnFocus = useRef<HTMLElement | null>(null)
  // A row click keeps focus for the arrow keys; it lets go once the overlay closes on pointer leave.
  const pointerFocus = useRef(false)
  const restoreRef = useRef<(() => void) | null>(null)
  const copy = useCopySectionLink(docRef)
  const ids = useMemo(() => items.map((i) => i.id), [items])
  const rovingId = (focusWithin ? focusId : null) ?? active ?? ids[0] ?? null

  useLayoutEffect(() => {
    // The wrapper's own ref isn't attached yet while this child's layout effect
    // runs; the aside's is.
    const wrap = rootRef.current?.parentElement
    const scroller = driver.scroller()
    if (!wrap || !scroller) return
    const measure = () => {
      setPaneWidth(wrap.clientWidth)
      setScrollerHeight(scroller.clientHeight)
    }
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(wrap)
    ro.observe(scroller)
    return () => ro.disconnect()
  }, [driver])

  useEffect(() => {
    let frame = 0
    const update = () => {
      frame = 0
      if (!heldRef.current) setActive(driver.activeId())
    }
    const onScroll = () => {
      if (!frame) frame = requestAnimationFrame(update)
    }
    onScroll()
    const unwatch = driver.watch(onScroll)
    return () => {
      unwatch()
      cancelAnimationFrame(frame)
    }
  }, [driver, items])

  useEffect(() => {
    const release = () => {
      heldRef.current = false
    }
    const releaseOutside = (e: Event) => {
      if (!asideRef.current?.contains(e.target as Node)) release()
    }
    window.addEventListener('wheel', release, { capture: true, passive: true })
    window.addEventListener('touchstart', release, { capture: true, passive: true })
    window.addEventListener('pointerdown', releaseOutside, { capture: true })
    window.addEventListener('keydown', releaseOutside, { capture: true })
    return () => {
      window.removeEventListener('wheel', release, { capture: true })
      window.removeEventListener('touchstart', release, { capture: true })
      window.removeEventListener('pointerdown', releaseOutside, { capture: true })
      window.removeEventListener('keydown', releaseOutside, { capture: true })
      window.clearTimeout(hoverTimer.current)
    }
  }, [])

  const jump = (id: string, flash: boolean) => {
    heldRef.current = true
    setActive(id)
    driver.jump(id, flash)
  }

  const focusRow = (id: string | null) => {
    if (id === null) return
    setFocusId(id)
    const row = panelRef.current?.querySelector<HTMLElement>(`[data-outline-id="${CSS.escape(id)}"]`)
    row?.focus({ preventScroll: true })
    row?.scrollIntoView?.({ block: 'nearest' })
  }

  const openFocused = (returnTo: HTMLElement | null) => {
    returnFocus.current = returnTo && returnTo !== document.body && document.contains(returnTo) ? returnTo : null
    pointerFocus.current = false
    flushSync(() => setForced(true))
    focusRow(active ?? ids[0] ?? null)
  }

  const focusHome = () => {
    if (driver.home) driver.home()
    else (document.activeElement as HTMLElement | null)?.blur()
  }

  const leave = () => {
    setForced(false)
    setHover(false)
    const back = returnFocus.current
    returnFocus.current = null
    if (back && document.contains(back)) back.focus({ preventScroll: true })
    else focusHome()
  }

  const openRef = useRef(openFocused)
  const shortcutRef = useRef(shortcut)
  useEffect(() => {
    openRef.current = openFocused
    shortcutRef.current = shortcut
  })
  const kind = shortcut.aria
  useEffect(() => {
    const me = Symbol('outline')
    const stack = mounted.get(kind) ?? []
    mounted.set(kind, stack)
    stack.push(me)
    const onKey = (e: KeyboardEvent) => {
      const sc = shortcutRef.current
      if (stack[stack.length - 1] !== me || e.defaultPrevented || !sc.matches(e)) return
      if ((!sc.whileTyping && isTyping(e.target)) || asideRef.current?.contains(e.target as Node)) return
      e.preventDefault()
      openRef.current(document.activeElement as HTMLElement | null)
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
      stack.splice(stack.indexOf(me), 1)
    }
  }, [kind])

  const onPanelKeyDown = (e: React.KeyboardEvent) => {
    if (e.ctrlKey || e.metaKey || e.altKey) return
    const current = (document.activeElement as HTMLElement | null)?.dataset.outlineId ?? focusId
    if (isOutlineKey(e.key)) {
      e.preventDefault()
      e.stopPropagation()
      const next = outlineStep(ids, current ?? null, e.key)
      if (next === null) return
      focusRow(next)
      jump(next, false)
    } else if (e.key === 'c' && current && !items.find((i) => i.id === current)?.icon) {
      e.preventDefault()
      e.stopPropagation()
      void copy(current)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      e.stopPropagation()
      leave()
    }
  }

  const togglePin = () => {
    restoreRef.current = driver.anchor()
    setPinned(!pinnedPref)
  }

  // Pinning re-wraps the text: keep the place being read where it was.
  useLayoutEffect(() => {
    const restore = restoreRef.current
    restoreRef.current = null
    restore?.()
  }, [pinned])

  const onEnter = () => {
    window.clearTimeout(hoverTimer.current)
    hoverTimer.current = window.setTimeout(() => setHover(true), OPEN_DELAY_MS)
  }
  const onLeave = () => {
    window.clearTimeout(hoverTimer.current)
    hoverTimer.current = window.setTimeout(() => {
      setHover(false)
      if (pointerFocus.current && !pinned && asideRef.current?.contains(document.activeElement)) dropMouseFocus()
    }, CLOSE_DELAY_MS)
  }
  // A mouse click mustn't leave focus in the panel, or it stays open after the
  // pointer leaves; hover alone holds it open then.
  const dropMouseFocus = () => {
    setForced(false)
    returnFocus.current = null
    pointerFocus.current = false
    focusHome()
  }

  const pinTitle = !pinnedPref
    ? 'Pin outline (keep it open beside the text)'
    : pinned
      ? 'Unpin outline'
      : 'Unpin outline (the pane is too narrow to keep it open)'
  const maxHeight = scrollerHeight ? Math.max(120, scrollerHeight - reserve) : undefined
  const railTitle = `Outline (${shortcut.label})`

  return (
    <aside ref={rootRef} className={'sticky top-2 z-20 self-start shrink-0 flex flex-col ' + (pinned ? 'w-56 ml-4' : 'w-6 ml-2')}>
      {header}
      <div
        ref={asideRef}
        onMouseEnter={onEnter}
        onMouseLeave={onLeave}
        onFocus={() => setFocusWithin(true)}
        onBlur={(e) => {
          if (asideRef.current?.contains(e.relatedTarget as Node | null)) return
          setFocusWithin(false)
          setForced(false)
        }}
        className="relative"
      >
        {!pinned && (
          <button
            type="button"
            onClick={() => openFocused(null)}
            title={railTitle}
            aria-label={railTitle}
            aria-keyshortcuts={shortcut.aria}
            aria-expanded={open}
            className="w-6 flex flex-col items-center gap-1 py-1 overflow-hidden rounded outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
            style={{ maxHeight }}
          >
            {items.map((it) => (
              <span
                key={it.id}
                aria-hidden
                className={
                  'block h-[3px] shrink-0 rounded-full ' +
                  (it.id === active ? 'bg-blue-500 dark:bg-blue-400' : 'bg-zinc-300 dark:bg-zinc-600')
                }
                style={{ width: it.depth === 2 ? 14 : 9 }}
              />
            ))}
          </button>
        )}
        <div
          ref={panelRef}
          role="navigation"
          aria-label="Outline"
          onKeyDown={onPanelKeyDown}
          style={{ maxHeight }}
          className={
            'flex flex-col ' +
            (pinned
              ? 'border-l border-zinc-200 dark:border-zinc-800'
              : 'absolute right-0 top-0 z-20 w-64 rounded-md border border-zinc-200 dark:border-zinc-700 ' +
                // `visibility` must not transition: mid-fade the panel still counts
                // as hidden and refuses the focus the shortcut moves into it.
                'bg-white dark:bg-zinc-950 shadow-lg transition-[opacity,transform] duration-100 ' +
                (open ? 'visible opacity-100 translate-x-0' : 'invisible opacity-0 translate-x-2 pointer-events-none'))
          }
        >
          <div className="flex items-center gap-1 pl-3 pr-1 py-1 shrink-0">
            <span className="flex-1 text-[11px] font-semibold uppercase tracking-wide text-zinc-500">Outline</span>
            <button
              type="button"
              onClick={(e) => {
                togglePin()
                if (e.detail > 0) dropMouseFocus()
              }}
              title={pinTitle}
              aria-label={pinTitle}
              aria-pressed={pinnedPref}
              className="p-1 rounded text-zinc-400 hover:text-zinc-700 hover:bg-zinc-100 dark:hover:text-zinc-200 dark:hover:bg-zinc-800"
            >
              {pinnedPref ? <PinOff className="h-3.5 w-3.5" /> : <Pin className="h-3.5 w-3.5" />}
            </button>
          </div>
          <div className="flex-1 min-h-0 overflow-y-auto">
            <OutlineRows
              items={items}
              docRef={docRef}
              activeHeadingId={active}
              focusId={rovingId}
              onSelect={(id, pointer) => {
                setFocusId(id)
                jump(id, true)
                if (!pointer) return
                // The editor takes the cursor back, so typing goes on at the heading.
                if (driver.home) dropMouseFocus()
                else {
                  // Safari doesn't focus a clicked button.
                  focusRow(id)
                  pointerFocus.current = true
                }
              }}
              indent={(d) => (d === 3 ? '1.75rem' : '0.75rem')}
            />
          </div>
          <div className="shrink-0 px-3 py-1 border-t border-zinc-100 dark:border-zinc-800 text-[10px] text-zinc-400">
            ↑↓ jump · c copy link · Esc {pinned ? 'leave' : 'close'}
          </div>
        </div>
      </div>
    </aside>
  )
}
