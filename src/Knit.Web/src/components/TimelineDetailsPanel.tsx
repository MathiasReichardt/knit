import type { ReactNode } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Ban, ChevronRight, Crosshair, Filter, Flag, ListTree, TriangleAlert, X } from 'lucide-react'
import { api, qk } from '../api/client'
import type { TimelineNode, TimelineRemaining } from '../api/types'
import { dayNumber } from '../lib/timeline'
import { dueStatus, dueStatusColor, formatDueDate } from '../lib/dueStatus'
import { STATE_COLOR, STATE_ICON, STATE_LABEL, TYPE_COLOR, TYPE_ICON, TYPE_LABEL } from '../lib/glyphs'
import { PRIORITY_ICON, PRIORITY_LABEL, hasPriorityIcon, priorityOf } from '../lib/priority'
import { useTimelineFilters } from '../stores/timelineFilters'

/** Starting width of either timeline selection panel. Resizable, so this is a
 * default and not a constant the canvas can be written against — the canvas
 * re-fits whatever width is left. */
export const TIMELINE_PANEL_W = 336

const ACTION_CLS =
  'inline-flex items-center gap-1.5 rounded-md border border-zinc-300 dark:border-zinc-700 px-2.5 py-1.5 text-xs font-medium text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800'

interface Props {
  node: TimelineNode
  /** Ids that have something to select on the canvas or in the at-risk tray.
   * A remaining row in this set gets an extra "find it here" button; the row
   * itself always opens the tree, whatever the set says. */
  drawnIds: Set<string>
  today: string
  dueSoonDays: number
  onSelect: (id: string | null) => void
  onClose: () => void
}

