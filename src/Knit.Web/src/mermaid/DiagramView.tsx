import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ArrowUpRight, CircleQuestionMark, Crosshair, Maximize2, Minimize2, Scan, ZoomIn, ZoomOut } from 'lucide-react'
import { ExportMenu, type ExportModel } from './ExportMenu'
import { addEdgeHitAreas, pickTarget } from './hitArea'
import { LayoutMenu, type LayoutModel } from './LayoutMenu'
import { unwrapLinks, type DiagramLink } from './links'
import { diagramName, formatPointer, nodeElFor, resolveHit, type Hit, type Pick } from './pointer'
import { Popover } from './Popover'
import {
  createGesture,
  FIT,
  fitView,
  MAX_SCALE,
  MIN_SCALE,
  transformOf,
  wheelFactor,
  ZOOM_STEP,
  zoomAt,
  type View,
} from './panZoom'
import './mermaid.css'

export type MermaidTheme = 'light' | 'dark'

export interface Rendered {
  svg: string
  diagramType: string
}

/** How a click names an element: mermaid's pointers unless a host brings its own (an SVG file). */
export interface Pointers {
  resolve: (target: Element, svg: SVGSVGElement) => Hit | undefined
  /** The pointer to `pick`, or to the whole diagram without one. */
  format: (pick?: Pick) => string
  /** The whole diagram, for the copy feedback. */
  name: string
}

const PICK_COLOR: Record<MermaidTheme, string> = { light: '#2563eb', dark: '#60a5fa' }
const FLASH_MS = 700

function flash(el: Element) {
  el.classList.remove('mermaid-flash')
  // Force a reflow so a repeat click restarts the animation.
  void el.getBoundingClientRect()
  el.classList.add('mermaid-flash')
  window.setTimeout(() => el.classList.remove('mermaid-flash'), FLASH_MS)
}

const BUTTON =
  'inline-flex h-6 w-6 items-center justify-center rounded text-zinc-600 dark:text-zinc-300 ' +
  'hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-40 disabled:hover:bg-transparent'

function parseSvg(html: string): SVGSVGElement {
  const holder = document.createElement('div')
  holder.innerHTML = html
  const svg = holder.querySelector('svg')!
  // A node's link is the badge's job; a click on the node stays a pick.
  unwrapLinks(svg)
  // Block, so the stage is exactly the diagram's height (no inline baseline gap).
  svg.style.display = 'block'
  return svg
}

/**
 * A rendered diagram with pan/zoom and, given `docRef` + `onPick`, click-to-pointer.
 * A background click maximizes; in the overlay it does nothing, so exploring never
 * closes it — only Esc or the Close button do.
 * Maximizing moves the one live SVG node into a viewport-filling overlay and back,
 * so element ids stay unique and picks resolve the same either way.
 * A linked node (`click` / `link` in the fence) gets a ↗ badge that follows the link.
 */
