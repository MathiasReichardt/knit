import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowRightLeft, CalendarClock, ChevronDown, ChevronsDown, ChevronsUp, ChevronUp, TriangleAlert } from 'lucide-react'
import { AgentSkillsEditor } from './AgentSkillsEditor'
import { TagsEditor } from './TagsEditor'
import { RetypeDialog } from './StructuralDialogs'
import { InlineDate } from './inline/InlineDate'
import { InlineEnum, type InlineEnumOption } from './inline/InlineEnum'
import { InlineText } from './inline/InlineText'
import { ApiError, api, qk, type ReorderPosition } from '../api/client'
import { slatesApi, slatesQk } from '../api/slates'
import type { Item, ItemHealth, ItemState, Priority } from '../api/types'
import {
  HEALTH_COLOR,
  HEALTH_ICON,
  HEALTH_LABEL,
  STATE_COLOR,
  STATE_ICON,
  STATE_LABEL,
  TYPE_COLOR,
  TYPE_ICON,
  TYPE_LABEL,
} from '../lib/glyphs'
import { PRIORITY_ICON, PRIORITY_LABEL, PRIORITY_LEVELS, hasPriorityIcon, priorityOf } from '../lib/priority'
import { patchItemSafe } from '../lib/patchItem'
import { useHandleVocabulary } from '../lib/useHandleVocabulary'
import { dueStatus, dueStatusColor, formatDueDate, todayIso } from '../lib/dueStatus'
import { MetaField, MetaFooter } from './MetaFields'
import { formatShortDate } from '../lib/shortDate'
import { useToasts } from '../stores/toasts'

interface Props {
  item: Item
}

const STATE_OPTIONS: InlineEnumOption<ItemState>[] = (
  ['draft', 'todo', 'in-progress', 'review', 'done', 'dismissed'] as const
).map((s) => ({ value: s, label: STATE_LABEL[s], icon: STATE_ICON[s], iconClass: STATE_COLOR[s] }))

export function FrontmatterStrip({ item }: Props) {
  const created = formatShortDate(item.created)
  const modified = formatShortDate(item.modified)
  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2 text-sm">
        {/* High-signal editable fields lead (State · Type · Priority · Due),
            then the lower-signal ones (Assignee · Effort · Rank · Slate). */}
        <MetaField label="State">
          <StateField item={item} />
        </MetaField>
        <MetaField label="Type">
          <TypeField item={item} />
        </MetaField>
        {/* Health is a project-only signal — rendered only for projects so it
            never occupies a grid cell on ordinary items. */}
        {item.type === 'project' && (
          <MetaField label="Health">
            <HealthField item={item} />
          </MetaField>
        )}
        <MetaField label="Priority">
          <PriorityField item={item} />
        </MetaField>
        <MetaField label="Due">
          <DueField item={item} />
        </MetaField>
        <MetaField label="Assignee">
          <AssigneeField item={item} />
        </MetaField>
        <MetaField label="Effort">
          <ItemTextField
            item={item}
            field="effort"
            label="effort"
            emptyDisplay="—"
            successVerb="Effort updated"
            clearedVerb="Effort cleared"
          />
        </MetaField>
        <MetaField label="Rank">
          <RankField item={item} />
        </MetaField>
        <MetaField label="Slate">
          <SlateField item={item} />
        </MetaField>
        {/* Skills + Tags are the wrap-safe chips fields; both are kept last so
            their wrapping never disturbs the fixed-height rows above. Tags sits
            beside Skills (same treatment) rather than in the single-line grid. */}
        <div className="flex flex-col gap-0.5 min-w-0">
          <span className="text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-500">
            Skills
          </span>
          <AgentSkillsEditor item={item} />
        </div>
        <div className="flex flex-col gap-0.5 min-w-0 col-span-2 sm:col-span-1">
          <span className="text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-500">
            Tags
          </span>
          <TagsEditor item={item} />
        </div>
      </div>
      {/* Read-only provenance is demoted to a compact muted footer rather than
          taking two labeled grid cells — full timestamp on hover. */}
      <MetaFooter
        items={[
          created && <span title={created.full}>created {created.short}</span>,
          modified && <span title={modified.full}>edited {modified.short}</span>,
        ]}
      />
    </div>
  )
}

// Retype validates against parents and children and can cascade, so it keeps
// its dialog (22): the button stays an explicit action, visually distinct from
// the inline values around it.
function TypeField({ item }: { item: Item }) {
  const TypeIcon = TYPE_ICON[item.type]
  const [open, setOpen] = useState(false)
  return (
    <span className="group inline-flex items-center gap-1.5 min-w-0">
      <TypeIcon aria-hidden className={TYPE_COLOR[item.type] + ' h-4 w-4 shrink-0'} />
      <span className="truncate">{TYPE_LABEL[item.type]}</span>
      <button
        type="button"
        onClick={() => setOpen(true)}
        title="Change type (uses retype to validate parent/children)"
        aria-label="Change type"
        className="opacity-0 group-hover:opacity-100 focus:opacity-100 p-0.5 rounded text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800 shrink-0"
      >
        <ArrowRightLeft className="h-3 w-3" />
      </button>
      {open && <RetypeDialog item={item} onClose={() => setOpen(false)} />}
    </span>
  )
}

