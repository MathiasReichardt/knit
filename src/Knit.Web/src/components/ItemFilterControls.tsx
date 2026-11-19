import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Ban, Check, ChevronDown, Copy, Filter, Flag, Rows3, Settings2, Tag, UserRound, X } from 'lucide-react'
import { api, qk, type BlockedFilter } from '../api/client'
import { useCopy } from '../hooks/useCopy'
import { slatesApi, slatesQk } from '../api/slates'
import type { ItemType, Priority } from '../api/types'
import { useHinted, useItemFilters } from '../stores/itemFilterStore'
import { useDialogs } from '../stores/dialogs'
import { PRIORITY_LABEL, PRIORITY_LEVELS } from '../lib/priority'
import { TYPE_COLOR, TYPE_ICON, TYPE_LABEL, type IconCmp } from '../lib/glyphs'
import { ACTIVE_BG, ACTIVE_PILL, ACTIVE_TOGGLE } from '../lib/activeStyle'

const TYPES: ItemType[] = ['project', 'epic', 'feature', 'bug', 'task', 'todo']

function chip(active: boolean): string {
  return (
    'inline-flex items-center justify-center h-7 w-7 rounded ' +
    (active
      ? ACTIVE_BG
      : 'hover:bg-zinc-100 dark:hover:bg-zinc-900')
  )
}

/** `muted` types stay clickable (a stale pick must be removable) but are dimmed
 * and say why — `mutedReason` is the tooltip. */
export function TypeChips({ muted = [], mutedReason }: { muted?: ItemType[]; mutedReason?: string }) {
  const types = useItemFilters((s) => s.types)
  const toggleType = useItemFilters((s) => s.toggleType)
  return (
    <div className="flex items-center gap-0.5">
      {TYPES.map((t) => {
        const Icon = TYPE_ICON[t]
        const active = types.includes(t)
        const dim = muted.includes(t)
        return (
          <button
            key={t}
            type="button"
            onClick={() => toggleType(t)}
            title={dim && mutedReason ? `${TYPE_LABEL[t]}: ${mutedReason}` : `Type: ${TYPE_LABEL[t]}`}
            aria-label={`Filter type ${TYPE_LABEL[t]}`}
            aria-pressed={active}
            className={chip(active) + (dim ? ' opacity-35' : '')}
          >
            <Icon className={`${TYPE_COLOR[t]} h-3.5 w-3.5`} />
          </button>
        )
      })}
    </div>
  )
}

export function AssigneeFilter() {
  const assignee = useItemFilters((s) => s.assignee)
  const setAssignee = useItemFilters((s) => s.setAssignee)
  // Debounced so we don't re-fetch on every keystroke.
  const [draft, setDraft] = useState(assignee ?? '')
  // External sync: resync the editable draft when the store assignee changes.
  // Not derivable — the draft diverges while typing.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => setDraft(assignee ?? ''), [assignee])
  useEffect(() => {
    const desired = draft.trim() || null
    if (desired === assignee) return
    const t = window.setTimeout(() => setAssignee(desired), 250)
    return () => window.clearTimeout(t)
  }, [draft, assignee, setAssignee])
  const hinted = useHinted()

  return (
    <label className="relative" title={hinted('Only items assigned to this handle')}>
      <UserRound className="absolute left-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-400" />
      <input
        type="text"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        placeholder="Assignee"
        aria-label="Assignee"
        className="w-32 pl-7 pr-2 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
    </label>
  )
}

export function PriorityFilter() {
  const minPriority = useItemFilters((s) => s.minPriority)
  const setMinPriority = useItemFilters((s) => s.setMinPriority)
  const hinted = useHinted()
  return (
    <label
      className="relative inline-flex items-center"
      title={hinted('Only items at or above this priority')}
    >
      <Flag
        aria-hidden
        className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-zinc-400"
      />
      <select
        value={minPriority ?? ''}
        onChange={(e) => setMinPriority((e.target.value || null) as Priority | null)}
        aria-label="Minimum priority"
        className="pl-7 pr-2 py-1 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        <option value="">Any priority</option>
        {PRIORITY_LEVELS.map((p) => (
          <option key={p} value={p}>{PRIORITY_LABEL[p]}</option>
        ))}
      </select>
    </label>
  )
}

export function ScopeChip() {
  const under = useItemFilters((s) => s.under)
  const setUnder = useItemFilters((s) => s.setUnder)
  if (!under) return null
  return (
    <button
      type="button"
      onClick={() => setUnder(null)}
      title="Clear scope"
      aria-label="Clear scope"
      className="inline-flex items-center gap-1 px-2 py-1 rounded bg-blue-100 dark:bg-blue-900/40 text-blue-700 dark:text-blue-200"
    >
      <Filter className="h-3 w-3" />
      Scoped to {under.slice(0, 8)} ×
    </button>
  )
}

