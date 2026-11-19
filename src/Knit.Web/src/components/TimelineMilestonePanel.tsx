import { useNavigate } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { CalendarClock, Flag, ListTree, X } from 'lucide-react'
import { projectsApi, projectsQk } from '../api/projects'
import { dayNumber } from '../lib/timeline'
import { dueStatus, dueStatusColor, formatDueDate } from '../lib/dueStatus'
import { TYPE_COLOR, TYPE_ICON } from '../lib/glyphs'

interface Props {
  project: string
  slug: string
  today: string
  dueSoonDays: number
  /** Select a linked item's node on the timeline (dated ones land on the canvas). */
  onSelectItem: (id: string | null) => void
  onClose: () => void
}

const STATE_COLOR: Record<string, string> = {
  reached: 'text-emerald-600 dark:text-emerald-400',
  dropped: 'text-zinc-400 dark:text-zinc-500 line-through',
  planned: 'text-violet-600 dark:text-violet-400',
}

// The milestone-selection surface: the other occupant of the timeline's
// resizable right split, so it shares its width preference. Target /
// state / linked items ride the existing milestone detail endpoint; the
// footer jumps to the sub-doc in the Projects view.
export function TimelineMilestonePanel({ project, slug, today, dueSoonDays, onSelectItem, onClose }: Props) {
  const navigate = useNavigate()
  const q = useQuery({
    queryKey: projectsQk.milestone(project, slug),
    queryFn: () => projectsApi.milestone(project, slug),
    retry: false,
  })
  const m = q.data

  return (
    <aside
      aria-label="Timeline milestone details"
      className="h-full w-full overflow-y-auto bg-white dark:bg-zinc-950 p-4 text-sm"
      onClick={(e) => e.stopPropagation()}
    >
      <div className="flex items-start gap-2">
        <Flag className="h-4 w-4 mt-0.5 shrink-0 text-violet-600 dark:text-violet-400" aria-hidden />
        <div className="min-w-0 flex-1">
          <div className="font-medium text-zinc-900 dark:text-zinc-100 break-words">
            {m?.title ?? slug}
          </div>
          <div className="mt-0.5 text-xs font-mono text-zinc-500 dark:text-zinc-400 truncate">
            {project}/{slug}
          </div>
        </div>
        <button
          type="button"
          onClick={onClose}
          title="Close"
          aria-label="Close milestone details"
          className="shrink-0 inline-flex items-center justify-center h-7 w-7 rounded-md text-zinc-500 hover:bg-zinc-200/70 dark:hover:bg-zinc-800"
        >
          <X className="h-4 w-4" aria-hidden />
        </button>
      </div>

      {q.isLoading ? (
        <div className="mt-4 text-zinc-500">Loading…</div>
      ) : !m ? (
        <div className="mt-4 text-red-600 dark:text-red-400">Milestone not found.</div>
      ) : (
        <>
          <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5">
            <dt className="text-xs uppercase tracking-wide text-zinc-400 dark:text-zinc-500 pt-0.5">State</dt>
            <dd className={`min-w-0 ${STATE_COLOR[m.state] ?? 'text-zinc-700 dark:text-zinc-300'}`}>{m.state}</dd>
            {m.target && (
              <>
                <dt className="text-xs uppercase tracking-wide text-zinc-400 dark:text-zinc-500 pt-0.5">Target</dt>
                <dd className="text-zinc-700 dark:text-zinc-300 min-w-0">
                  <span className="inline-flex items-center gap-1">
                    <CalendarClock className="h-3.5 w-3.5 shrink-0" aria-hidden />
                    <TargetDate target={m.target} today={today} dueSoonDays={dueSoonDays} state={m.state} />
                  </span>
                </dd>
              </>
            )}
          </dl>

          <div className="mt-4">
            <div className="text-xs uppercase tracking-wide text-zinc-400 dark:text-zinc-500 mb-1">
              Linked items ({m.backlinks.length})
            </div>
            {m.backlinks.length === 0 ? (
              <div className="text-zinc-500 italic">Nothing links this milestone yet.</div>
            ) : (
              <ul className="flex flex-col gap-0.5">
                {m.backlinks.map((b) => {
                  const Icon = TYPE_ICON[b.type]
                  return (
                    <li
                      key={b.id}
                      className="flex items-center gap-2 rounded px-1.5 py-1 hover:bg-zinc-100 dark:hover:bg-zinc-900"
                    >
                      <button
                        type="button"
                        onClick={() => onSelectItem(b.id)}
                        title="Select on timeline"
                        className="flex items-center gap-2 min-w-0 flex-1 text-left"
                      >
                        <Icon className={`${TYPE_COLOR[b.type]} h-3.5 w-3.5 shrink-0`} aria-hidden />
                        <span className="truncate">{b.title}</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => void navigate({ to: '/items', search: { id: b.id } })}
                        title="Open in items tree"
                        aria-label="Open in items tree"
                        className="shrink-0 p-1 rounded text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800"
                      >
                        <ListTree className="h-3.5 w-3.5" />
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>

          <button
            type="button"
            onClick={() => void navigate({ to: '/projects', search: { mp: project, ms: slug } })}
            className="mt-4 inline-flex items-center gap-1.5 rounded-md border border-zinc-300 dark:border-zinc-700 px-2.5 py-1.5 text-xs font-medium text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            <Flag className="h-3.5 w-3.5" aria-hidden />
            Open milestone
          </button>
        </>
      )}
    </aside>
  )
}

function TargetDate({
  target, today, dueSoonDays, state,
}: {
  target: string
  today: string
  dueSoonDays: number
  state: string
}) {
  // A reached/dropped milestone is closed — no overdue emphasis.
  const closed = state === 'reached' || state === 'dropped'
  const status = dueStatus(target, today, dueSoonDays, closed)
  return (
    <span className={dueStatusColor(status) || undefined}>
      {formatDueDate(target)}
      {status === 'overdue' && ` — ${dayNumber(today) - dayNumber(target)}d overdue`}
      {status === 'soon' && ` — in ${dayNumber(target) - dayNumber(today)}d`}
    </span>
  )
}