// A failed write reverts by itself: the value shown is server state, so when
// the PATCH is refused nothing was staged locally to roll back — the field
// simply still reads the old value, and the toast says why.
function useFieldPatch(item: Item, noun: string) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  return async (changes: Record<string, unknown>, success: string) => {
    const res = await patchItemSafe(qc, item.id, changes)
    if (!res.ok) {
      toast('error', `${noun} change failed: ${res.error?.problem?.detail ?? res.error?.message ?? ''}`)
      return
    }
    toast('success', `${success}: ${item.shortId}`)
  }
}

function StateField({ item }: { item: Item }) {
  const patch = useFieldPatch(item, 'State')
  return (
    <InlineEnum
      value={item.state}
      options={STATE_OPTIONS}
      label="state"
      onChange={(next) => void patch({ state: next }, STATE_LABEL[next])}
    />
  )
}

// Project-only. Unset ('') is "no signal" — distinct from on-track — so the
// listbox carries an explicit clear row.
const HEALTH_OPTIONS: InlineEnumOption<ItemHealth | ''>[] = [
  { value: '', label: 'no signal' },
  ...(['on-track', 'at-risk', 'off-track'] as const).map((h) => ({
    value: h,
    label: HEALTH_LABEL[h],
    icon: HEALTH_ICON[h],
    iconClass: HEALTH_COLOR[h],
  })),
]

function HealthField({ item }: { item: Item }) {
  const patch = useFieldPatch(item, 'Health')
  const current = item.health ?? ''
  const HealthIcon = item.health ? HEALTH_ICON[item.health] : null
  return (
    <InlineEnum
      value={current}
      options={HEALTH_OPTIONS}
      label="health"
      onChange={(next) => void patch({ health: next }, next ? HEALTH_LABEL[next] : 'Health cleared')}
    >
      {HealthIcon && <HealthIcon aria-hidden className={HEALTH_COLOR[item.health!] + ' h-4 w-4 shrink-0'} />}
      <span className="truncate">
        {item.health ? HEALTH_LABEL[item.health] : <em className="text-zinc-400">no signal</em>}
      </span>
    </InlineEnum>
  )
}

const PRIORITY_OPTIONS: InlineEnumOption<Priority>[] = PRIORITY_LEVELS.map((p) => ({
  value: p,
  label: PRIORITY_LABEL[p],
  // `normal` is the implicit default and carries no glyph, so a non-default
  // priority stands out in the list exactly as it does on the value.
  icon: hasPriorityIcon(p) ? PRIORITY_ICON[p] : undefined,
  iconClass: 'text-zinc-500 dark:text-zinc-400',
}))

function PriorityField({ item }: { item: Item }) {
  const patch = useFieldPatch(item, 'Priority')
  const current = priorityOf(item.priority)
  return (
    <InlineEnum
      value={current}
      options={PRIORITY_OPTIONS}
      label="priority"
      onChange={(next) => void patch({ priority: next }, PRIORITY_LABEL[next])}
    />
  )
}

function RankField({ item }: { item: Item }) {
  const qc = useQueryClient()
  const toast = useToasts((s) => s.push)
  const [busy, setBusy] = useState(false)
  // Same-parent siblings query: ignores active filters because the move
  // is sibling-relative regardless of what the user has filtered.
  const siblingsQ = useQuery({
    queryKey: qk.itemsFiltered({}),
    queryFn: () => api.items({}),
  })
  const siblings = (siblingsQ.data ?? [])
    .filter((s) => s.id !== item.id && s.parent === (item.parent ?? null) && s.rank)
    .sort((a, b) => (a.rank! < b.rank! ? -1 : a.rank! > b.rank! ? 1 : 0))
  // Logical position among siblings (index of first sibling whose rank
  // sorts AFTER us; equal to siblings.length when we're at the tail or
  // have no rank).
  let after = 0
  while (
    after < siblings.length &&
    (item.rank ?? '') > '' &&
    siblings[after]!.rank! < (item.rank ?? '')
  ) {
    after++
  }
  const before = after - 1
  const atTop = before < 0
  const atBottom = after >= siblings.length
  const lonely = siblings.length === 0

  const move = async (position: ReorderPosition) => {
    setBusy(true)
    try {
      const fetched = await api.fetchItemWithEtag(item.id)
      const res = await api.reorderItem(item.id, position, fetched.etag)
      qc.setQueryData(qk.item(item.id), res.item)
      qc.invalidateQueries({ queryKey: qk.items })
      qc.invalidateQueries({ queryKey: qk.tree })
      toast('success', `Moved ${position}: ${item.shortId}`)
    } catch (e) {
      const msg = e instanceof ApiError ? e.problem?.detail ?? e.message : 'Reorder failed'
      toast('error', `Reorder failed: ${msg}`)
    } finally {
      setBusy(false)
    }
  }

  const btn = (
    pos: ReorderPosition,
    Icon: typeof ChevronUp,
    label: string,
    disabled: boolean,
  ) => (
    <button
      type="button"
      disabled={busy || disabled}
      onClick={() => void move(pos)}
      title={label}
      aria-label={label}
      className={
        'p-0.5 rounded shrink-0 ' +
        (disabled
          ? 'text-zinc-300 dark:text-zinc-700 cursor-not-allowed'
          : 'text-zinc-500 hover:text-blue-600 hover:bg-zinc-200 dark:hover:bg-zinc-800')
      }
    >
      <Icon className="h-3 w-3" />
    </button>
  )

  // Rank is relative, so it is nudged rather than picked — drag-and-drop in the
  // tree is its real surface (22 keeps it out of the inline set).
  return (
    <span className="group inline-flex items-center gap-1 min-w-0">
      <span className="font-mono text-xs truncate" title={`Among ${siblings.length + 1} siblings`}>
        {item.rank || '—'}
      </span>
      <span className="opacity-0 group-hover:opacity-100 focus-within:opacity-100 inline-flex items-center">
        {btn('top', ChevronsUp, 'Move to top', lonely || atTop)}
        {btn('up', ChevronUp, 'Move up', lonely || atTop)}
        {btn('down', ChevronDown, 'Move down', lonely || atBottom)}
        {btn('bottom', ChevronsDown, 'Move to bottom', lonely || atBottom)}
      </span>
    </span>
  )
}