// Toolbar popover button: icon + label + active-dot + caret, panel below.
// The active dot signals a set filter without opening.
// Mirrors the HamburgerMenu popover: ref + mousedown outside-click + Esc.
export function Popover({
  icon: Icon,
  label,
  title,
  active,
  dot = active,
  children,
}: {
  icon: IconCmp
  label: string
  title: string
  active: boolean
  // The dot without the fill: "changed from default" rather than "filtering".
  dot?: boolean
  children: ReactNode
}) {
  const hinted = useHinted()
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
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-haspopup="dialog"
        title={hinted(title)}
        className={
          'inline-flex items-center gap-1 px-2 h-7 rounded text-xs ' +
          (active
            ? ACTIVE_TOGGLE
            : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900')
        }
      >
        <Icon className="h-3.5 w-3.5" aria-hidden />
        <span className="truncate max-w-[12rem]">{label}</span>
        {dot && (
          <span aria-hidden className="h-1.5 w-1.5 rounded-full bg-blue-500 dark:bg-blue-400" />
        )}
        <ChevronDown className="h-3 w-3" aria-hidden />
      </button>
      {open && (
        <div
          role="dialog"
          aria-label={title}
          className="absolute left-0 mt-2 z-20 rounded-md border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950 shadow-md p-3 flex flex-col gap-2 text-xs text-zinc-600 dark:text-zinc-300"
        >
          {children}
        </div>
      )}
    </div>
  )
}

// A filter pill that toggles the filter, with a copy icon beside it (shown on
// hover/focus) that puts `value` on the clipboard — the way to hand a scope to
// an agent. The icon slot is always reserved so hovering doesn't reflow the row.
export function CopyableChip({
  label,
  value,
  copyTitle,
  on,
  onToggle,
  title,
  className = '',
}: {
  label: string
  value: string
  copyTitle: string
  on: boolean
  onToggle: () => void
  title?: string
  className?: string
}) {
  const { copied, copy } = useCopy(value)
  return (
    <span className={'group inline-flex items-center min-w-0 max-w-full ' + className}>
      <button
        type="button"
        onClick={onToggle}
        aria-pressed={on}
        title={title ?? label}
        className={
          'truncate rounded-full border px-2 py-0.5 text-[11px] font-mono ' +
          (on
            ? ACTIVE_PILL
            : 'border-zinc-200 dark:border-zinc-700 hover:bg-zinc-100 dark:hover:bg-zinc-900')
        }
      >
        {label}
      </button>
      <button
        type="button"
        onClick={copy}
        title={copied ? 'Copied' : copyTitle}
        aria-label={`${copyTitle} (${value})`}
        className={
          'shrink-0 p-0.5 rounded text-zinc-400 hover:text-blue-600 dark:hover:text-blue-400 ' +
          'group-hover:opacity-100 focus-visible:opacity-100 ' +
          (copied ? 'opacity-100' : 'opacity-0')
        }
      >
        {copied
          ? <Check className="h-3 w-3 text-green-600 dark:text-green-400" aria-hidden />
          : <Copy className="h-3 w-3" aria-hidden />}
      </button>
    </span>
  )
}

// Slate filter (single-select + "No slate"). Matches on the effective
// (inherited) slate server-side: an item is in slate S when it or its
// nearest slate-carrying ancestor sets S — an explicit child value
// re-roots its subtree. The footer opens the manage-slates modal.
export function SlateFilter() {
  const slate = useItemFilters((s) => s.slate)
  const setSlate = useItemFilters((s) => s.setSlate)
  const noSlate = useItemFilters((s) => s.noSlate)
  const setNoSlate = useItemFilters((s) => s.setNoSlate)
  const openManageSlates = useDialogs((s) => s.openManageSlates)
  const slatesQ = useQuery({ queryKey: slatesQk.list, queryFn: slatesApi.list })
  const slates = slatesQ.data ?? []
  const open = slates.filter((s) => s.status !== 'archived')
  const archived = slates.filter((s) => s.status === 'archived')
  const active = slate !== null || noSlate
  const label = slate !== null ? `Slate: ${slate}` : noSlate ? 'Slate: none' : 'Slate'

  return (
    <Popover icon={Rows3} label={label} title="Filter by slate" active={active}>
      <Switch
        active={noSlate}
        onChange={setNoSlate}
        label="No slate"
        title="Only items with no slate (own or inherited)"
      />
      {slates.length > 0 && <div className="border-t border-zinc-200 dark:border-zinc-800" />}
      {(open.length > 0 || archived.length > 0) && (
        <div className="flex flex-wrap items-center gap-1 w-max max-w-[24rem]">
          {[...open, ...archived].map((s) => (
            <CopyableChip
              key={s.name}
              label={s.name}
              value={s.name}
              copyTitle="Copy slate name"
              on={slate === s.name}
              onToggle={() => setSlate(slate === s.name ? null : s.name)}
              title={
                s.status === 'archived'
                  ? `archived${s.notes ? ` — ${s.notes}` : ''}`
                  : (s.notes ?? undefined)
              }
              className={s.status === 'archived' ? 'opacity-60' : ''}
            />
          ))}
        </div>
      )}
      {active && (
        <button
          type="button"
          onClick={() => { setSlate(null); setNoSlate(false) }}
          className="inline-flex items-center gap-1 self-start rounded px-1.5 py-1 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-100 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <X className="h-3 w-3" aria-hidden />
          Clear
        </button>
      )}
      <div className="border-t border-zinc-200 dark:border-zinc-800" />
      <button
        type="button"
        onClick={openManageSlates}
        className="inline-flex items-center gap-1 self-start rounded px-1.5 py-1 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-100 hover:bg-zinc-100 dark:hover:bg-zinc-900"
      >
        <Settings2 className="h-3 w-3" aria-hidden />
        Manage slates…
      </button>
    </Popover>
  )
}

