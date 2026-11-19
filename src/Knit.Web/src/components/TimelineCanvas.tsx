import { useCallback, useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { Ban, Flag, TriangleAlert } from 'lucide-react'
import type { TimelineEdge, TimelineMilestone, TimelineNode } from '../api/types'
import {
  assignLanes,
  dayNumber,
  depChains,
  milestoneFlagSpans,
  milestonesInWindow,
  segmentTicks,
  type LaneSpan,
  type SegmentUnit,
} from '../lib/timeline'
import { useElementWidth } from '../hooks/useElementWidth'
import { dueStatus } from '../lib/dueStatus'
import { STATE_ICON, STATE_LABEL, TYPE_COLOR, TYPE_ICON, TYPE_LABEL } from '../lib/glyphs'
import { priorityMarkerFill, priorityOf } from '../lib/priority'

const PAD_LEFT = 24
const PAD_RIGHT = 48
const AXIS_H = 30
const LANE_H = 40
const R = 10
// One stacked row of milestone flags (icon + slug label).
const FLAG_ROW_H = 15
// Width of the colored type icon drawn left of the title. Keep in step with
// the title x-offset below.
const TYPE_ICON_W = 16
// Horizontal room reserved per marker for its title label when packing lanes.
// Sized for TITLE_MAX chars at text-base (~8.6 px/char) plus the type icon —
// keep the three in step.
const LABEL_W = 300
const TITLE_MAX = 34
// Parent-breadcrumb cap (chars). Sized under LABEL_W at the ~11px crumb font
// so the dim ancestor line never out-runs the reserved title width.
const CRUMB_MAX = 40

// The scale only ever *widens* the canvas: a window narrower than the
// container is stretched to fill it, a wider one keeps this floor and scrolls
// rather than compressing to illegibility.
const MIN_PX_PER_DAY = 2.6
// First-paint container width, before the ResizeObserver has measured.
const ASSUMED_W = 960

interface Props {
  nodes: TimelineNode[]
  edges: TimelineEdge[]
  axisFrom: string
  axisTo: string
  /** Segment granularity of the (already snapped) axis window. */
  unit: SegmentUnit
  today: string
  dueSoonDays: number
  selectedId: string | null
  onSelect: (id: string | null) => void
  colorByPriority: boolean
  /** Dated milestones for the overlay; empty when the toggle is off. */
  milestones: TimelineMilestone[]
  /** Selected milestone ref (`project/slug`), or null. */
  selectedMilestone: string | null
  onSelectMilestone: (ref: string | null) => void
}

export function TimelineCanvas({
  nodes, edges, axisFrom, axisTo, unit, today, dueSoonDays, selectedId, onSelect, colorByPriority,
  milestones, selectedMilestone, onSelectMilestone,
}: Props) {
  const scrollRef = useRef<HTMLDivElement>(null)

  // Scale from the container, not from the span. Observing the
  // scroller is safe — its width comes from the flex parent and a wider SVG
  // only creates scroll, so there is no size-feedback loop.
  const containerW = useElementWidth(scrollRef, ASSUMED_W)
  const fromDay = dayNumber(axisFrom)
  const spanDays = Math.max(1, dayNumber(axisTo) - fromDay)
  const avail = Math.max(0, Math.floor(containerW) - PAD_LEFT - PAD_RIGHT)
  const pxPerDay = Math.max(MIN_PX_PER_DAY, avail / spanDays)
  const x = useCallback(
    (iso: string) => PAD_LEFT + (dayNumber(iso) - fromDay) * pxPerDay,
    [fromDay, pxPerDay],
  )
  const width = Math.floor(x(axisTo) + PAD_RIGHT)

  // A changed scale re-maps dates to pixels, so scrollLeft would silently
  // point at a different day. Keep the day at the left edge put.
  const leftEdgeDay = useRef(fromDay)
  const lastScale = useRef(pxPerDay)
  const lastFrom = useRef(fromDay)
  const onScroll = useCallback(() => {
    const el = scrollRef.current
    if (el) leftEdgeDay.current = fromDay + el.scrollLeft / pxPerDay
  }, [fromDay, pxPerDay])
  useLayoutEffect(() => {
    const scaleChanged = lastScale.current !== pxPerDay
    const fromChanged = lastFrom.current !== fromDay
    lastScale.current = pxPerDay
    lastFrom.current = fromDay
    const el = scrollRef.current
    if (!el) return
    // A new window is a new anchor, not a rescale of the old one.
    if (fromChanged) leftEdgeDay.current = fromDay + el.scrollLeft / pxPerDay
    else if (scaleChanged && el.scrollLeft > 0) {
      el.scrollLeft = Math.max(0, (leftEdgeDay.current - fromDay) * pxPerDay)
    }
  }, [pxPerDay, fromDay])

  const { lanes, laneCount } = useMemo(() => {
    const spans: LaneSpan[] = nodes.map((n) => {
      const markerX = x(n.spanEnd!)
      const startX = Math.min(x(n.spanStart!), markerX - R)
      return { id: n.id, start: startX - 4, end: markerX + R + LABEL_W }
    })
    return assignLanes(spans)
  }, [nodes, x])

  // Only milestones whose target lands inside the drawn axis get a guide.
  const visibleMilestones = useMemo(
    () => milestonesInWindow(milestones, axisFrom, axisTo),
    [milestones, axisFrom, axisTo],
  )

  // Flags are packed into rows by their real label extent, not by index
  // parity: two milestones sharing a target sit at the same x, so one of them
  // has to move down, and three of them need three rows.
  const { lanes: flagRow, laneCount: flagRowCount } = useMemo(
    () => assignLanes(milestoneFlagSpans(visibleMilestones, x)),
    [visibleMilestones, x],
  )

  // The flags own a band of their own between the axis and the first lane —
  // as tall as they actually stack. Overflowing it would lay a milestone label
  // over the item rows, where it is neither readable nor clickable.
  const headerH = AXIS_H + (visibleMilestones.length > 0 ? 6 + flagRowCount * FLAG_ROW_H + 6 : 12)
  const height = headerH + laneCount * LANE_H + 8
  const yOf = (id: string) => headerH + (lanes.get(id) ?? 0) * LANE_H + LANE_H / 2

  const chains = useMemo(
    () => (selectedId ? depChains(edges, selectedId) : null),
    [edges, selectedId],
  )
  const byId = useMemo(() => new Map(nodes.map((n) => [n.id, n])), [nodes])

  // Rows sort by date, so a parent and its children land arbitrarily far apart
  // and an always-on connector would be a hairball. Scope the relation to the
  // selection instead: it is drawn exactly when it is asked for.
  const subtree = useMemo(() => {
    if (!selectedId) return null
    const ids = nodes.filter((n) => n.ancestorIds.includes(selectedId)).map((n) => n.id)
    return ids.length > 0 ? new Set(ids) : null
  }, [nodes, selectedId])

  // Dimming the whole canvas to highlight one row is worse than doing nothing,
  // so a leaf with no chain and no subtree highlights nothing.
  const hasChain = !!chains && (chains.upstream.size > 0 || chains.downstream.size > 0)
  const focused = hasChain || subtree !== null
  const related = (id: string) =>
    !focused ||
    id === selectedId ||
    (chains?.upstream.has(id) ?? false) ||
    (chains?.downstream.has(id) ?? false) ||
    (subtree?.has(id) ?? false)

  // Scroll the selected marker into view when it sits outside the visible
  // range. The panel no longer overlays the canvas (it shrinks it), so this is
  // only about the horizontal scroll a wide window still has.
  const lastScrolledFor = useRef<string | null>(null)
  useEffect(() => {
    const el = scrollRef.current
    if (!el || !selectedId) return
    if (lastScrolledFor.current === selectedId) return
    lastScrolledFor.current = selectedId
    const sel = byId.get(selectedId)
    if (!sel?.spanEnd) return
    const markerX = x(sel.spanEnd)
    if (markerX > el.scrollLeft + el.clientWidth - 24) {
      el.scrollTo({ left: markerX - el.clientWidth + 60, behavior: 'smooth' })
    } else if (markerX < el.scrollLeft + PAD_LEFT) {
      el.scrollTo({ left: Math.max(0, markerX - 60), behavior: 'smooth' })
    }
  }, [selectedId, byId, x])

  // A band behind the selected item — its own extent only, never the full row:
  // lanes are packed, so several unrelated items share one row and a full-width
  // band would claim them too. Finding a selection that has scrolled out of view
  // is the axis tick's job, not this one's.
  const selBand = useMemo(() => {
    const n = selectedId ? byId.get(selectedId) : null
    if (!n?.spanEnd) return null
    const markerX = x(n.spanEnd)
    const startX = Math.min(n.spanStart ? x(n.spanStart) : markerX, markerX - R) - 6
    const chars = Math.min(n.title.length, TITLE_MAX)
    const labelW = TYPE_ICON_W + 2 + chars * 8.6
    const endX = markerX + R + 5 + labelW + 8
    const top = headerH + (lanes.get(n.id) ?? 0) * LANE_H
    return { x: startX, w: Math.max(0, endX - startX), y: top + 3, h: LANE_H - 6 }
  }, [selectedId, byId, x, lanes, headerH])

  const ticks = useMemo(() => segmentTicks(axisFrom, axisTo, unit), [axisFrom, axisTo, unit])
  // Due date of the selected item, surfaced on the axis. Anchored to
  // the axis rather than the marker, so it stays readable once the item's
  // lane row has scrolled away.
  const selectedDue = useMemo(() => {
    const sel = selectedId ? byId.get(selectedId) : null
    const due = sel?.spanEnd
    if (!due) return null
    if (dayNumber(due) < dayNumber(axisFrom) || dayNumber(due) > dayNumber(axisTo)) return null
    return due
  }, [selectedId, byId, axisFrom, axisTo])
  const todayX = x(today)
  const warnEndX = todayX + Math.max(0, dueSoonDays) * pxPerDay

  // Milestone linkage on the canvas: the Flag badge on linked bars
  // rides with the overlay, so it only shows when the overlay is on.
  const showMilestoneBadge = milestones.length > 0
  const selMs = useMemo(() => {
    if (!selectedMilestone) return null
    const inWindow = visibleMilestones.some((m) => `${m.project}/${m.slug}` === selectedMilestone)
    return inWindow ? milestones.find((m) => `${m.project}/${m.slug}` === selectedMilestone) ?? null : null
  }, [selectedMilestone, visibleMilestones, milestones])

  return (
    <div ref={scrollRef} className="h-full overflow-auto" onScroll={onScroll}>
      <svg
        width={width}
        height={height}
        className="block select-none"
        onClick={() => {
          onSelect(null)
          onSelectMilestone(null)
        }}
        role="img"
        aria-label="Deadline timeline"
      >
        {/* due-soon warning zone [today → today + due_soon_days] */}
        <rect
          x={todayX}
          y={AXIS_H}
          width={Math.max(0, warnEndX - todayX)}
          height={height - AXIS_H}
          className="fill-amber-400/10 dark:fill-amber-500/10"
        />

        {/* backdrop box behind the selected item — its own extent only, never
            the full row: lanes are packed, so several unrelated items share one
            row. Drawn under the grid so the today line and the segment rules
            still read across it; finding a selection that has scrolled away is
            the axis tick's job, not this box's. */}
        {selBand && (
          <rect
            x={selBand.x} y={selBand.y} width={selBand.w} height={selBand.h} rx={6}
            className="fill-blue-100 stroke-blue-400 dark:fill-blue-950 dark:stroke-blue-700"
            strokeWidth={1}
          />
        )}

        {ticks.map((t) => {
          const tx = x(t.iso)
          return (
            <g key={t.iso}>
              <line
                x1={tx} y1={AXIS_H} x2={tx} y2={height}
                className={t.major ? 'stroke-zinc-200 dark:stroke-zinc-800' : 'stroke-zinc-100 dark:stroke-zinc-900'}
              />
              <text
                x={tx + 3} y={AXIS_H - 8}
                className={
                  t.major
                    ? 'fill-zinc-500 dark:fill-zinc-400 text-xs font-medium'
                    : 'fill-zinc-400 dark:fill-zinc-500 text-xs'
                }
              >
                {t.label}
              </text>
            </g>
          )
        })}

        {/* today line, pinned above the grid */}
        <line x1={todayX} y1={AXIS_H - 4} x2={todayX} y2={height} className="stroke-sky-500" strokeWidth={1.5} />
        <text x={todayX + 4} y={AXIS_H + 10} className="fill-sky-600 dark:fill-sky-400 text-xs font-medium">
          Today
        </text>

        {/* selected item's due date: a tick at the due position labeled with the
            exact date, above the segment labels */}
        {selectedDue && (
          <g className="pointer-events-none">
            <line
              x1={x(selectedDue)} y1={AXIS_H - 17} x2={x(selectedDue)} y2={AXIS_H}
              className="stroke-blue-500"
              strokeWidth={2}
            />
            <text
              x={x(selectedDue)}
              y={AXIS_H - 21}
              textAnchor="middle"
              className="fill-blue-600 dark:fill-blue-400 text-[11px] font-medium"
            >
              {dueTickLabel(selectedDue)}
            </text>
          </g>
        )}

        {/* milestone guide lines beneath markers; flags render on top below */}
        {visibleMilestones.map((m) => {
          const mx = x(m.target)
          const selected = selectedMilestone === `${m.project}/${m.slug}`
          return (
            <line
              key={`${m.project}/${m.slug}-line`}
              x1={mx} y1={AXIS_H} x2={mx} y2={height}
              className={selected ? 'stroke-violet-600 dark:stroke-violet-400' : 'stroke-violet-500 dark:stroke-violet-500'}
              strokeWidth={selected ? 2 : 1.5}
              strokeDasharray="4 3"
            />
          )
        })}

        {/* connectors from bars to the SELECTED milestone's guide —
            only the selected milestone draws them, so no cross-milestone spaghetti */}
        {selMs && nodes.map((n) =>
          n.milestone === selectedMilestone && n.spanEnd ? (
            <line
              key={`${n.id}-mslink`}
              x1={x(n.spanEnd)} y1={yOf(n.id)} x2={x(selMs.target)} y2={yOf(n.id)}
              className="stroke-violet-400 dark:stroke-violet-500"
              strokeWidth={1}
              strokeDasharray="3 3"
              opacity={0.7}
            />
          ) : null,
        )}

        {/* containment connectors from the selection to its descendants
            — same line idiom as the milestone link, in the selection's colour */}
        {subtree && selectedId && byId.get(selectedId)?.spanEnd && (
          <g className="pointer-events-none">
            {[...subtree].map((id) => {
              const d = byId.get(id)
              if (!d?.spanEnd) return null
              return (
                <line
                  key={`${id}-sublink`}
                  x1={x(byId.get(selectedId)!.spanEnd!)} y1={yOf(selectedId)}
                  x2={x(d.spanEnd)} y2={yOf(id)}
                  className="stroke-blue-400 dark:stroke-blue-500"
                  strokeWidth={1}
                  strokeDasharray="3 3"
                  opacity={0.7}
                />
              )
            })}
          </g>
        )}

        {/* blocker edges beneath the markers */}
        {edges.map((e, idx) => {
          const from = byId.get(e.from)
          const to = byId.get(e.to)
          if (!from?.spanEnd || !to?.spanEnd) return null
          const x1 = x(from.spanEnd) + R
          const y1 = yOf(from.id)
          const x2 = x(to.spanEnd) - R - 2
          const y2 = yOf(to.id)
          const mid = (x1 + x2) / 2
          let cls = e.atRisk
            ? 'stroke-red-500'
            : 'stroke-zinc-300 dark:stroke-zinc-700'
          let opacity = 1
          if (chains) {
            const upEdge = e.to === selectedId || chains.upstream.has(e.to)
            const downEdge = e.from === selectedId || chains.downstream.has(e.from)
            if (upEdge && (e.from === selectedId || chains.upstream.has(e.from))) cls = 'stroke-amber-500'
            else if (downEdge && (e.to === selectedId || chains.downstream.has(e.to))) cls = 'stroke-sky-500'
            else opacity = 0.15
          }
          return (
            <path
              key={idx}
              d={`M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`}
              fill="none"
              strokeWidth={e.atRisk ? 1.8 : 1.2}
              className={cls}
              opacity={opacity}
              markerEnd="url(#tl-arrow)"
            />
          )
        })}
        <defs>
          <marker id="tl-arrow" viewBox="0 0 8 8" refX={7} refY={4} markerWidth={7} markerHeight={7} orient="auto-start-reverse">
            <path d="M 0 0 L 8 4 L 0 8 z" className="fill-current text-zinc-400 dark:text-zinc-600" />
          </marker>
        </defs>

        {nodes.map((n) => (
          <Marker
            key={n.id}
            node={n}
            cx={x(n.spanEnd!)}
            cy={yOf(n.id)}
            spanStartX={x(n.spanStart!)}
            today={today}
            dueSoonDays={dueSoonDays}
            selected={n.id === selectedId}
            dimmed={selectedMilestone ? n.milestone !== selectedMilestone : !related(n.id)}
            colorByPriority={colorByPriority}
            milestoneBadge={
              !showMilestoneBadge || !n.milestone
                ? 'none'
                : n.milestone === selectedMilestone
                  ? 'selected'
                  : 'linked'
            }
            onSelect={onSelect}
          />
        ))}

        {/* milestone flags on top — selectable; stacked into rows so labels never overlap */}
        {visibleMilestones.map((m) => {
          const mx = x(m.target)
          const ref = `${m.project}/${m.slug}`
          const selected = selectedMilestone === ref
          const dropped = m.state === 'dropped'
          const flagY = AXIS_H + 4 + (flagRow.get(ref) ?? 0) * FLAG_ROW_H
          return (
            <g
              key={`${ref}-flag`}
              role="button"
              tabIndex={0}
              aria-label={`Milestone ${m.slug}, target ${m.target}, ${m.state}`}
              className="cursor-pointer focus:outline-none"
              onClick={(e) => {
                e.stopPropagation()
                onSelectMilestone(selected ? null : ref)
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  onSelectMilestone(selected ? null : ref)
                }
              }}
            >
              <title>{`${m.title} — ${m.target} (${m.state})`}</title>
              <Flag
                x={mx + 1} y={flagY} width={11} height={11}
                className={dropped ? 'text-zinc-400 dark:text-zinc-500' : 'text-violet-600 dark:text-violet-400'}
                aria-hidden
              />
              <text
                x={mx + 14} y={flagY + 9.5}
                className={
                  'text-[11px] font-medium ' +
                  (dropped ? 'fill-zinc-400 dark:fill-zinc-500 line-through ' : '') +
                  (selected ? 'fill-violet-700 dark:fill-violet-300' : 'fill-violet-600 dark:fill-violet-400')
                }
              >
                {m.slug}
              </text>
            </g>
          )
        })}
      </svg>
    </div>
  )
}