// Options sourced from /api/slates: active + planned in registry order,
// archived under a group heading, plus the item's current value if it's not
// listed (union semantics — a stale value stays selectable).
function SlateField({ item }: { item: Item }) {
  const patch = useFieldPatch(item, 'Slate')
  const slatesQ = useQuery({ queryKey: slatesQk.list, queryFn: slatesApi.list })
  const slates = slatesQ.data ?? []
  const current = item.slate ?? ''

  const options: InlineEnumOption<string>[] = [
    { value: '', label: 'none' },
    ...slates.filter((s) => s.status !== 'archived').map((s) => ({ value: s.name, label: s.name })),
    ...slates
      .filter((s) => s.status === 'archived')
      .map((s) => ({ value: s.name, label: s.name, group: 'archived' })),
  ]
  if (current && !slates.some((s) => s.name === current))
    options.push({ value: current, label: current, group: 'not in the registry' })

  return (
    <InlineEnum
      value={current}
      options={options}
      label="slate"
      onChange={(next) => void patch({ slate: next }, next ? `Slate ${next}` : 'Slate cleared')}
    >
      <span className="truncate">{current || <em className="text-zinc-400">none</em>}</span>
    </InlineEnum>
  )
}

function AssigneeField({ item }: { item: Item }) {
  const handles = useHandleVocabulary()
  return (
    <ItemTextField
      item={item}
      field="assignee"
      label="assignee"
      emptyDisplay={<em className="text-zinc-400">unassigned</em>}
      successVerb="Assigned"
      clearedVerb="Unassigned"
      suggestions={handles}
    />
  )
}

type EditableField = 'assignee' | 'effort'

function ItemTextField({
  item,
  field,
  label,
  emptyDisplay,
  successVerb,
  clearedVerb,
  suggestions,
}: {
  item: Item
  field: EditableField
  label: string
  emptyDisplay: React.ReactNode
  successVerb: string
  clearedVerb: string
  suggestions?: readonly string[]
}) {
  const patch = useFieldPatch(item, label)
  return (
    <InlineText
      value={item[field] ?? ''}
      label={label}
      emptyDisplay={emptyDisplay}
      suggestions={suggestions}
      onCommit={(next) => void patch({ [field]: next }, next ? successVerb : clearedVerb)}
    />
  )
}

function DueField({ item }: { item: Item }) {
  const patch = useFieldPatch(item, 'Due')
  const health = useQuery({ queryKey: qk.health, queryFn: api.health })
  const dueSoonDays = health.data?.dueSoonDays ?? 3
  const current = item.due ?? ''

  const closed = item.state === 'done' || item.state === 'dismissed'
  const status = dueStatus(item.due, todayIso(), dueSoonDays, closed)
  const color = dueStatusColor(status)
  const StatusIcon = status === 'overdue' ? TriangleAlert : CalendarClock

  return (
    <InlineDate
      value={current}
      label="due date"
      onChange={(next) => void patch({ due: next }, next ? 'Due updated' : 'Due cleared')}
    >
      {current ? (
        <span className={'inline-flex items-center gap-1 truncate ' + color} title={current}>
          {status !== 'none' && status !== 'future' && (
            <StatusIcon aria-hidden className="h-3.5 w-3.5 shrink-0" />
          )}
          <span className="truncate">{formatDueDate(current)}</span>
        </span>
      ) : (
        <span className="text-zinc-400">—</span>
      )}
    </InlineDate>
  )
}
