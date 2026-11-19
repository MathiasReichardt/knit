import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { parseCollapsed, resolveDrag, stepCollapsed, type Collapsed } from '../lib/splitLayout'

const MIN = 320
const OTHER_MIN = 320
const STORAGE_PREFIX = 'knit.split.'

interface Props {
  storageKey: string
  defaultWidth?: number
  /** Which pane the stored width sizes; the other one flexes. Dragging the
   * handle right widens the left pane and narrows the right one either way —
   * only the anchor changes. */
  resize?: 'left' | 'right'
  left: ReactNode
  right: ReactNode
}

export function ResizableSplit({ storageKey, defaultWidth = 560, resize = 'left', left, right }: Props) {
  const [width, setWidth] = useState<number>(() => {
    const raw = localStorage.getItem(STORAGE_PREFIX + storageKey)
    const parsed = raw ? Number(raw) : NaN
    return Number.isFinite(parsed) && parsed >= MIN ? parsed : defaultWidth
  })
  // Kept apart from the width so reopening a pane restores where it was.
  const [collapsed, setCollapsed] = useState<Collapsed>(() =>
    parseCollapsed(localStorage.getItem(STORAGE_PREFIX + storageKey + '.collapsed')),
  )
  const containerRef = useRef<HTMLDivElement | null>(null)
  const draggingRef = useRef(false)
  const [containerWidth, setContainerWidth] = useState(0)

  useEffect(() => {
    localStorage.setItem(STORAGE_PREFIX + storageKey, String(width))
  }, [storageKey, width])

  useEffect(() => {
    localStorage.setItem(STORAGE_PREFIX + storageKey + '.collapsed', collapsed)
  }, [storageKey, collapsed])

  // Track the container width so we can clamp the stored width when the window
  // shrinks — otherwise the fixed-width, shrink-0 pane can squeeze its sibling
  // to 0 with no horizontal scroller to recover it.
  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const ro = new ResizeObserver((entries) => {
      setContainerWidth(entries[0]!.contentRect.width)
    })
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '[' && e.key !== ']') return
      // `[` / `]` sit behind AltGr on e.g. German layouts, reported as Ctrl+Alt.
      const altGr = e.getModifierState('AltGraph')
      if (e.defaultPrevented || e.metaKey || (!altGr && (e.ctrlKey || e.altKey))) return
      const t = e.target as HTMLElement | null
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return
      e.preventDefault()
      setCollapsed((c) => stepCollapsed(c, e.key === '[' ? 'left' : 'right'))
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // The stored width is the user's preference; render it clamped so the flexing
  // pane always keeps at least OTHER_MIN. Widening the window restores it.
  const effectiveWidth =
    containerWidth > 0
      ? Math.max(MIN, Math.min(width, containerWidth - OTHER_MIN))
      : width

  const onMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    draggingRef.current = true
    const rect = containerRef.current?.getBoundingClientRect()
    const containerLeft = rect?.left ?? 0
    const containerWidth = rect?.width ?? window.innerWidth

    const onMove = (ev: MouseEvent) => {
      if (!draggingRef.current) return
      const next = resolveDrag(ev.clientX - containerLeft, containerWidth, MIN)
      setCollapsed(next.collapsed)
      // A snapped-shut drag keeps the last open width for the reopen.
      if (next.collapsed === 'none')
        setWidth(resize === 'left' ? next.leftWidth : containerWidth - next.leftWidth)
    }
    const onUp = () => {
      draggingRef.current = false
      window.removeEventListener('mousemove', onMove)
      window.removeEventListener('mouseup', onUp)
      document.body.style.userSelect = ''
      document.body.style.cursor = ''
    }
    document.body.style.userSelect = 'none'
    document.body.style.cursor = 'col-resize'
    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onUp)
  }, [resize])

  const onDoubleClick = useCallback(() => {
    setCollapsed((c) => (c === 'none' ? 'right' : 'none'))
  }, [])

  const step = (dir: 'left' | 'right') => setCollapsed((c) => stepCollapsed(c, dir))

  const fixed = collapsed === 'none' ? { width: effectiveWidth } : undefined
  const sized = 'min-h-0 flex flex-col ' + (fixed ? 'shrink-0' : 'flex-1 min-w-0')
  const flexing = 'flex-1 min-w-0 min-h-0'
  return (
    <div ref={containerRef} className="flex h-full min-h-0 w-full">
      <div
        style={resize === 'left' ? fixed : undefined}
        className={
          (resize === 'left' ? sized : flexing) +
          ' border-r border-zinc-200 dark:border-zinc-800' +
          (collapsed === 'left' ? ' hidden' : '')
        }
      >
        {left}
      </div>
      {collapsed === 'none' ? (
        <div
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize panels"
          onMouseDown={onMouseDown}
          onDoubleClick={onDoubleClick}
          className="group relative w-1 cursor-col-resize bg-transparent hover:bg-blue-400/40 active:bg-blue-500/60 shrink-0"
        >
          <div className="absolute top-1/2 left-1/2 z-10 -translate-x-1/2 -translate-y-1/2 flex flex-col rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 opacity-40 group-hover:opacity-100 focus-within:opacity-100">
            <RailButton dir="left" label="Hide left panel" onClick={() => step('left')} />
            <RailButton dir="right" label="Hide right panel" onClick={() => step('right')} />
          </div>
        </div>
      ) : (
        <div
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize panels"
          onMouseDown={onMouseDown}
          onDoubleClick={onDoubleClick}
          className={
            'w-5 shrink-0 flex items-center justify-center cursor-col-resize bg-zinc-50 dark:bg-zinc-900 hover:bg-blue-400/20 border-zinc-200 dark:border-zinc-800' +
            (collapsed === 'left' ? ' border-r' : '')
          }
        >
          {collapsed === 'left' ? (
            <RailButton dir="right" label="Show left panel" onClick={() => step('right')} />
          ) : (
            <RailButton dir="left" label="Show right panel" onClick={() => step('left')} />
          )}
        </div>
      )}
      <div
        style={resize === 'right' ? fixed : undefined}
        className={(resize === 'right' ? sized : flexing) + (collapsed === 'right' ? ' hidden' : '')}
      >
        {right}
      </div>
    </div>
  )
}

function RailButton({ dir, label, onClick }: { dir: 'left' | 'right'; label: string; onClick: () => void }) {
  const Icon = dir === 'left' ? ChevronLeft : ChevronRight
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      // Keep a click on the chevron from starting a drag or a double-click toggle.
      onMouseDown={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
      className="p-0.5 text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100 cursor-pointer"
    >
      <Icon size={12} />
    </button>
  )
}
