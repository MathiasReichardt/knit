// Pure schedule math for the /timeline view. Day-granular ISO 'YYYY-MM-DD'
// strings throughout (the SPA's date convention, see dueStatus.ts) — no Date
// objects in the domain math, so there is no timezone to get wrong.
//
// Library pick: hand-rolled linear day scale +
// boundary-filtered ticks instead of d3-scale/d3-time. A due-only timeline
// needs exactly `days-since-epoch * pxPerDay` and day/week/month boundaries;
// d3's Date-based scales would reintroduce timezone risk for ~15 kB gz.

import type { ItemType, TimelineEdge, TimelineMilestone, TimelineNode } from '../api/types'

// 'custom' is an explicit from/to pick, carried alongside the range rather
// than in it — one control answers "what window am I looking at", and
// picking a preset is how a custom window is cleared.
export type TimelineRange = 'week' | 'month' | '3m' | 'year' | 'all' | 'custom'
export type TimelineDepth = 'project' | 'epic' | 'feature' | 'all'

// Mirrors the server's TimelineRollup.TypeRank: a type ranked finer than the
// depth is folded into its parent's bar, so filtering to it finds only the
// parentless ones.
const TYPE_RANK: Record<ItemType, number> = { project: 0, epic: 1, feature: 2, bug: 2, task: 3, todo: 3 }
const DEPTH_RANK: Record<TimelineDepth, number> = { project: 0, epic: 1, feature: 2, all: 3 }

/** The item types a depth folds into their parents instead of drawing. */
export function foldedTypes(depth: TimelineDepth): ItemType[] {
  return (Object.keys(TYPE_RANK) as ItemType[]).filter((t) => TYPE_RANK[t] > DEPTH_RANK[depth])
}

/** Days since the Unix epoch for an ISO date (UTC-based, timezone-free). */
export function dayNumber(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number)
  return Math.floor(Date.UTC(y, m - 1, d) / 86_400_000)
}

export function isoFromDayNumber(day: number): string {
  return new Date(day * 86_400_000).toISOString().slice(0, 10)
}

export function addDaysIso(iso: string, days: number): string {
  return isoFromDayNumber(dayNumber(iso) + days)
}

/** Month add with end-of-month clamping (mirrors DateOnly.AddMonths — Jan 31
 * + 1 month = Feb 28, not Mar 3 like raw JS Date arithmetic). */
export function addMonthsIso(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const total = y * 12 + (m - 1) + months
  const ny = Math.floor(total / 12)
  const nm = total - ny * 12 // 0-based
  const lastDay = new Date(Date.UTC(ny, nm + 1, 0)).getUTCDate()
  const nd = Math.min(d, lastDay)
  return `${String(ny).padStart(4, '0')}-${String(nm + 1).padStart(2, '0')}-${String(nd).padStart(2, '0')}`
}

export interface TimelineWindow {
  from: string
  /** null = unbounded ('all'). */
  to: string | null
}

/** An explicitly picked window (range 'custom'). */
export interface CustomWindow {
  from: string
  to: string
}

export interface WindowOptions {
  /** The pick backing range 'custom'. Ignored for the presets. */
  custom?: CustomWindow | null
  /** The overdue back-extension. On by default; off pins the start to
   * today and lets out-of-window overdue work fall into the tray treatment. */
  lookBack?: boolean
}

/**
 * Resolve the render window for a named range: future-focused (start =
 * today), except the start extends back to the earliest overdue node so
 * overdue work is visible on load. Mirrors `knit timeline`. A still-`planned`
 * milestone whose target is in the past is a missed deadline and pulls the
 * start back the same way — reached/dropped ones are closed and never
 * widen the window.
 *
 * Done and dismissed nodes widen it too: they are only in `nodes` when
 * `includeDone` asked for them, and extending for open work alone would fetch
 * completed work and then drop it behind the start.
 *
 * A custom window is returned verbatim — an explicitly picked window
 * that silently grows is a bug, exactly as `--from`/`--to` are never extended
 * in the CLI.
 */