export function DiagramView({
  rendered,
  theme,
  docRef,
  fence,
  onPick,
  layout,
  exportImage,
  links,
  canFollow,
  onFollow,
  onCopy,
  pointers,
}: {
  rendered: Rendered
  theme: MermaidTheme
  docRef?: string
  fence: number
  onPick?: (pointer: string, name: string) => void
  /** Without it, or with no options for the diagram type, there's no layout menu. */
  layout?: LayoutModel
  /** Without it, there's no export button. */
  exportImage?: ExportModel
  /** The fence's link lines; each linked node gets a badge. */
  links?: DiagramLink[]
  /** Whether the host can open a link's target; keep it stable. */
  canFollow?: (target: string) => boolean
  /** Opens a link's target. */
  onFollow?: (target: string) => void
  /** Copies a target the host can't open, for the reader to go there by hand. */
  onCopy?: (target: string) => void
  pointers?: Pointers
}) {
  const pickable = !!docRef && !!onPick
  const svg = useMemo(() => {
    const el = parseSvg(rendered.svg)
    if (pickable) addEdgeHitAreas(el)
    return el
  }, [rendered.svg, pickable])
  const [maximized, setMaximized] = useState(false)
  const [inlineHeight, setInlineHeight] = useState<number | null>(null)
  const inlineRef = useRef<HTMLDivElement>(null)
  const maximizeRef = useRef<HTMLButtonElement>(null)
  const restoreFocus = useRef(false)

  useEffect(() => {
    if (maximized || !restoreFocus.current) return
    restoreFocus.current = false
    maximizeRef.current?.focus()
  }, [maximized])

  const open = () => {
    setInlineHeight(inlineRef.current?.offsetHeight ?? null)
    setMaximized(true)
  }
  const close = useCallback(() => {
    restoreFocus.current = true
    setMaximized(false)
    setInlineHeight(null)
  }, [])

  const copyPointer = (hit: Hit | undefined) => {
    if (!docRef || !onPick) return
    flash(hit?.el ?? svg)
    const name = hit
      ? (hit.pick.label ?? hit.pick.dslId ?? hit.pick.kind)
      : (pointers?.name ?? `${diagramName(rendered.diagramType)} diagram`)
    onPick(pointers ? pointers.format(hit?.pick) : formatPointer(docRef, fence, rendered.diagramType, hit?.pick), name)
  }
  const resolve = (target: Element) =>
    !svg.contains(target)
      ? undefined
      : pointers
        ? pointers.resolve(target, svg)
        : resolveHit(target, svg, rendered.diagramType)
  const pick = (target: Element) => {
    const hit = resolve(target)
    if (hit) copyPointer(hit)
    return !!hit
  }
  const hoverTarget = (target: Element) => resolve(target)?.el ?? null

  const pane = {
    svg,
    theme,
    pick: pickable ? pick : undefined,
    hoverTarget: pickable ? hoverTarget : undefined,
    layout: layout?.options.length ? layout : undefined,
    exportImage,
    copyDiagram: pickable ? () => copyPointer(undefined) : undefined,
    diagramType: rendered.diagramType,
    links,
    canFollow,
    onCopy,
    onFollow: onFollow && ((target: string) => {
      if (maximized) close()
      onFollow(target)
    }),
  }

  return (
    <div
      ref={inlineRef}
      className={
        'group/diagram relative not-prose my-3 rounded outline outline-1 outline-offset-2 outline-transparent ' +
        'transition-[outline-color] hover:outline-zinc-300 dark:hover:outline-zinc-700'
      }
      style={inlineHeight === null ? undefined : { height: inlineHeight }}
      // Hosts open the body editor on double-click; inside a diagram that's two picks or zooms.
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Pane
        {...pane}
        hosted={!maximized}
        onBackground={open}
        extraButton={
          <button
            ref={maximizeRef}
            type="button"
            className={BUTTON}
            title="Maximize"
            aria-label="Maximize"
            onClick={open}
          >
            <Maximize2 className="h-3.5 w-3.5" />
          </button>
        }
      />
      {maximized && <Overlay pane={pane} onClose={close} />}
    </div>
  )
}

function Overlay({
  pane,
  onClose,
}: {
  pane: Omit<PaneProps, 'hosted' | 'extraButton' | 'fill' | 'onBackground'>
  onClose: () => void
}) {
  const closeRef = useRef<HTMLButtonElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    closeRef.current?.focus()
    // Capture phase: Esc closes the overlay only, not whatever else listens for it.
    // An open popover takes the Esc first.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || dialogRef.current?.querySelector('[data-diagram-popover]')) return
      e.preventDefault()
      e.stopPropagation()
      onClose()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [onClose])

  // Below the toasts (z-50), so pick feedback stays visible.
  return createPortal(
    <div
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Diagram"
      className="group/diagram fixed inset-0 z-40 bg-white dark:bg-zinc-950 p-4"
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Pane
        {...pane}
        hosted
        fill
        extraButton={
          <button
            ref={closeRef}
            type="button"
            className={BUTTON.replace('w-6', 'gap-1 px-1.5 text-xs')}
            title="Close (Esc)"
            onClick={onClose}
          >
            <Minimize2 className="h-3.5 w-3.5" aria-hidden />
            Close
          </button>
        }
      />
    </div>,
    document.body,
  )
}

