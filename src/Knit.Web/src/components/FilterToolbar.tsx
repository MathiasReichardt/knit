import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  ArrowDownUp,
  CalendarDays,
  Check,
  Eraser,
  Eye,
  Flag,
  List,
  ListChecks,
  ListOrdered,
  ListTree,
  Maximize2,
  Plus,
  Search,
  SlidersHorizontal,
  TableProperties,
  X,
} from 'lucide-react'
import { projectsApi, projectsQk } from '../api/projects'
import { ApplySortDialog } from './ApplySortDialog'
import { useNavigator, useTreeSort, hasAnyActiveFilter, type LensPreset } from '../stores/navigator'
import type { TreeSort } from '../api/client'
import { isViewCustomized, useViewPrefs, type DueDisplay, type ViewPrefs } from '../stores/viewPrefs'
import { useDialogs } from '../stores/dialogs'
import type { ItemState } from '../api/types'
import { STATE_COLOR, STATE_ICON, STATE_LABEL } from '../lib/glyphs'
import { ACTIVE_BG, ACTIVE_TOGGLE } from '../lib/activeStyle'
import {
  AssigneeFilter,
  BlockedTriChip,
  CopyableChip,
  Popover,
  PriorityFilter,
  ScopeChip,
  SlateFilter,
  Switch,
  TagFilter,
  TypeChips,
} from './ItemFilterControls'

const STATES: ItemState[] = ['draft', 'todo', 'in-progress', 'review', 'done', 'dismissed']