export function computeWindow(
  nodes: readonly TimelineNode[],
  range: TimelineRange,
  today: string,
  milestones: readonly TimelineMilestone[] = [],
  options: WindowOptions = {},
): TimelineWindow {
  const { custom = null, lookBack = true } = options
  if (range === 'custom' && custom) {
    // A reversed pick would draw an empty axis; collapse it to a single day
    // instead, so the canvas keeps rendering while the fields are edited.
    return { from: custom.from, to: custom.to < custom.from ? custom.from : custom.to }
  }
  let to: string | null
  switch (range) {
    case 'week': to = addDaysIso(today, 7); break
    case '3m': to = addMonthsIso(today, 3); break
    case 'year': to = addMonthsIso(today, 12); break
    case 'all': to = null; break
    // 'month', and 'custom' before anything is picked.
    default: to = addMonthsIso(today, 1); break
  }
  let from = today
  if (lookBack) {
    for (const n of nodes) {
      if (n.spanStart && n.spanStart < from) from = n.spanStart
    }
    for (const m of milestones) {
      if (m.state !== 'planned') continue
      if (m.target < from) from = m.target
    }
  }
  return { from, to }
}

/**
 * How many items the overdue look-back is pulling into view — the label on its
 * switch, so the cost of turning it off is visible before it is paid.
 * These are the spans ending before today: with the look-back off the window
 * starts at today and `splitByWindow` drops exactly them. Ghosts are excluded,
 * mirroring the extension rule.
 */
export function lookBackCount(nodes: readonly TimelineNode[], today: string): number {
  let count = 0
  for (const n of nodes) {
    if (!n.ghost && n.spanEnd && n.spanEnd < today) count++
  }
  return count
}

export interface SplitNodes {
  dated: TimelineNode[]
  undated: TimelineNode[]
}

/**
 * Split nodes into the dated set overlapping the window and the undated
 * tray. A node renders when its span touches the window (same rule as the
 * CLI): spanEnd ≥ from and (no upper bound or spanStart ≤ to).
 */
export function splitByWindow(
  nodes: readonly TimelineNode[],
  window: TimelineWindow,
): SplitNodes {
  const dated: TimelineNode[] = []
  const undated: TimelineNode[] = []
  for (const n of nodes) {
    if (!n.spanStart || !n.spanEnd) {
      undated.push(n)
      continue
    }
    if (n.spanEnd >= window.from && (window.to === null || n.spanStart <= window.to)) {
      dated.push(n)
    }
  }
  return { dated, undated }
}

/** Dated milestones whose target lands inside the drawn axis `[from, to]`
 * (inclusive) — the overlay guides. ISO dates compare lexically. */
export function milestonesInWindow(
  milestones: readonly TimelineMilestone[],
  from: string,
  to: string,
): TimelineMilestone[] {
  return milestones.filter((m) => m.target >= from && m.target <= to)
}

/** Axis end for the SVG domain: the window's `to`, or (for 'all') the latest
 * visible span end — never less than two weeks out so the axis has room. In
 * 'all', a dated milestone beyond the last span extends the axis too
 * so its flag isn't clipped off the right edge. */
export function axisEnd(
  dated: readonly TimelineNode[],
  window: TimelineWindow,
  today: string,
  milestones: readonly TimelineMilestone[] = [],
): string {
  if (window.to !== null) return window.to
  let end = addDaysIso(today, 14)
  for (const n of dated) {
    if (n.spanEnd && n.spanEnd > end) end = n.spanEnd
  }
  for (const m of milestones) {
    if (m.target > end) end = m.target
  }
  return end
}

export type SegmentUnit = 'week' | 'month'

/** Longest span still labelled in weeks — beyond it the week ticks crowd. */
export const WEEK_SEGMENT_MAX_DAYS = 120

/**
 * Segment granularity for a drawn span. Span-derived, not
 * range-derived: `custom` has no preset to look up, and a `week` range dragged
 * back five months by overdue work should label what it actually shows. Measure
 * the *unsnapped* span — snapping needs the unit, so taking the unit from the
 * snapped span would be circular.
 *
 * Layout-independent on purpose: the drawn window is snapped to segment
 * boundaries, so deriving the unit from container width or px-per-day would
 * make the window depend on its own rendered width.
 */