// The selected item's exact due date — always full (day + month + year), so it
// reads as a date and not as another axis segment label.
function dueTickLabel(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y!, m! - 1, d!).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

interface MarkerProps {
  node: TimelineNode
  cx: number
  cy: number
  spanStartX: number
  today: string
  dueSoonDays: number
  selected: boolean
  dimmed: boolean
  colorByPriority: boolean
  /** 'none' = no link or overlay off; 'linked' = links to some milestone;
   * 'selected' = links to the currently-selected milestone. */
  milestoneBadge: 'none' | 'linked' | 'selected'
  onSelect: (id: string | null) => void
}

// Lean marker — only the glanceable triage channels (state glyph, priority
// fill, due-status ring, at-risk badge) plus a colored type icon + truncated
// title. Everything else textual lives in the selection panel.
function Marker({
  node: n, cx, cy, spanStartX, today, dueSoonDays, selected, dimmed, colorByPriority, milestoneBadge, onSelect,
}: MarkerProps) {
  const closed = n.state === 'done' || n.state === 'dismissed'
  const status = dueStatus(n.spanEnd, today, dueSoonDays, closed)
  const endangered = n.atRisk || n.atRiskBlockers.length > 0 || n.subtreeAtRisk
  const isRollup = n.rolledUp.length > 0

  const ring =
    status === 'overdue' ? 'stroke-red-500'
    : status === 'soon' ? 'stroke-amber-500'
    : 'stroke-zinc-400 dark:stroke-zinc-600'
  const prio = priorityOf(n.priority)
  const fill = (colorByPriority ? priorityMarkerFill(prio) : null) ?? 'fill-white dark:fill-zinc-900'
  const StateIcon = STATE_ICON[n.state]
  // Colored type icon left of the title, matching the tree's identity glyph so
  // an item reads the same across views.
  const TypeIcon = TYPE_ICON[n.type]
  const titleX = cx + R + 5 + TYPE_ICON_W + 2
  // Selected-milestone items get a stronger pull than the small flag: the
  // title goes violet + medium weight so the milestone's membership reads
  // across the whole canvas, not just the badge.
  const msSelected = milestoneBadge === 'selected'
  const overdueDays = status === 'overdue' && n.spanEnd ? dayNumber(today) - dayNumber(n.spanEnd) : 0

  const opacity = dimmed ? 0.4 : closed || n.ghost ? 0.45 : 1
  const label = n.title.length > TITLE_MAX ? `${n.title.slice(0, TITLE_MAX - 1)}…` : n.title
  // Parent breadcrumb, dim, on a line above the title so containment
  // reads without a click. Kept from the *end* (nearest ancestor) when long.
  const crumb = n.parentPath && n.parentPath.length > CRUMB_MAX
    ? `…${n.parentPath.slice(-(CRUMB_MAX - 1))}`
    : n.parentPath

  return (
    <g
      opacity={opacity}
      role="button"
      tabIndex={0}
      aria-label={`${TYPE_LABEL[n.type]} ${n.title}, ${STATE_LABEL[n.state]}${n.spanEnd ? `, due ${n.spanEnd}` : ''}${n.blocked ? ', blocked' : ''}${endangered ? ', at risk' : ''}${milestoneBadge !== 'none' && n.milestone ? `, milestone ${n.milestone}` : ''}`}
      className="cursor-pointer focus:outline-none"
      onClick={(e) => {
        e.stopPropagation()
        onSelect(n.id)
      }}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onSelect(n.id)
        }
      }}
    >
      <title>{n.title}</title>
      {spanStartX < cx - R && (
        <g className="stroke-zinc-300 dark:stroke-zinc-700">
          {/* span bar: earliest due → marker (latest due) */}
          <line x1={spanStartX} y1={cy} x2={cx - R} y2={cy} strokeWidth={3} strokeLinecap="round" />
          {/* start-cap tick so the span reads as a bracket ⊢——● */}
          <line x1={spanStartX} y1={cy - R + 1} x2={spanStartX} y2={cy + R - 1} strokeWidth={2} strokeLinecap="round" />
        </g>
      )}
      {selected && (
        <>
          {/* a gap ring in the page colour first, so the blue reads as a halo
              rather than merging with the marker's own due-status ring */}
          <circle cx={cx} cy={cy} r={R + 3} className="fill-none stroke-white dark:stroke-zinc-950" strokeWidth={3} />
          <circle cx={cx} cy={cy} r={R + 4.5} className="fill-none stroke-blue-500" strokeWidth={2.5} />
        </>
      )}
      {isRollup ? (
        // Rollup anchors get a distinct shape from leaves (square vs circle) —
        // orthogonal to the type icon, which names what kind of item it is.
        <rect
          x={cx - R} y={cy - R} width={R * 2} height={R * 2} rx={3}
          className={`${fill} ${ring}`}
          strokeWidth={status === 'none' ? 1.5 : 2.5}
          strokeDasharray={n.ghost ? '3 2' : undefined}
        />
      ) : (
        <circle
          cx={cx} cy={cy} r={R}
          className={`${fill} ${ring}`}
          strokeWidth={status === 'none' ? 1.5 : 2.5}
          strokeDasharray={n.ghost ? '3 2' : undefined}
        />
      )}
      <StateIcon
        x={cx - 6.5} y={cy - 6.5} width={13} height={13}
        className={
          n.state === 'in-progress'
            ? 'text-yellow-600 dark:text-yellow-400'
            : n.state === 'review'
              ? 'text-violet-600 dark:text-violet-400'
              : n.state === 'done'
                ? 'text-emerald-600 dark:text-emerald-400'
                : 'text-zinc-500 dark:text-zinc-400'
        }
        aria-hidden
      />
      {/* Live blocked flag — top-left badge, mirroring the at-risk badge on
          the right; the reason lives in the details panel. */}
      {n.blocked && (
        <Ban
          x={cx - R - 7} y={cy - R - 8} width={13} height={13}
          className="text-red-600 dark:text-red-400"
          aria-hidden
        />
      )}
      {endangered && (
        <TriangleAlert
          x={cx + 4} y={cy - R - 8} width={13} height={13}
          className="text-red-600 dark:text-red-400"
          aria-hidden
        />
      )}
      {/* milestone-link badge — bottom-left; filled when its milestone
          is the selected one, plain outline otherwise */}
      {milestoneBadge !== 'none' && (
        <Flag
          x={cx - R - 4} y={cy + R - 4} width={12} height={12}
          className={
            milestoneBadge === 'selected'
              ? 'fill-violet-600 text-violet-600 dark:fill-violet-400 dark:text-violet-400'
              : 'text-violet-500 dark:text-violet-400'
          }
          aria-hidden
        />
      )}
      {crumb && (
        <text x={titleX} y={cy - 4} className="fill-zinc-400 dark:fill-zinc-500 text-[11px]">
          {crumb}
        </text>
      )}
      <TypeIcon
        x={cx + R + 5} y={(crumb ? cy + 9 : cy + 5) - 12} width={14} height={14}
        className={TYPE_COLOR[n.type]}
        aria-hidden
      />
      <text
        x={titleX} y={crumb ? cy + 9 : cy + 5}
        className={
          'text-base ' +
          (msSelected
            ? 'fill-violet-700 dark:fill-violet-300 font-medium'
            : 'fill-zinc-700 dark:fill-zinc-300')
        }
      >
        {label}
      </text>
      {overdueDays > 0 && (
        <text x={cx - R} y={cy + R + 13} className="fill-red-600 dark:fill-red-400 text-[11px] font-medium">
          {overdueDays}d overdue
        </text>
      )}
    </g>
  )
}