export function FilterToolbar() {
  const view = useNavigator((s) => s.view)
  const setView = useNavigator((s) => s.setView)
  const filterMode = useNavigator((s) => s.filterMode)
  const setFilterMode = useNavigator((s) => s.setFilterMode)
  const treeSort = useTreeSort()
  const setTreeSort = useNavigator((s) => s.setTreeSort)
  const states = useNavigator((s) => s.states)
  const excludeStates = useNavigator((s) => s.excludeStates)
  const cycleState = useNavigator((s) => s.cycleState)
  const open = useNavigator((s) => s.open)
  const setOpen = useNavigator((s) => s.setOpen)
  const mine = useNavigator((s) => s.mine)
  const setMine = useNavigator((s) => s.setMine)
  const ready = useNavigator((s) => s.ready)
  const setReady = useNavigator((s) => s.setReady)
  const query = useNavigator((s) => s.query)
  const setQuery = useNavigator((s) => s.setQuery)
  const under = useNavigator((s) => s.under)
  const clearFilters = useNavigator((s) => s.clearFilters)
  const anyFilter = useNavigator(hasAnyActiveFilter)
  const lens = useNavigator((s) => s.lens)
  const setLens = useNavigator((s) => s.setLens)
  const openNewItem = useDialogs((s) => s.openNewItem)

  // Debounce text inputs so we don't re-fetch on every keystroke.
  const [queryDraft, setQueryDraft] = useState(query)
  // External sync: resync the editable draft when the store query changes
  // (e.g. cleared elsewhere). Not derivable — the draft diverges while typing.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => setQueryDraft(query), [query])
  useEffect(() => {
    if (queryDraft === query) return
    const t = window.setTimeout(() => setQuery(queryDraft), 250)
    return () => window.clearTimeout(t)
  }, [queryDraft, query, setQuery])

  return (
    <div className="flex flex-col gap-1.5 px-2 py-1.5 border-b border-zinc-200 dark:border-zinc-800 text-xs">
      <div className="flex items-center gap-1.5 flex-wrap">
        <ModeToggle view={view} onChange={setView} />
        <LensToggle lens={lens} onChange={setLens} />
        {view === 'tree' && (
          <>
            <FilterModeToggle mode={filterMode} onChange={setFilterMode} />
            <TreeSortMenu sort={treeSort} onChange={setTreeSort} />
          </>
        )}
        <ViewMenu />
        <span className="mx-1 h-4 w-px bg-zinc-200 dark:bg-zinc-800" />
        <TypeChips />
        <span className="mx-1 h-4 w-px bg-zinc-200 dark:bg-zinc-800" />
        <div className="flex items-center gap-0.5">
          {STATES.map((st) => {
            const Icon = STATE_ICON[st]
            const included = states.includes(st)
            const excluded = excludeStates.includes(st)
            const mode: TriMode = included ? 'include' : excluded ? 'exclude' : 'off'
            const title =
              mode === 'include'
                ? `Showing only ${STATE_LABEL[st]} — click to hide`
                : mode === 'exclude'
                  ? `Hiding ${STATE_LABEL[st]} — click to clear`
                  : `State ${STATE_LABEL[st]} — click to show only`
            return (
              <button
                key={st}
                type="button"
                onClick={() => cycleState(st)}
                title={title}
                aria-label={title}
                aria-pressed={mode === 'include' ? true : mode === 'exclude' ? 'mixed' : false}
                className={triChip(mode)}
              >
                <Icon className={`${STATE_COLOR[st]} h-3.5 w-3.5`} />
                {mode === 'exclude' && (
                  <span aria-hidden className="absolute inset-0 flex items-center justify-center">
                    <span className="block w-5 h-px bg-zinc-500 dark:bg-zinc-400 rotate-45" />
                  </span>
                )}
              </button>
            )
          })}
        </div>
        <Switch
          active={open}
          onChange={setOpen}
          label="Open"
          title="Hide done items (shorthand for --exclude-state done)"
        />
        <Switch
          active={mine}
          onChange={setMine}
          label="Mine"
          title="Only items assigned to me"
        />
        <Switch
          active={ready}
          onChange={setReady}
          label="Ready"
          title="Only open, unblocked items"
        />
        <SlateFilter />
        <MilestoneFilter />
        <TagFilter />
        <MoreFilters />
        <div className="flex-1" />
        {anyFilter && (
          // Labelled, not an icon alone: filters are restored per repo on load,
          // so the first impression after a reload must say "filtered"
          // rather than read as an empty repo.
          <button
            type="button"
            onClick={clearFilters}
            title="Clear all filters"
            aria-label="Clear all filters"
            className="inline-flex items-center gap-1 px-2 py-1 rounded text-amber-700 dark:text-amber-300 bg-amber-100/70 dark:bg-amber-950/50 hover:bg-amber-200 dark:hover:bg-amber-900"
          >
            <Eraser className="h-3.5 w-3.5" />
            <span className="text-xs">Filtered</span>
          </button>
        )}
        <button
          type="button"
          onClick={() => openNewItem({ parentId: under })}
          title="New item (n)"
          aria-label="New item"
          className="inline-flex items-center gap-1 px-2 py-1 rounded bg-blue-600 hover:bg-blue-700 text-white"
        >
          <Plus className="h-3.5 w-3.5" />
          New
        </button>
      </div>
      <div className="flex items-center gap-1.5 flex-wrap">
        <label className="relative flex-1 min-w-[180px]">
          <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-400" />
          <input
            type="text"
            value={queryDraft}
            onChange={(e) => setQueryDraft(e.target.value)}
            placeholder="Search titles…"
            aria-label="Title search"
            className="w-full pl-7 pr-2 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </label>
        <AssigneeFilter />
        <DueFilter />
        <PriorityFilter />
        <ScopeChip />
      </div>
    </div>
  )
}

type TriMode = 'off' | 'include' | 'exclude'

function triChip(mode: TriMode): string {
  const base = 'relative inline-flex items-center justify-center h-7 w-7 rounded '
  if (mode === 'include')
    return base + ACTIVE_BG
  if (mode === 'exclude')
    return base + 'bg-zinc-200/70 dark:bg-zinc-800/70 opacity-60'
  return base + 'hover:bg-zinc-100 dark:hover:bg-zinc-900'
}