export function segmentUnitFor(spanDays: number): SegmentUnit {
  return spanDays > WEEK_SEGMENT_MAX_DAYS ? 'month' : 'week'
}

/** Day number of the Monday on or before `day` (day 0 = 1970-01-01, a Thursday). */
function weekStartDay(day: number): number {
  return day - ((day + 3) % 7)
}

/** Monday on or before the date. */
export function startOfWeekIso(iso: string): string {
  return isoFromDayNumber(weekStartDay(dayNumber(iso)))
}

/** The 1st of the date's month. */
export function startOfMonthIso(iso: string): string {
  return `${iso.slice(0, 7)}-01`
}

function segmentStart(iso: string, unit: SegmentUnit): string {
  return unit === 'week' ? startOfWeekIso(iso) : startOfMonthIso(iso)
}

function nextSegment(iso: string, unit: SegmentUnit): string {
  return unit === 'week' ? addDaysIso(iso, 7) : addMonthsIso(startOfMonthIso(iso), 1)
}

/**
 * Snap both ends of the axis outward to segment boundaries, so every
 * tick is a real calendar boundary and no segment is ever drawn truncated.
 * Costs up to one segment of leading + trailing slack.
 */
export function snapWindowOut(from: string, to: string, unit: SegmentUnit): TimelineWindow & { to: string } {
  const start = segmentStart(from, unit)
  const endStart = segmentStart(to, unit)
  return { from: start, to: endStart === to ? to : nextSegment(endStart, unit) }
}

/**
 * ISO-8601 week number: the week containing the year's first Thursday is W1.
 * Day-number arithmetic, so there is no timezone or locale in it.
 */
export function isoWeekNumber(iso: string): number {
  const thursday = weekStartDay(dayNumber(iso)) + 3
  const year = isoFromDayNumber(thursday).slice(0, 4)
  const firstWeekMonday = weekStartDay(dayNumber(`${year}-01-04`))
  return Math.floor((thursday - 3 - firstWeekMonday) / 7) + 1
}

export interface AxisTick {
  iso: string
  /** Segment label — `W40` for weeks, `Oct` / `Jan 27` for months. */
  label: string
  /** Majors open a larger unit (a new month among weeks, a new year among
   * months) and get a stronger grid line. */
  major: boolean
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/**
 * Segment separators across `[from, to]` — one tick per segment boundary,
 * labeled by the segment it opens. Assumes a window already snapped by
 * {@link snapWindowOut}; an unsnapped one simply starts at the first boundary
 * inside it.
 */
export function segmentTicks(from: string, to: string, unit: SegmentUnit): AxisTick[] {
  const ticks: AxisTick[] = []
  let iso = segmentStart(from, unit)
  if (iso < from) iso = nextSegment(iso, unit)
  while (iso <= to) {
    const month = MONTHS[Number(iso.slice(5, 7)) - 1]!
    if (unit === 'week') {
      // A week that opens a month says so — `W40` alone loses the calendar.
      const opensMonth = iso.slice(0, 7) !== addDaysIso(iso, -7).slice(0, 7)
      ticks.push({
        iso,
        label: opensMonth ? `${month} · W${isoWeekNumber(iso)}` : `W${isoWeekNumber(iso)}`,
        major: opensMonth,
      })
    } else {
      const opensYear = iso.endsWith('-01-01')
      ticks.push({
        iso,
        label: opensYear ? `${month} ${iso.slice(2, 4)}` : month,
        major: opensYear,
      })
    }
    iso = nextSegment(iso, unit)
  }
  return ticks
}

export interface TimelineAxis extends TimelineWindow {
  to: string
  unit: SegmentUnit
}

/**
 * The drawn axis for a range: the requested window (start pulled back to the
 * earliest overdue open item / missed milestone), its end resolved for 'all',
 * then both ends snapped out to segment boundaries.
 *
 * Ghosts (out-of-filter at-risk blockers) render but never widen the window,
 * so they are excluded from the start extension — same rule as the CLI.
 */
export function computeAxis(
  nodes: readonly TimelineNode[],
  range: TimelineRange,
  today: string,
  milestones: readonly TimelineMilestone[] = [],
  options: WindowOptions = {},
): TimelineAxis {
  const window_ = computeWindow(nodes.filter((n) => !n.ghost), range, today, milestones, options)
  // 'all' takes its end from the spans that fall in the window, so the split
  // runs once before snapping; snapping only ever widens, so re-splitting
  // against the snapped axis can only add items.
  const dated = splitByWindow(nodes, window_).dated
  const end = axisEnd(dated, window_, today, milestones)
  const unit = segmentUnitFor(dayNumber(end) - dayNumber(window_.from))
  return { ...snapWindowOut(window_.from, end, unit), unit }
}

export interface LaneSpan {
  id: string
  /** Inclusive extent in any linear unit (px on the rendered axis). */
  start: number
  end: number
}

/**
 * Greedy lane packing for overlapping marker extents: each span lands in
 * the first lane whose previous occupant ends before it starts. Input must
 * be processed in ascending `start` order (sorted here defensively).
 * Returns lane index per id plus the lane count.
 */
export function assignLanes(spans: readonly LaneSpan[]): { lanes: Map<string, number>; laneCount: number } {
  const sorted = [...spans].sort((a, b) => a.start - b.start || a.end - b.end || (a.id < b.id ? -1 : 1))
  const laneEnds: number[] = []
  const lanes = new Map<string, number>()
  for (const s of sorted) {
    let lane = laneEnds.findIndex((e) => e < s.start)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(s.end)
    } else {
      laneEnds[lane] = s.end
    }
    lanes.set(s.id, lane)
  }
  return { lanes, laneCount: Math.max(1, laneEnds.length) }
}