interface PaneProps {
  svg: SVGSVGElement
  theme: MermaidTheme
  /** Whether the SVG lives in this pane now. */
  hosted: boolean
  /** Overlay: fit the diagram to the pane, and a plain wheel pans. */
  fill?: boolean
  /** Copies the element under `target`'s pointer; false when there's none. */
  pick?: (target: Element) => boolean
  /** A click that hit no element (and no `click` link). */
  onBackground?: () => void
  hoverTarget?: (target: Element) => Element | null
  layout?: LayoutModel
  exportImage?: ExportModel
  /** Copies the pointer to the whole diagram. */
  copyDiagram?: () => void
  diagramType: string
  links?: DiagramLink[]
  canFollow?: (target: string) => boolean
  onFollow?: (target: string) => void
  onCopy?: (target: string) => void
  extraButton: ReactNode
}

const BADGE_HALF_PX = 8

interface Badge extends DiagramLink {
  /** The node's top-right corner, in unscaled stage coordinates. */
  x: number
  y: number
  /** The stage's width: a badge on a node at the diagram's top or right edge is pulled inside it. */
  stageWidth: number
}

function Pane({
  svg,
  theme,
  hosted,
  fill = false,
  pick,
  onBackground,
  hoverTarget,
  layout,
  exportImage,
  copyDiagram,
  diagramType,
  links,
  canFollow,
  onFollow,
  onCopy,
  extraButton,
}: PaneProps) {
  const viewportRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<HTMLDivElement>(null)
  const hoverRef = useRef<Element | null>(null)
  const [gesture] = useState(createGesture)
  const [view, setView] = useState<View>(FIT)
  const [fit, setFit] = useState<View>(FIT)
  const [panning, setPanning] = useState(false)
  const [popover, setPopover] = useState<'layout' | 'export' | 'help' | null>(null)
  // Stable: a popover re-runs its open effect (and refocuses) when this changes.
  const onPopover = useMemo(() => {
    const toggle = (which: 'layout' | 'export' | 'help') => (open: boolean) =>
      setPopover((p) => (open ? which : p === which ? null : p))
    return { layout: toggle('layout'), export: toggle('export'), help: toggle('help') }
  }, [])

  const setHover = (el: Element | null) => {
    if (hoverRef.current === el) return
    hoverRef.current?.classList.remove('mermaid-hover')
    el?.classList.add('mermaid-hover')
    // On the viewport: the cursor sits on an edge's hit copy or a label, not on `el`.
    viewportRef.current?.classList.toggle('mermaid-hovering', !!el)
    hoverRef.current = el
  }

  const measureFit = () => {
    const vp = viewportRef.current
    const stage = stageRef.current
    if (!fill || !vp || !stage) return FIT
    // Untransformed size: the stage's layout box ignores its own transform.
    return fitView(vp.clientWidth, vp.clientHeight, svg.clientWidth || stage.offsetWidth, stage.offsetHeight)
  }

  useLayoutEffect(() => {
    const stage = stageRef.current
    const vp = viewportRef.current
    if (!hosted || !stage) return
    stage.appendChild(svg)
    const f = measureFit()
    /* eslint-disable react-hooks/set-state-in-effect -- fit needs the hosted SVG's size */
    setFit(f)
    if (fill) setView(f)
    /* eslint-enable react-hooks/set-state-in-effect */
    return () => {
      hoverRef.current?.classList.remove('mermaid-hover')
      vp?.classList.remove('mermaid-hovering')
      hoverRef.current = null
      // A re-render brings a new SVG; the next pane to host this one appends it itself.
      if (svg.parentNode === stage) svg.remove()
    }
    // measureFit reads refs only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [svg, hosted, fill])

  // Native listener: React's wheel handler is passive and can't stop the browser's page zoom.
  useEffect(() => {
    const vp = viewportRef.current
    if (!vp) return
    const onWheel = (e: WheelEvent) => {
      if (e.ctrlKey || e.metaKey) {
        e.preventDefault()
        const r = vp.getBoundingClientRect()
        const factor = wheelFactor(e.deltaY, e.deltaMode)
        setView((v) => zoomAt(v, factor, e.clientX - r.left, e.clientY - r.top))
      } else if (fill) {
        e.preventDefault()
        setView((v) => ({ ...v, x: v.x - e.deltaX, y: v.y - e.deltaY }))
      }
    }
    vp.addEventListener('wheel', onWheel, { passive: false })
    return () => vp.removeEventListener('wheel', onWheel)
  }, [fill])

  // Measured once the SVG is hosted; pan / zoom then only move the badges.
  const [badges, setBadges] = useState<Badge[]>([])
  useLayoutEffect(() => {
    const stage = stageRef.current
    if (!hosted || !stage || !links?.length) {
      setBadges([])
      return
    }
    const s = stage.getBoundingClientRect()
    // The stage's current zoom: its layout box ignores its own transform.
    const scale = stage.offsetWidth ? s.width / stage.offsetWidth : 1
    const seen = new Set<string>()
    const next: Badge[] = []
    for (const link of links) {
      if (seen.has(link.id)) continue
      const el = nodeElFor(svg, diagramType, link.id)
      if (!el) continue
      seen.add(link.id)
      const r = el.getBoundingClientRect()
      next.push({ ...link, x: (r.right - s.left) / scale, y: (r.top - s.top) / scale, stageWidth: stage.offsetWidth })
    }
    setBadges(next)
  }, [svg, hosted, links, diagramType])

  const zoomCentered = (factor: number) => {
    const vp = viewportRef.current
    if (vp) setView((v) => zoomAt(v, factor, vp.clientWidth / 2, vp.clientHeight / 2))
  }

  const onPointerDown = (e: React.PointerEvent) => {
    if (e.button === 0) gesture.down(e.clientX, e.clientY)
  }

  const onPointerMove = (e: React.PointerEvent) => {
    const d = gesture.move(e.clientX, e.clientY)
    if (!d) return
    if (!panning) {
      // Capture only once it's a drag: capturing the press would retarget its click.
      viewportRef.current?.setPointerCapture(e.pointerId)
      setHover(null)
      setPanning(true)
    }
    setView((v) => ({ ...v, x: v.x + d.dx, y: v.y + d.dy }))
  }

  const onPointerUp = () => {
    gesture.up()
    setPanning(false)
  }

  // Capture phase: the click ending a drag reaches neither a pick nor a `click` link.
  const onClickCapture = (e: React.MouseEvent) => {
    if (gesture.click()) return
    e.preventDefault()
    e.stopPropagation()
  }

  const onMouseOver = (e: React.MouseEvent) => {
    if (!hoverTarget || gesture.dragging) return
    setHover(hoverTarget(pickTarget(e.target as Element)))
  }

  const onClick = (e: React.MouseEvent) => {
    if (!hosted) return
    const target = pickTarget(e.target as Element)
    if (pick?.(target)) return
    onBackground?.()
  }

  const fitted = view.x === fit.x && view.y === fit.y && view.scale === fit.scale

  return (
    <>
      <div
        ref={viewportRef}
        data-diagram-viewport
        className={
          (pick ? 'mermaid-pickable ' : '') +
          (fill ? 'h-full ' : '') +
          'relative overflow-hidden select-none ' +
          (panning ? 'cursor-grabbing' : onBackground ? 'cursor-zoom-in' : 'cursor-grab')
        }
        style={{ '--mermaid-pick': PICK_COLOR[theme] } as React.CSSProperties}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onDragStart={(e) => e.preventDefault()}
        onClickCapture={onClickCapture}
        onMouseOver={onMouseOver}
        onMouseLeave={() => setHover(null)}
        onClick={onClick}
      >
        <div ref={stageRef} className="origin-top-left" style={{ transform: transformOf(view) }} />
        {badges.map((b) => {
          const followable = !!onFollow && !!canFollow?.(b.target)
          const label = followable
            ? `Go to ${b.target}`
            : onCopy
              ? `Copy "${b.target}": not a Knit ref, open it yourself`
              : `Can't follow "${b.target}": not a Knit ref`
          return (
            <button
              key={b.id}
              type="button"
              data-diagram-link={b.id}
              className={
                'absolute -translate-x-1/2 -translate-y-1/2 inline-flex h-4 w-4 items-center justify-center rounded-full ' +
                'border shadow-sm ' +
                (followable
                  ? 'border-blue-300 bg-white text-blue-600 hover:bg-blue-50 dark:border-blue-700 dark:bg-zinc-900 ' +
                    'dark:text-blue-300 dark:hover:bg-blue-950 cursor-pointer'
                  : 'border-zinc-300 bg-white text-zinc-400 hover:text-zinc-600 dark:border-zinc-600 dark:bg-zinc-900 ' +
                    (onCopy ? 'cursor-copy' : 'cursor-help'))
              }
              style={{
                left: view.x + Math.min(b.x * view.scale, b.stageWidth * view.scale - BADGE_HALF_PX),
                top: view.y + Math.max(b.y * view.scale, BADGE_HALF_PX),
              }}
              title={label}
              aria-label={label}
              aria-disabled={!followable && !onCopy}
              onPointerDown={(e) => e.stopPropagation()}
              onClick={(e) => {
                e.stopPropagation()
                if (followable) onFollow(b.target)
                else onCopy?.(b.target)
              }}
            >
              <ArrowUpRight className="h-3 w-3" aria-hidden />
            </button>
          )
        })}
      </div>
      <div
        className={
          'absolute flex gap-0.5 rounded border border-zinc-200 dark:border-zinc-700 ' +
          'bg-white/90 dark:bg-zinc-900/90 p-0.5 transition-opacity ' +
          // Top: a layout change resizes the diagram, and the buttons stay put.
          (fill ? 'top-5 right-5 ' : 'top-1 right-1 ') +
          (fitted && !fill && !popover ? 'opacity-0 group-hover/diagram:opacity-100 focus-within:opacity-100' : '')
        }
      >
        {copyDiagram && (
          <button
            type="button"
            className={BUTTON}
            title="Copy pointer to the whole diagram"
            aria-label="Copy diagram pointer"
            onClick={copyDiagram}
          >
            <Crosshair className="h-3.5 w-3.5" />
          </button>
        )}
        <button
          type="button"
          className={BUTTON}
          title="Zoom in (or Ctrl+wheel)"
          aria-label="Zoom in"
          disabled={view.scale >= MAX_SCALE}
          onClick={() => zoomCentered(ZOOM_STEP)}
        >
          <ZoomIn className="h-3.5 w-3.5" />
        </button>
        <button
          type="button"
          className={BUTTON}
          title="Zoom out (or Ctrl+wheel)"
          aria-label="Zoom out"
          disabled={view.scale <= MIN_SCALE}
          onClick={() => zoomCentered(1 / ZOOM_STEP)}
        >
          <ZoomOut className="h-3.5 w-3.5" />
        </button>
        <button
          type="button"
          className={BUTTON}
          title="Reset to fit"
          aria-label="Reset to fit"
          disabled={fitted}
          onClick={() => setView(measureFit())}
        >
          <Scan className="h-3.5 w-3.5" />
        </button>
        {layout && (
          <LayoutMenu
            layout={layout}
            open={popover === 'layout'}
            onOpenChange={onPopover.layout}
            buttonClassName={BUTTON}
          />
        )}
        {exportImage && (
          <ExportMenu
            model={exportImage}
            theme={theme}
            open={popover === 'export'}
            onOpenChange={onPopover.export}
            buttonClassName={BUTTON}
          />
        )}
        <Popover
          label="Diagram help"
          icon={<CircleQuestionMark className="h-3.5 w-3.5" />}
          open={popover === 'help'}
          onOpenChange={onPopover.help}
          buttonClassName={BUTTON}
          width="w-60"
        >
          <ul className="list-disc space-y-0.5 pl-4">
            {pick && <li>Click an element to copy a pointer to it</li>}
            {badges.length > 0 && <li>Click a ↗ badge to follow the node's link</li>}
            {onBackground && <li>Click the background to maximize</li>}
            <li>{fill ? 'Drag or wheel to pan' : 'Drag to pan'}</li>
            <li>Ctrl+wheel to zoom</li>
          </ul>
        </Popover>
        {extraButton}
      </div>
    </>
  )
}