// Collapses the due-date filters (overdue toggle + the two native
// `<input type="date">` range pickers) behind a popover so the resting toolbar
// shows no `dd.mm.yyyy` mask. Overdue lives here too — it's a
// due-date filter — so one dot covers all three.
function DueFilter() {
  const dueAfter = useNavigator((s) => s.dueAfter)
  const setDueAfter = useNavigator((s) => s.setDueAfter)
  const dueBefore = useNavigator((s) => s.dueBefore)
  const setDueBefore = useNavigator((s) => s.setDueBefore)
  const overdue = useNavigator((s) => s.overdue)
  const setOverdue = useNavigator((s) => s.setOverdue)
  const active = overdue || dueAfter !== null || dueBefore !== null

  return (
    <Popover icon={CalendarDays} label="Due" title="Filter by due date" active={active}>
      <Switch
        active={overdue}
        onChange={setOverdue}
        label="Overdue only"
        title="Only open items past their due date"
      />
      <div className="border-t border-zinc-200 dark:border-zinc-800" />
      <label className="flex items-center justify-between gap-3">
        <span className="whitespace-nowrap">Due after</span>
        <input
          type="date"
          value={dueAfter ?? ''}
          onChange={(e) => setDueAfter(e.target.value || null)}
          title="Only items due strictly after this date"
          aria-label="Due after"
          className="px-1.5 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      </label>
      <label className="flex items-center justify-between gap-3">
        <span className="whitespace-nowrap">Due before</span>
        <input
          type="date"
          value={dueBefore ?? ''}
          onChange={(e) => setDueBefore(e.target.value || null)}
          title="Only items due strictly before this date"
          aria-label="Due before"
          className="px-1.5 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      </label>
      {active && (
        <button
          type="button"
          onClick={() => {
            setOverdue(false)
            setDueAfter(null)
            setDueBefore(null)
          }}
          className="inline-flex items-center gap-1 self-start rounded px-1.5 py-1 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-100 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <X className="h-3 w-3" aria-hidden />
          Clear
        </button>
      )}
    </Popover>
  )
}

// Milestone filter (single-select). Lists every project's milestone sub-docs;
// selecting narrows to items carrying a Milestone row targeting it (server
// resolves the qualified `<project>/<slug>` ref via LinkGraph). Mirrors the CLI
// `--milestone` filter.
function MilestoneFilter() {
  const milestone = useNavigator((s) => s.milestone)
  const setMilestone = useNavigator((s) => s.setMilestone)
  const projectsQ = useQuery({ queryKey: projectsQk.list, queryFn: projectsApi.list })
  const milestones = (projectsQ.data ?? []).flatMap((p) => p.milestones)
  const active = milestone !== null
  // The slug alone is what the chips show; the copied value stays the
  // qualified ref `--milestone` resolves unambiguously.
  const label = milestone !== null ? `Milestone: ${milestone.split('/').pop()}` : 'Milestone'

  return (
    <Popover icon={Flag} label={label} title="Filter by milestone" active={active}>
      {milestones.length === 0 ? (
        <span className="italic text-zinc-400">no milestones yet</span>
      ) : (
        <div className="flex flex-wrap items-center gap-1 w-max max-w-[24rem]">
          {milestones.map((m) => {
            const ref = `${m.project}/${m.slug}`
            return (
              <CopyableChip
                key={ref}
                label={m.slug}
                value={ref}
                copyTitle="Copy milestone ref"
                on={milestone === ref}
                onToggle={() => setMilestone(milestone === ref ? null : ref)}
                title={`${m.title} — ${ref}`}
              />
            )
          })}
        </div>
      )}
      {active && (
        <button
          type="button"
          onClick={() => setMilestone(null)}
          className="inline-flex items-center gap-1 self-start rounded px-1.5 py-1 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-100 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <X className="h-3 w-3" aria-hidden />
          Clear
        </button>
      )}
    </Popover>
  )
}