// The selection surface: everything textual lives here, off the markers. A
// fixed-width flex sibling of the canvas, not an overlay: the canvas re-fits
// the narrower space instead of having its right edge covered.
export function TimelineDetailsPanel({ node, drawnIds, today, dueSoonDays, onSelect, onClose }: Props) {
  const navigate = useNavigate()
  const contextQ = useQuery({ queryKey: qk.context(node.id), queryFn: () => api.itemContext(node.id) })
  const itemQ = useQuery({ queryKey: qk.item(node.id), queryFn: () => api.item(node.id) })

  const TypeIcon = TYPE_ICON[node.type]
  const StateIcon = STATE_ICON[node.state]
  const closed = node.state === 'done' || node.state === 'dismissed'
  const status = dueStatus(node.due, today, dueSoonDays, closed)
  const prio = priorityOf(node.priority)
  const PrioIcon = PRIORITY_ICON[prio]
  const parents = contextQ.data?.parentChain ?? []
  const tags = itemQ.data?.tags ?? []
  const endangered = node.atRiskBlockers.length > 0
  const scoped = useTimelineFilters((s) => s.under === node.id)
  const setUnder = useTimelineFilters((s) => s.setUnder)

  return (
    <aside
      aria-label="Timeline selection details"
      className="h-full w-full overflow-y-auto bg-white dark:bg-zinc-950 p-4 text-sm"
      onClick={(e) => e.stopPropagation()}
    >
      <div className="flex items-start gap-2">
        <TypeIcon className={`h-4 w-4 mt-0.5 shrink-0 ${TYPE_COLOR[node.type]}`} aria-hidden />
        <div className="min-w-0 flex-1">
          <div className="font-medium text-zinc-900 dark:text-zinc-100 break-words">{node.title}</div>
          {parents.length > 0 && (
            <div className="mt-0.5 flex flex-wrap items-center gap-0.5 text-xs text-zinc-500 dark:text-zinc-400">
              {parents.map((p, i) => (
                <span key={p.id} className="inline-flex items-center gap-0.5">
                  {i > 0 && <ChevronRight className="h-3 w-3" aria-hidden />}
                  <span className="truncate max-w-40">{p.title}</span>
                </span>
              ))}
            </div>
          )}
          {node.milestone && (
            <div
              className="mt-0.5 inline-flex items-center gap-1 text-xs text-zinc-500 dark:text-zinc-400"
              title={`Milestone — ${node.milestone}`}
            >
              <Flag className="h-3 w-3 shrink-0" aria-hidden />
              <span className="truncate max-w-48 font-mono">{node.milestone}</span>
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={onClose}
          title="Close"
          aria-label="Close details"
          className="shrink-0 inline-flex items-center justify-center h-7 w-7 rounded-md text-zinc-500 hover:bg-zinc-200/70 dark:hover:bg-zinc-800"
        >
          <X className="h-4 w-4" aria-hidden />
        </button>
      </div>

      <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5">
        <Field label="Type">
          <span className={TYPE_COLOR[node.type]}>{TYPE_LABEL[node.type]}</span>
        </Field>
        <Field label="State">
          <span className={`inline-flex items-center gap-1 ${STATE_COLOR[node.state]}`}>
            <StateIcon className="h-3.5 w-3.5" aria-hidden />
            {STATE_LABEL[node.state]}
          </span>
        </Field>
        {hasPriorityIcon(prio) && (
          <Field label="Priority">
            <span className="inline-flex items-center gap-1">
              <PrioIcon className="h-3.5 w-3.5" aria-hidden />
              {PRIORITY_LABEL[prio]}
            </span>
          </Field>
        )}
        {node.due && (
          <Field label="Due">
            <span className={dueStatusColor(status) || undefined}>
              {formatDueDate(node.due)}
              {status === 'overdue' && ` — ${dayNumber(today) - dayNumber(node.due)}d overdue`}
              {status === 'soon' && ` — in ${dayNumber(node.due) - dayNumber(today)}d`}
            </span>
          </Field>
        )}
        {node.effort && <Field label="Effort">{node.effort}</Field>}
        <Field label="Assignee">
          {node.assignee ?? (
            <span className={node.atRisk || endangered ? 'text-red-600 dark:text-red-400 font-medium' : 'text-zinc-400 dark:text-zinc-500'}>
              unassigned{(node.atRisk || endangered) && '!'}
            </span>
          )}
        </Field>
        {tags.length > 0 && (
          <Field label="Tags">
            <span className="inline-flex flex-wrap items-center gap-1">
              {tags.map((t) => (
                <span
                  key={t}
                  className="inline-flex items-center rounded-full border border-zinc-200 dark:border-zinc-700 px-1.5 py-px text-[10px] font-mono text-zinc-500 dark:text-zinc-400"
                >
                  {t}
                </span>
              ))}
            </span>
          </Field>
        )}
      </dl>

      {node.remaining.length > 0 && (
        <div className="mt-3">
          <div className="text-xs uppercase tracking-wide text-zinc-400 dark:text-zinc-500">
            Remaining {node.remaining.length}
            {node.openBelow > 0 && (
              <span className="normal-case tracking-normal text-zinc-500 dark:text-zinc-400">
                {' '}· {node.openBelow} open below
              </span>
            )}
          </div>
          <ul className="mt-1 space-y-1">
            {node.remaining.map((r) => (
              <RemainingRow
                key={r.id}
                row={r}
                today={today}
                dueSoonDays={dueSoonDays}
                drawn={drawnIds.has(r.id)}
                onSelect={onSelect}
                onOpenInTree={(id) => void navigate({ to: '/items', search: { id } })}
              />
            ))}
          </ul>
        </div>
      )}

      {node.blocked && (
        <div className="mt-3 rounded-md border border-red-300 dark:border-red-900 bg-red-50 dark:bg-red-950/40 p-2.5 text-xs text-red-700 dark:text-red-300">
          <div className="flex items-center gap-1.5 font-medium">
            <Ban className="h-3.5 w-3.5 shrink-0" aria-hidden />
            Blocked
          </div>
          <div className="mt-1 break-words">{node.blocked}</div>
        </div>
      )}

      {(node.atRisk || endangered || node.subtreeAtRisk) && (
        <div className="mt-3 rounded-md border border-red-300 dark:border-red-900 bg-red-50 dark:bg-red-950/40 p-2.5 text-xs text-red-700 dark:text-red-300">
          <div className="flex items-center gap-1.5 font-medium">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden />
            {node.atRisk
              ? node.impliedDeadline
                ? `At risk — needed by ${formatDueDate(node.impliedDeadline)}`
                : 'At risk'
              : node.subtreeAtRisk && !endangered
                ? 'Subtree at risk'
                : 'Deadline endangered'}
          </div>
          {node.subtreeAtRisk && (node.atRisk || endangered) && <div className="mt-1">Subtree at risk</div>}
          {/* The blockers themselves are rows in Remaining — same
              question, one answer. Repeating them here split the list in two. */}
          {endangered && (
            <div className="mt-1">
              {node.atRiskBlockers.length} at-risk blocker
              {node.atRiskBlockers.length === 1 ? '' : 's'} — listed under Remaining
            </div>
          )}
        </div>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => void navigate({ to: '/items', search: { id: node.id } })}
          className={ACTION_CLS}
        >
          <ListTree className="h-3.5 w-3.5" aria-hidden />
          Open in tree
        </button>
        {scoped ? (
          <button type="button" onClick={() => setUnder(null)} className={ACTION_CLS}>
            <Filter className="h-3.5 w-3.5" aria-hidden />
            Clear scope
          </button>
        ) : (
          <button
            type="button"
            onClick={() => setUnder(node.id)}
            title="Show only this item and what is beneath it"
            className={ACTION_CLS}
          >
            <Filter className="h-3.5 w-3.5" aria-hidden />
            Scope timeline here
          </button>
        )}
      </div>
    </aside>
  )
}

// One remaining row: a direct child of the selection, or one of its open
// blockers. No indentation and no expander — drilling down is just selecting
// the row, which shows *its* remaining work the same way.
//
// The row always opens the tree. Jumping to the tree for one row and moving the
// timeline selection for the next — depending on something the user can't see
// (whether that row has a bar in the current window) — made one gesture mean two
// things. Selecting it here is a separate, visible button, shown only when there
// is in fact something on the canvas to select.
function RemainingRow({
  row, today, dueSoonDays, drawn, onSelect, onOpenInTree,
}: {
  row: TimelineRemaining
  today: string
  dueSoonDays: number
  drawn: boolean
  onSelect: (id: string) => void
  onOpenInTree: (id: string) => void
}) {
  const Icon = TYPE_ICON[row.type]
  const closed = row.state === 'done' || row.state === 'dismissed'
  const status = dueStatus(row.due, today, dueSoonDays, closed)
  return (
    <li className={'flex items-start gap-1 ' + (closed ? 'opacity-55' : '')}>
      <button
        type="button"
        onClick={() => onOpenInTree(row.id)}
        title={`Open ${row.title} in tree`}
        className="min-w-0 flex-1 text-left rounded px-1 py-0.5 hover:bg-zinc-100 dark:hover:bg-zinc-800"
      >
        <span className="flex items-center gap-1.5 min-w-0">
          <Icon className={`h-3 w-3 shrink-0 ${TYPE_COLOR[row.type]}`} aria-hidden />
          <span className="truncate">{row.title}</span>
          {row.blocker && (
            <Ban className="h-3 w-3 shrink-0 text-red-600 dark:text-red-400" aria-hidden />
          )}
        </span>
        <span className="mt-0.5 flex items-center gap-2 pl-4.5 text-xs">
          <span className={dueStatusColor(status) || 'text-zinc-400 dark:text-zinc-500 italic'}>
            {row.due ? formatDueDate(row.due) : 'no due'}
          </span>
          {row.openBelow > 0 && (
            <span className="ml-auto shrink-0 text-zinc-500 dark:text-zinc-400">
              {row.openBelow} open
              {row.undatedBelow > 0 && (
                <span className="text-amber-600 dark:text-amber-400"> · {row.undatedBelow} undated</span>
              )}
            </span>
          )}
        </span>
      </button>
      {drawn && (
        <button
          type="button"
          onClick={() => onSelect(row.id)}
          title={`Select ${row.title} on the timeline`}
          aria-label={`Select ${row.title} on the timeline`}
          className="shrink-0 mt-0.5 inline-flex items-center justify-center h-6 w-6 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800"
        >
          <Crosshair className="h-3.5 w-3.5" aria-hidden />
        </button>
      )}
    </li>
  )
}

// The panel is a permanent occupant of the split: a panel that appears
// on select and vanishes on deselect resizes the canvas under the pointer, so
// every click moved the bars you were aiming at.
export function TimelineEmptyPanel() {
  return (
    <aside
      aria-label="Timeline selection details"
      className="h-full w-full overflow-y-auto bg-white dark:bg-zinc-950 p-4 text-sm"
      onClick={(e) => e.stopPropagation()}
    >
      <p className="text-zinc-400 dark:text-zinc-500">
        Select an item or a milestone to see what has to happen for its date to hold.
      </p>
    </aside>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-xs uppercase tracking-wide text-zinc-400 dark:text-zinc-500 pt-0.5">{label}</dt>
      <dd className="text-zinc-700 dark:text-zinc-300 min-w-0">{children}</dd>
    </>
  )
}