export interface DepChains {
  /** Transitive blockers the item waits on. */
  upstream: Set<string>
  /** Items that (transitively) wait on the item. */
  downstream: Set<string>
}

/** Transitive dependency chains over the blocker edges (`from` blocks `to`),
 * cycle-safe. The selected id itself is in neither set. */
export function depChains(edges: readonly TimelineEdge[], id: string): DepChains {
  const blockersOf = new Map<string, string[]>()
  const blockedBy = new Map<string, string[]>()
  for (const e of edges) {
    const up = blockersOf.get(e.to) ?? []
    up.push(e.from)
    blockersOf.set(e.to, up)
    const down = blockedBy.get(e.from) ?? []
    down.push(e.to)
    blockedBy.set(e.from, down)
  }
  const walk = (adjacency: Map<string, string[]>): Set<string> => {
    const seen = new Set<string>()
    const queue = [...(adjacency.get(id) ?? [])]
    while (queue.length > 0) {
      const next = queue.pop()!
      if (next === id || seen.has(next)) continue
      seen.add(next)
      queue.push(...(adjacency.get(next) ?? []))
    }
    return seen
  }
  return { upstream: walk(blockersOf), downstream: walk(blockedBy) }
}

/** Horizontal room a milestone flag occupies: the icon, its slug label, and a
 * gap. Keep in step with the flag rendering in TimelineCanvas. */
const FLAG_LABEL_X = 14
const FLAG_CHAR_W = 6.2
const FLAG_GAP = 10

/**
 * Row extents for the milestone flags, for packing them into stacked rows
 * (35.x). Two milestones on the same target sit at the same x, so their labels
 * are laid on top of each other unless they are put in different rows — and
 * index parity can't do that job: three same-date milestones would put the
 * first and third back on one row, and flags far enough apart don't need a
 * second row at all.
 */
export function milestoneFlagSpans(
  milestones: readonly TimelineMilestone[],
  x: (iso: string) => number,
): LaneSpan[] {
  return milestones.map((m) => {
    const start = x(m.target)
    return {
      id: `${m.project}/${m.slug}`,
      start,
      end: start + FLAG_LABEL_X + m.slug.length * FLAG_CHAR_W + FLAG_GAP,
    }
  })
}
