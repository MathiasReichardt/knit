import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { ChevronDown, Eraser, SlidersHorizontal, TriangleAlert } from 'lucide-react'
import { api, qk, type TimelineQuery } from '../api/client'
import { projectsApi, projectsQk } from '../api/projects'
import type { TimelineNode } from '../api/types'
import { TimelineCanvas } from '../components/TimelineCanvas'
import { TimelineDetailsPanel, TimelineEmptyPanel, TIMELINE_PANEL_W } from '../components/TimelineDetailsPanel'
import { ResizableSplit } from '../components/ResizableSplit'
import { TimelineMilestonePanel } from '../components/TimelineMilestonePanel'
import { computeAxis, depChains, foldedTypes, lookBackCount, milestonesInWindow, splitByWindow, type TimelineDepth, type TimelineRange } from '../lib/timeline'
import { todayIso } from '../lib/dueStatus'
import { STATE_ICON } from '../lib/glyphs'
import { useShallow } from 'zustand/react/shallow'
import {
  hasTimelineFilter,
  pickFilters,
  timelineFilterQuery,
  useTimelineFilters,
  useTimelineFiltersRepo,
} from '../stores/timelineFilters'
import { FilterHintContext, ItemFilterStoreContext } from '../stores/itemFilterStore'
import {
  AssigneeFilter,
  BlockedTriChip,
  Popover,
  PriorityFilter,
  ScopeChip,
  SlateFilter,
  Switch,
  TagFilter,
  TypeChips,
} from '../components/ItemFilterControls'
import { TimelineUrlSync } from '../components/TimelineUrlSync'
import { timelineSearchSchema } from '../lib/timelineSearch'
import { useTimelinePrefs } from '../stores/timelinePrefs'
import { useViewPrefs } from '../stores/viewPrefs'

export const Route = createFileRoute('/timeline')({
  // `id` selects an item node; `ms` (=`<project>/<slug>`) selects a milestone
  // flag on the overlay. Mutually exclusive — picking one clears the
  // other so only one details panel shows. The rest is the view itself, so a
  // link reproduces it (see lib/timelineSearch).
  validateSearch: timelineSearchSchema,
  component: TimelineScreen,
})

const DEPTHS: Array<[TimelineDepth, string]> = [
  ['project', 'Projects'],
  ['epic', 'Epics'],
  ['feature', 'Features & bugs'],
  ['all', 'All'],
]

const RANGES: Array<[TimelineRange, string]> = [
  ['week', 'Week'],
  ['month', 'Month'],
  ['3m', '3 months'],
  ['year', 'Year'],
  ['all', 'All'],
  ['custom', 'Custom'],
]