// Free-form tag filter (multi-select, AND). The tag universe is gathered from
// all items (the unfiltered `api.items({})` query the RankField already warms),
// so no dedicated endpoint is needed. Selecting narrows to items carrying every
// picked tag — mirrors the CLI `--tag` semantics.
export function TagFilter() {
  const tags = useItemFilters((s) => s.tags)
  const toggleTag = useItemFilters((s) => s.toggleTag)
  const clearTags = useItemFilters((s) => s.clearTags)
  const itemsQ = useQuery({ queryKey: qk.itemsFiltered({}), queryFn: () => api.items({}) })
  const universe = Array.from(
    new Set((itemsQ.data ?? []).flatMap((i) => i.tags ?? [])),
  ).sort((a, b) => a.localeCompare(b))
  const active = tags.length > 0

  return (
    <Popover
      icon={Tag}
      label={active ? `Tags: ${tags.join(', ')}` : 'Tags'}
      title="Filter by tag"
      active={active}
    >
      {universe.length === 0 ? (
        <span className="italic text-zinc-400">no tags yet</span>
      ) : (
        <div className="flex flex-wrap items-center gap-1 w-max max-w-[24rem]">
          {universe.map((t) => (
            <CopyableChip
              key={t}
              label={t}
              value={t}
              copyTitle="Copy tag"
              on={tags.includes(t)}
              onToggle={() => toggleTag(t)}
            />
          ))}
        </div>
      )}
      {active && (
        <button
          type="button"
          onClick={clearTags}
          className="inline-flex items-center gap-1 self-start rounded px-1.5 py-1 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-100 hover:bg-zinc-100 dark:hover:bg-zinc-900"
        >
          <X className="h-3 w-3" aria-hidden />
          Clear
        </button>
      )}
    </Popover>
  )
}

export function Switch({
  active,
  onChange,
  label,
  title,
}: {
  active: boolean
  onChange: (v: boolean) => void
  label: string
  title: string
}) {
  const hinted = useHinted()
  return (
    <button
      type="button"
      onClick={() => onChange(!active)}
      title={hinted(title)}
      aria-pressed={active}
      className={
        'inline-flex items-center whitespace-nowrap px-2 h-7 rounded text-xs ' +
        (active
          ? ACTIVE_TOGGLE
          : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900')
      }
    >
      {label}
    </button>
  )
}

export function BlockedTriChip({
  value,
  onChange,
}: {
  value: BlockedFilter | null
  onChange: (v: BlockedFilter | null) => void
}) {
  const next = value === null ? 'only' : value === 'only' ? 'hide' : null
  const label =
    value === 'only' ? 'Blocked only' : value === 'hide' ? 'Hide blocked' : 'Blocked: any'
  const title =
    value === 'only'
      ? 'Showing only blocked items — click to hide blocked'
      : value === 'hide'
        ? 'Hiding blocked items — click to clear'
        : 'All items — click to show only blocked'
  const active = value !== null
  const hinted = useHinted()
  return (
    <button
      type="button"
      onClick={() => onChange(next)}
      title={hinted(title)}
      aria-label={label}
      aria-pressed={active}
      className={
        'inline-flex items-center gap-1 px-2 h-7 rounded text-xs ' +
        (active
          ? ACTIVE_TOGGLE
          : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900')
      }
    >
      <Ban className="h-3.5 w-3.5" />
      {value === 'only' ? 'only' : value === 'hide' ? 'hide' : 'any'}
    </button>
  )
}