// Audience lens: an exclusive segmented selector,
// styled like the tree/list toggle — a *view* choice, not a filter (so no blue
// active-highlight). Projects + milestones are always framed; the lens picks the
// leaf band. A per-type chip include still overrides a hidden band
// (buildItemQuery). Coarse counterpart to the per-type chips — this swaps the
// band, a chip drills to one type.
function LensToggle({
  lens,
  onChange,
}: {
  lens: LensPreset
  onChange: (l: LensPreset) => void
}) {
  const opts: { key: LensPreset; label: string; title: string }[] = [
    { key: 'pm', label: 'PM', title: 'PM lens — projects, milestones and todos (no dev items)' },
    { key: 'dev', label: 'Dev', title: 'Dev lens — projects and engineering items (no todos)' },
    { key: 'all', label: 'All', title: 'All — projects, dev items and todos' },
  ]
  return (
    <div
      role="group"
      aria-label="Audience lens"
      className="inline-flex rounded border border-zinc-200 dark:border-zinc-800 overflow-hidden"
    >
      {opts.map((o) => {
        const on = lens === o.key
        return (
          <button
            key={o.key}
            type="button"
            onClick={() => onChange(o.key)}
            title={o.title}
            aria-label={o.title}
            aria-pressed={on}
            className={
              'inline-flex items-center justify-center h-7 px-2 text-xs ' +
              (on
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300')
            }
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

// Low-frequency triage/hygiene filters collected out of the main row.
// Self-labeling chips; the button dot lights when any is set.
function MoreFilters() {
  const blocked = useNavigator((s) => s.blocked)
  const setBlocked = useNavigator((s) => s.setBlocked)
  const unassigned = useNavigator((s) => s.unassigned)
  const setUnassigned = useNavigator((s) => s.setUnassigned)
  const unparented = useNavigator((s) => s.unparented)
  const setUnparented = useNavigator((s) => s.setUnparented)
  const active = blocked !== null || unassigned || unparented

  return (
    <Popover icon={SlidersHorizontal} label="More" title="More filters" active={active}>
      <BlockedTriChip value={blocked} onChange={setBlocked} />
      <Switch
        active={unassigned}
        onChange={setUnassigned}
        label="Unassigned"
        title="Only items with no assignee"
      />
      <Switch
        active={unparented}
        onChange={setUnparented}
        label="Orphans"
        title="Items whose parent doesn't resolve"
      />
    </Popover>
  )
}

type BoolPref = { [K in keyof ViewPrefs]: ViewPrefs[K] extends boolean ? K : never }[keyof ViewPrefs]

const VIEW_SWITCHES: { key: BoolPref; label: string; title: string }[] = [
  { key: 'showSummary', label: 'Show summary', title: 'Completion, blocked and overdue counts above the tree and list' },
  { key: 'showTags', label: 'Show tags', title: 'Tag badges beside each title' },
  { key: 'showIds', label: 'Show ids', title: 'Short id beside each title — click it to copy the full id' },
  { key: 'colorByPriority', label: 'Color by priority', title: 'Tint rows by priority' },
]

function ViewMenu() {
  const prefs = useViewPrefs()
  return (
    <Popover
      icon={Eye}
      label="View"
      title="What the tree and list show"
      active={false}
      dot={isViewCustomized(prefs)}
    >
      {VIEW_SWITCHES.map((v) => (
        <ViewSwitch
          key={v.key}
          on={prefs[v.key]}
          onChange={(on) => prefs.set(v.key, on)}
          label={v.label}
          title={v.title}
        />
      ))}
      <DueSelector value={prefs.due} onChange={(d) => prefs.set('due', d)} />
    </Popover>
  )
}

// Label left, sliding switch right — the same row shape as the Due selector below it.
function ViewSwitch({
  on,
  onChange,
  label,
  title,
}: {
  on: boolean
  onChange: (on: boolean) => void
  label: string
  title: string
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      onClick={() => onChange(!on)}
      title={title}
      className="flex items-center justify-between gap-4 px-2 h-6 rounded whitespace-nowrap hover:bg-zinc-100 dark:hover:bg-zinc-900"
    >
      {label}
      <span
        aria-hidden
        className={
          'relative inline-flex h-4 w-7 shrink-0 rounded-full transition-colors ' +
          (on ? 'bg-zinc-900 dark:bg-zinc-100' : 'bg-zinc-300 dark:bg-zinc-700')
        }
      >
        <span
          className={
            'absolute top-0.5 left-0.5 h-3 w-3 rounded-full bg-white dark:bg-zinc-900 transition-transform ' +
            (on ? 'translate-x-3' : '')
          }
        />
      </span>
    </button>
  )
}

const DUE_OPTIONS: { value: DueDisplay; label: string; title: string }[] = [
  { value: 'all', label: 'All', title: 'Show every due date' },
  { value: 'overdue', label: 'Overdue', title: 'Show only due dates that have passed' },
  { value: 'none', label: 'None', title: 'Hide due dates' },
]

function DueSelector({ value, onChange }: { value: DueDisplay; onChange: (d: DueDisplay) => void }) {
  return (
    <div className="flex items-center justify-between gap-4 px-2">
      <span id="view-due-label" className="whitespace-nowrap">Due dates</span>
      <div
        role="radiogroup"
        aria-labelledby="view-due-label"
        className="inline-flex shrink-0 rounded border border-zinc-200 dark:border-zinc-800 overflow-hidden"
      >
        {DUE_OPTIONS.map((o) => (
          <button
            key={o.value}
            type="button"
            role="radio"
            aria-checked={value === o.value}
            onClick={() => onChange(o.value)}
            title={o.title}
            className={
              'px-2 h-6 whitespace-nowrap ' +
              (value === o.value
                ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
                : 'hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300')
            }
          >
            {o.label}
          </button>
        ))}
      </div>
    </div>
  )
}

function ModeToggle({
  view,
  onChange,
}: {
  view: 'tree' | 'list'
  onChange: (v: 'tree' | 'list') => void
}) {
  return (
    <div className="inline-flex rounded border border-zinc-200 dark:border-zinc-800 overflow-hidden">
      <button
        type="button"
        onClick={() => onChange('tree')}
        title="Tree mode"
        aria-label="Tree mode"
        aria-pressed={view === 'tree'}
        className={
          'inline-flex items-center justify-center h-7 w-8 ' +
          (view === 'tree'
            ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
            : 'hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300')
        }
      >
        <ListTree className="h-3.5 w-3.5" />
      </button>
      <button
        type="button"
        onClick={() => onChange('list')}
        title="List mode"
        aria-label="List mode"
        aria-pressed={view === 'list'}
        className={
          'inline-flex items-center justify-center h-7 w-8 ' +
          (view === 'list'
            ? 'bg-zinc-900 text-white dark:bg-zinc-100 dark:text-zinc-900'
            : 'hover:bg-zinc-100 dark:hover:bg-zinc-900 text-zinc-600 dark:text-zinc-300')
        }
      >
        <List className="h-3.5 w-3.5" />
      </button>
    </div>
  )
}

// A view order, never a write: `state` groups each sibling list by state (done
// on top, dismissed last), rank kept inside a state. Defaults to `tree_sort`.
const TREE_SORTS: { sort: TreeSort; label: string; title: string }[] = [
  { sort: 'rank', label: 'Rank', title: 'The planned order' },
  { sort: 'state', label: 'State', title: 'Done on top, dismissed last, rank within a state' },
]

function TreeSortMenu({ sort, onChange }: { sort: TreeSort; onChange: (s: TreeSort) => void }) {
  const [applyOpen, setApplyOpen] = useState(false)
  const byState = sort === 'state'
  const row =
    'flex items-center gap-2 px-2 h-6 rounded whitespace-nowrap text-left hover:bg-zinc-100 dark:hover:bg-zinc-900'
  return (
    <>
      <Popover
        icon={byState ? ListChecks : ListOrdered}
        label={sort}
        title="How siblings are ordered in the tree"
        active={byState}
      >
        {TREE_SORTS.map((o) => (
          <button
            key={o.sort}
            type="button"
            role="radio"
            aria-checked={sort === o.sort}
            onClick={() => onChange(o.sort)}
            title={o.title}
            className={row + (sort === o.sort ? ' font-semibold text-zinc-900 dark:text-zinc-50' : '')}
          >
            <Check className={'h-3 w-3 ' + (sort === o.sort ? '' : 'invisible')} aria-hidden />
            Sort by {o.label.toLowerCase()}
          </button>
        ))}
        {byState && (
          <>
            <div className="border-t border-zinc-200 dark:border-zinc-800" />
            <button
              type="button"
              onClick={() => setApplyOpen(true)}
              title="Rewrite ranks across the whole tree so rank order matches the state order"
              className={row}
            >
              <ArrowDownUp className="h-3 w-3" aria-hidden />
              Apply state order to ranks…
            </button>
          </>
        )}
      </Popover>
      {applyOpen && <ApplySortDialog onClose={() => setApplyOpen(false)} />}
    </>
  )
}

function FilterModeToggle({
  mode,
  onChange,
}: {
  mode: 'context' | 'flat'
  onChange: (m: 'context' | 'flat') => void
}) {
  const isContext = mode === 'context'
  return (
    <button
      type="button"
      onClick={() => onChange(isContext ? 'flat' : 'context')}
      title={
        isContext
          ? 'Context mode — keep ancestors of matches (dim). Click for flat (matches only).'
          : 'Flat mode — only matches shown. Click for context (dim ancestors).'
      }
      aria-label={isContext ? 'Switch to flat mode' : 'Switch to context mode'}
      aria-pressed={!isContext}
      className={
        'inline-flex items-center gap-1 px-2 h-7 rounded text-xs ' +
        (isContext
          ? 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900'
          : ACTIVE_TOGGLE)
      }
    >
      {isContext ? <TableProperties className="h-3.5 w-3.5" /> : <Maximize2 className="h-3.5 w-3.5" />}
      {isContext ? 'context' : 'flat'}
    </button>
  )
}