function TimelineScreen() {
  const search = Route.useSearch()
  const navigate = useNavigate()
  const selectedId = search.id ?? null
  const selectedMilestone = search.ms ?? null
  const setSelectedId = useCallback(
    (id: string | null) => {
      // Node ↔ milestone selection are mutually exclusive: picking a node
      // clears any selected milestone flag.
      void navigate({
        to: '.',
        search: (prev) => ({ ...prev, id: id ?? undefined, ms: undefined }),
        replace: false,
      })
    },
    [navigate],
  )
  const setSelectedMilestone = useCallback(
    (ref: string | null) => {
      void navigate({
        to: '.',
        search: (prev) => ({ ...prev, ms: ref ?? undefined, id: undefined }),
        replace: false,
      })
    },
    [navigate],
  )

  const depth = useTimelinePrefs((s) => s.depth)
  const range = useTimelinePrefs((s) => s.range)
  const hideDone = useTimelinePrefs((s) => s.hideDone)
  const hideTodos = useTimelinePrefs((s) => s.hideTodos)
  const hideDisconnected = useTimelinePrefs((s) => s.hideDisconnected)
  const showMilestones = useTimelinePrefs((s) => s.showMilestones)
  const customFrom = useTimelinePrefs((s) => s.customFrom)
  const customTo = useTimelinePrefs((s) => s.customTo)
  const showLookBack = useTimelinePrefs((s) => s.showLookBack)
  const setDepth = useTimelinePrefs((s) => s.setDepth)
  const setRange = useTimelinePrefs((s) => s.setRange)
  const setCustomWindow = useTimelinePrefs((s) => s.setCustomWindow)
  const setShowLookBack = useTimelinePrefs((s) => s.setShowLookBack)
  const setHideDone = useTimelinePrefs((s) => s.setHideDone)
  const setHideTodos = useTimelinePrefs((s) => s.setHideTodos)
  const setHideDisconnected = useTimelinePrefs((s) => s.setHideDisconnected)
  const setShowMilestones = useTimelinePrefs((s) => s.setShowMilestones)
  const colorByPriority = useViewPrefs((s) => s.colorByPriority)
  const toggleColorByPriority = useViewPrefs((s) => s.toggleColorByPriority)
  // Filters are restored per repo once /api/root resolves; fetching before
  // that would draw the unfiltered timeline and snap.
  const { ready: filtersReady } = useTimelineFiltersRepo()
  const filters = useTimelineFilters(useShallow(pickFilters))

  const query: TimelineQuery = {
    ...timelineFilterQuery(filters),
    depth: depth !== 'all' ? depth : undefined,
    includeDone: !hideDone || undefined,
    hideTodos: hideTodos || undefined,
    // No-op at depth 'all' server-side, but omit it there so the query key
    // (and cache) doesn't split needlessly.
    hideDisconnected: (depth !== 'all' && hideDisconnected) || undefined,
  }
  const timelineQ = useQuery({
    queryKey: qk.timelineFiltered(query),
    queryFn: () => api.timeline(query),
    enabled: filtersReady,
  })
  const healthQ = useQuery({ queryKey: qk.health, queryFn: api.health })
  const dueSoonDays = healthQ.data?.dueSoonDays ?? 3

  const today = todayIso()
  const nodes = useMemo(() => timelineQ.data?.nodes ?? [], [timelineQ.data])
  const edges = useMemo(() => timelineQ.data?.edges ?? [], [timelineQ.data])
  // Overlay guides only when the toggle is on; the server already
  // narrows the set to the milestone filter when one is active.
  const milestones = useMemo(
    () => (showMilestones ? timelineQ.data?.milestones ?? [] : []),
    [showMilestones, timelineQ.data],
  )

  // Ghosts (out-of-filter at-risk blockers) render, but never widen the
  // window — the overdue back-extension keys off the filtered view. Dated
  // milestones do widen it so a roadmap renders even with no dated
  // items; `milestones` is empty when the overlay toggle is off.
  const windowOptions = useMemo(
    () => ({ custom: { from: customFrom, to: customTo }, lookBack: showLookBack }),
    [customFrom, customTo, showLookBack],
  )
  const axis = useMemo(
    () => computeAxis(nodes, range, today, milestones, windowOptions),
    [nodes, range, today, milestones, windowOptions],
  )
  // What the look-back is buying, for its own label — the spans that
  // end before today, i.e. exactly what turning it off drops from the canvas.
  const lookBackItems = useMemo(() => lookBackCount(nodes, today), [nodes, today])
  const { dated, undated } = useMemo(() => splitByWindow(nodes, axis), [nodes, axis])
  // Render the canvas when either dated items OR dated milestones fall in
  // view — a milestone-only window is a valid roadmap, not "nothing here".
  const hasContent = dated.length > 0 || milestonesInWindow(milestones, axis.from, axis.to).length > 0

  // The tray only shows undated items that are at-risk (or carry risk) — an
  // undated blocker of dated work is the signal this view exists for; plain
  // undated backlog is noise here and lives in the tree/list.
  const riskyUndated = useMemo(
    () => undated.filter((n) => n.atRisk || n.atRiskBlockers.length > 0 || n.subtreeAtRisk),
    [undated],
  )

  const nodesById = useMemo(() => new Map(nodes.map((n) => [n.id, n])), [nodes])
  const selectedNode = selectedId ? nodesById.get(selectedId) ?? null : null
  // What a remaining row can be selected *in place*: a bar on the canvas or a
  // chip in the at-risk tray. Anything else has nothing to select, so the panel
  // opens it in the tree instead.
  const drawnIds = useMemo(
    () => new Set([...dated, ...riskyUndated].map((n) => n.id)),
    [dated, riskyUndated],
  )
  const chains = useMemo(
    () => (selectedId ? depChains(edges, selectedId) : null),
    [edges, selectedId],
  )

  return (
    <div className="h-full flex flex-col">
      <TimelineUrlSync search={search} ready={filtersReady} />
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-3 py-2 border-b border-zinc-200 dark:border-zinc-800 text-xs">
        <Segmented label="Depth" value={depth} options={DEPTHS} onChange={setDepth} />
        <Segmented label="Range" value={range} options={RANGES} onChange={setRange} />
        {range === 'custom' && (
          <CustomWindowFields from={customFrom} to={customTo} onChange={setCustomWindow} />
        )}
        <ProjectPicker />
        <MilestonePicker />
        <ViewOptionsMenu
          depth={depth}
          range={range}
          showLookBack={showLookBack}
          lookBackItems={lookBackItems}
          setShowLookBack={setShowLookBack}
          hideDone={hideDone}
          hideTodos={hideTodos}
          hideDisconnected={hideDisconnected}
          showMilestones={showMilestones}
          colorByPriority={colorByPriority}
          setHideDone={setHideDone}
          setHideTodos={setHideTodos}
          setHideDisconnected={setHideDisconnected}
          setShowMilestones={(v) => {
            setShowMilestones(v)
            if (!v) setSelectedMilestone(null)
          }}
          toggleColorByPriority={toggleColorByPriority}
        />
      </div>
      <TimelineFilterBar depth={depth} />

      <div className="flex flex-1 min-h-0">
        {(() => {
          const canvas = (
            <div className="h-full min-w-0">
              {!filtersReady || timelineQ.isLoading ? (
                <div className="h-full flex items-center justify-center text-sm text-zinc-500">Loading…</div>
              ) : timelineQ.isError ? (
                <div className="h-full flex items-center justify-center text-sm text-red-600 dark:text-red-400">
                  Failed to load the timeline.
                </div>
              ) : !hasContent ? (
                <div className="h-full flex items-center justify-center text-sm text-zinc-500">
                  {showMilestones ? 'No dated items or milestones in this window.' : 'No dated items in this window.'}
                </div>
              ) : (
                <TimelineCanvas
                  nodes={dated}
                  edges={edges}
                  axisFrom={axis.from}
                  axisTo={axis.to}
                  unit={axis.unit}
                  today={today}
                  dueSoonDays={dueSoonDays}
                  selectedId={selectedId}
                  onSelect={setSelectedId}
                  colorByPriority={colorByPriority}
                  milestones={milestones}
                  selectedMilestone={selectedMilestone}
                  onSelectMilestone={setSelectedMilestone}
                />
              )}
            </div>
          )
          const msRef = showMilestones && selectedMilestone ? selectedMilestone : null
          const slash = msRef ? msRef.indexOf('/') : -1
          const panel = selectedNode ? (
            <TimelineDetailsPanel
              node={selectedNode}
              drawnIds={drawnIds}
              today={today}
              dueSoonDays={dueSoonDays}
              onSelect={setSelectedId}
              onClose={() => setSelectedId(null)}
            />
          ) : msRef && slash > 0 ? (
            <TimelineMilestonePanel
              project={msRef.slice(0, slash)}
              slug={msRef.slice(slash + 1)}
              today={today}
              dueSoonDays={dueSoonDays}
              onSelectItem={setSelectedId}
              onClose={() => setSelectedMilestone(null)}
            />
          ) : (
            <TimelineEmptyPanel />
          )
          // One split for either panel — they never show together, and the
          // width is the same preference either way. It is always
          // mounted: a panel that came and went with the selection re-fitted
          // the canvas on every click, moving the bars out from under the
          // pointer. The canvas re-fits from its own ResizeObserver, so it
          // still follows the drag live.
          return (
            <ResizableSplit
              storageKey="timeline.panel"
              resize="right"
              defaultWidth={TIMELINE_PANEL_W}
              left={canvas}
              right={panel}
            />
          )
        })()}
      </div>

      {riskyUndated.length > 0 && (
        <div className="border-t border-zinc-200 dark:border-zinc-800 px-3 py-2 max-h-32 overflow-y-auto">
          <div
            className="text-[11px] uppercase tracking-wide text-zinc-400 dark:text-zinc-500"
            title="Undated items that endanger dated work — without a due date of their own they have no position on the axis, and can never demonstrably finish in time"
          >
            At risk (undated)
          </div>
          <div className="mt-1 flex flex-wrap gap-1.5">
            {riskyUndated.map((n) => (
              <UndatedChip
                key={n.id}
                node={n}
                selected={n.id === selectedId}
                inChain={chains ? chains.upstream.has(n.id) || chains.downstream.has(n.id) : false}
                onSelect={setSelectedId}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// The timeline's headline filter: narrows to items carrying a
// Milestone row targeting the pick. Hidden until at least one milestone exists.
function MilestonePicker() {
  const milestone = useTimelineFilters((s) => s.milestone)
  const setMilestone = useTimelineFilters((s) => s.setMilestone)
  const projectsQ = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const milestones = useMemo(
    () => (projectsQ.data ?? []).flatMap((p) => p.milestones),
    [projectsQ.data],
  )
  if (milestones.length === 0) return null
  return (
    <label
      className="inline-flex items-center gap-1.5"
      title="Filter to items linked to a milestone (the item's Milestone reference)"
    >
      <span className="text-zinc-400 dark:text-zinc-500">Milestone</span>
      <select
        value={milestone ?? ''}
        onChange={(e) => setMilestone(e.target.value || null)}
        aria-label="Filter by milestone"
        className="rounded-md border border-zinc-300 dark:border-zinc-700 bg-transparent px-1.5 py-1 text-zinc-700 dark:text-zinc-300"
      >
        <option value="">All milestones</option>
        {milestones.map((m) => {
          const ref = `${m.project}/${m.slug}`
          return (
            <option key={ref} value={ref}>
              {m.slug}
            </option>
          )
        })}
      </select>
    </label>
  )
}

// The timeline's project filter: narrows to items in a project's tree
// (containment — the item's nearest project-type ancestor points at the doc
// folder). Hidden until at least one project folder exists.
function ProjectPicker() {
  const project = useTimelineFilters((s) => s.project)
  const setProject = useTimelineFilters((s) => s.setProject)
  const projectsQ = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const projects = projectsQ.data ?? []
  if (projects.length === 0) return null
  return (
    <label
      className="inline-flex items-center gap-1.5"
      title="Filter to items contained in a project's tree (by ancestry, not the Milestone link)"
    >
      <span className="text-zinc-400 dark:text-zinc-500">Project</span>
      <select
        value={project ?? ''}
        onChange={(e) => setProject(e.target.value || null)}
        aria-label="Filter by project"
        className="rounded-md border border-zinc-300 dark:border-zinc-700 bg-transparent px-1.5 py-1 text-zinc-700 dark:text-zinc-300"
      >
        <option value="">All projects</option>
        {projects.map((p) => (
          <option key={p.slug} value={p.slug}>
            {p.slug}
          </option>
        ))}
      </select>
    </label>
  )
}

// The timeline's boolean view toggles, collected behind one dropdown so the
// toolbar stays quiet: hide-done, hide-todos, hide-disconnected, the milestone
// overlay, and color-by-priority. Value selectors (depth, range, project,
// milestone) stay in the toolbar — these are on/off view options.
function ViewOptionsMenu({
  depth, range, showLookBack, lookBackItems, setShowLookBack,
  hideDone, hideTodos, hideDisconnected, showMilestones, colorByPriority,
  setHideDone, setHideTodos, setHideDisconnected, setShowMilestones, toggleColorByPriority,
}: {
  depth: TimelineDepth
  range: TimelineRange
  showLookBack: boolean
  lookBackItems: number
  setShowLookBack: (v: boolean) => void
  hideDone: boolean
  hideTodos: boolean
  hideDisconnected: boolean
  showMilestones: boolean
  colorByPriority: boolean
  setHideDone: (v: boolean) => void
  setHideTodos: (v: boolean) => void
  setHideDisconnected: (v: boolean) => void
  setShowMilestones: (v: boolean) => void
  toggleColorByPriority: () => void
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        className="inline-flex items-center gap-1.5 rounded-md border border-zinc-300 dark:border-zinc-700 px-2 py-1 text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
      >
        <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden />
        View
        <ChevronDown className="h-3 w-3 text-zinc-400 dark:text-zinc-500" aria-hidden />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 mt-2 min-w-[13rem] rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-md p-2 flex flex-col z-20"
        >
          <ToggleRow
            label={lookBackItems > 0 ? `Show overdue (${lookBackItems})` : 'Show overdue'}
            checked={showLookBack}
            onChange={setShowLookBack}
            disabled={range === 'custom'}
            title={
              range === 'custom'
                ? 'No effect on a custom window — an explicitly picked window is never extended'
                : 'Extend the window back over items that were due before today. Off pins it to today; anything left behind shows in the at-risk tray.'
            }
          />
          <ToggleRow label="Hide done" checked={hideDone} onChange={setHideDone} />
          <ToggleRow
            label="Hide todos"
            checked={hideTodos}
            onChange={setHideTodos}
            title="Hide todo-typed items — a todo blocking dated work still surfaces as an at-risk blocker"
          />
          <ToggleRow
            label="Hide disconnected"
            checked={hideDisconnected}
            onChange={setHideDisconnected}
            disabled={depth === 'all'}
            title={
              depth === 'all'
                ? 'No effect at depth All — every type is drawn at its own level'
                : 'Hide items whose parent chain never reaches the selected depth rank — a clean "real rank only" view. A hidden at-risk blocker still surfaces as a ghost.'
            }
          />
          <ToggleRow
            label="Milestones"
            checked={showMilestones}
            onChange={setShowMilestones}
            title="Overlay each dated milestone as a vertical guide + flag"
          />
          <ToggleRow
            label="Color by priority"
            checked={colorByPriority}
            onChange={toggleColorByPriority}
          />
        </div>
      )}
    </div>
  )
}

// The two date fields behind range 'custom'. Shown only while 'custom'
// is selected — picking a preset both hides them and clears the window, so
// there is no separate clear button to find.
function CustomWindowFields({
  from, to, onChange,
}: {
  from: string
  to: string
  onChange: (from: string, to: string) => void
}) {
  const cls =
    'rounded-md border border-zinc-300 dark:border-zinc-700 bg-transparent px-1.5 py-1 text-zinc-700 dark:text-zinc-300'
  return (
    <span className="inline-flex items-center gap-1.5" title="Draw an explicit window — never extended back over overdue work">
      <input
        type="date"
        value={from}
        max={to}
        onChange={(e) => e.target.value && onChange(e.target.value, to)}
        aria-label="Window start"
        className={cls}
      />
      <span className="text-zinc-400 dark:text-zinc-500">→</span>
      <input
        type="date"
        value={to}
        min={from}
        onChange={(e) => e.target.value && onChange(from, e.target.value)}
        aria-label="Window end"
        className={cls}
      />
    </span>
  )
}

function ToggleRow({
  label, checked, onChange, disabled, title,
}: {
  label: string
  checked: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
  title?: string
}) {
  return (
    <label
      title={title}
      className={
        'flex items-center gap-2 rounded px-2 py-1.5 ' +
        (disabled
          ? 'text-zinc-300 dark:text-zinc-600 cursor-not-allowed'
          : 'text-zinc-700 dark:text-zinc-300 cursor-pointer hover:bg-zinc-100 dark:hover:bg-zinc-800')
      }
    >
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="accent-blue-600"
      />
      {label}
    </label>
  )
}

function Segmented<T extends string>({
  label, value, options, onChange,
}: {
  label: string
  value: T
  options: Array<[T, string]>
  onChange: (v: T) => void
}) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className="text-zinc-400 dark:text-zinc-500">{label}</span>
      <span className="inline-flex rounded-md border border-zinc-300 dark:border-zinc-700 overflow-hidden">
        {options.map(([v, text]) => (
          <button
            key={v}
            type="button"
            onClick={() => onChange(v)}
            aria-pressed={v === value}
            className={
              'px-2 py-1 ' +
              (v === value
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800')
            }
          >
            {text}
          </button>
        ))}
      </span>
    </span>
  )
}

function UndatedChip({
  node, selected, inChain, onSelect,
}: {
  node: TimelineNode
  selected: boolean
  inChain: boolean
  onSelect: (id: string | null) => void
}) {
  const StateIcon = STATE_ICON[node.state]
  return (
    <button
      type="button"
      onClick={() => onSelect(node.id)}
      title={node.title}
      className={
        'inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-sm max-w-72 ' +
        (selected
          ? 'border-blue-500 ring-1 ring-blue-500 '
          : 'border-red-400 dark:border-red-700 ') +
        (inChain ? 'bg-amber-50 dark:bg-amber-950/40 ' : '') +
        (node.ghost ? 'opacity-50 border-dashed ' : '') +
        'text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800'
      }
    >
      <StateIcon className="h-3.5 w-3.5 shrink-0 text-zinc-400 dark:text-zinc-500" aria-hidden />
      <span className="truncate">{node.title}</span>
      <TriangleAlert className="h-3 w-3 shrink-0 text-red-600 dark:text-red-400" aria-hidden />
    </button>
  )
}


// Filters test the bar's own item, after the rollup — what is folded into a
// bar is never matched, so e.g. someone's tasks under another's epic vanish.
const FOLD_HINT = "Matches the bar's item only, not what is folded into it. Set Depth to All to match every item."

// The timeline's own item filters — independent of the tree/list, so each view
// keeps its own question. The controls are the tree's, bound to this store.
function TimelineFilterBar({ depth }: { depth: TimelineDepth }) {
  const mine = useTimelineFilters((s) => s.mine)
  const setMine = useTimelineFilters((s) => s.setMine)
  const overdue = useTimelineFilters((s) => s.overdue)
  const setOverdue = useTimelineFilters((s) => s.setOverdue)
  const blocked = useTimelineFilters((s) => s.blocked)
  const setBlocked = useTimelineFilters((s) => s.setBlocked)
  const unassigned = useTimelineFilters((s) => s.unassigned)
  const setUnassigned = useTimelineFilters((s) => s.setUnassigned)
  const clearFilters = useTimelineFilters((s) => s.clearFilters)
  const anyFilter = useTimelineFilters(hasTimelineFilter)

  return (
    <ItemFilterStoreContext.Provider value={useTimelineFilters}>
      <FilterHintContext.Provider value={depth === 'all' ? null : FOLD_HINT}>
        <div className="flex flex-wrap items-center gap-1.5 px-3 py-1.5 border-b border-zinc-200 dark:border-zinc-800 text-xs">
          <TypeChips muted={foldedTypes(depth)} mutedReason="folded into its parent at this depth" />
          <span className="mx-1 h-4 w-px bg-zinc-200 dark:bg-zinc-800" />
          <Switch active={mine} onChange={setMine} label="Mine" title="Only items assigned to me" />
          <Switch
            active={overdue}
            onChange={setOverdue}
            label="Overdue"
            title="Only open items past their due date"
          />
          <SlateFilter />
          <TagFilter />
          <Popover
            icon={SlidersHorizontal}
            label="More"
            title="More filters"
            active={blocked !== null || unassigned}
          >
            <BlockedTriChip value={blocked} onChange={setBlocked} />
            <Switch
              active={unassigned}
              onChange={setUnassigned}
              label="Unassigned"
              title="Only items with no assignee"
            />
          </Popover>
          <AssigneeFilter />
          <PriorityFilter />
          <ScopeChip />
          <div className="flex-1" />
          {anyFilter && (
            <button
              type="button"
              onClick={clearFilters}
              title="Clear all timeline filters"
              aria-label="Clear all timeline filters"
              className="inline-flex items-center gap-1 px-2 py-1 rounded text-amber-700 dark:text-amber-300 bg-amber-100/70 dark:bg-amber-950/50 hover:bg-amber-200 dark:hover:bg-amber-900"
            >
              <Eraser className="h-3.5 w-3.5" />
              <span className="text-xs">Filtered</span>
            </button>
          )}
        </div>
      </FilterHintContext.Provider>
    </ItemFilterStoreContext.Provider>
  )
}
