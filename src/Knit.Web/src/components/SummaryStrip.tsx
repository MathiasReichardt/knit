import { useQuery } from '@tanstack/react-query'
import { Ban, Clock, TriangleAlert, UserRound } from 'lucide-react'
import { statusApi, statusQk, type StatusScope, type StatusSummary } from '../api/status'
import { useNavigator } from '../stores/navigator'
import { useViewPrefs } from '../stores/viewPrefs'
import { barFilled, countLabel, headlineParts } from '../lib/status'
import { ACTIVE_TOGGLE } from '../lib/activeStyle'

const BAR_CELLS = 24

/**
 * Aggregate numbers above the tree — the one thing the tree itself can't tell
 * you at a glance ("62% done, 4 blocked, 9 overdue"). Deliberately a strip and
 * not a view: anything that needs a *list* is a filter click, so the three
 * attention counts double as filter buttons.
 *
 * Scope only follows the scope filters (project / milestone / slate / under),
 * never the state chips — a percentage computed after "hide done" is always 0%.
 */
export function SummaryStrip() {
  const show = useViewPrefs((s) => s.showSummary)
  const project = useNavigator((s) => s.project)
  const slate = useNavigator((s) => s.slate)
  const milestone = useNavigator((s) => s.milestone)
  const under = useNavigator((s) => s.under)

  const scope: StatusScope = {
    project: project ?? undefined,
    slate: slate ?? undefined,
    milestone: milestone ?? undefined,
    under: under ?? undefined,
  }
  const q = useQuery({
    queryKey: statusQk.scoped(scope),
    queryFn: () => statusApi.get(scope),
    enabled: show,
  })

  if (!show) return null
  const s = q.data
  if (!s) return null

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-1.5 border-b border-zinc-200 dark:border-zinc-800 text-xs">
      <span className="font-medium text-zinc-700 dark:text-zinc-200">{s.scope}</span>
      <HealthChip health={s.health} />
      {s.total === 0 ? (
        <span className="italic text-zinc-400">no items in scope</span>
      ) : (
        <>
          <Bar percent={s.percent} />
          <span className="font-medium tabular-nums text-zinc-700 dark:text-zinc-200">
            {s.percent}%
          </span>
          <span className="text-zinc-500 dark:text-zinc-400">
            {headlineParts(s).join(' · ')}
          </span>
          <div className="flex-1" />
          <AttentionButtons summary={s} />
        </>
      )}
    </div>
  )
}

function Bar({ percent }: { percent: number }) {
  const filled = barFilled(percent, BAR_CELLS)
  return (
    <span
      role="img"
      aria-label={`${percent} percent done`}
      className="inline-flex items-center gap-px"
    >
      {Array.from({ length: BAR_CELLS }, (_, i) => (
        <span
          key={i}
          aria-hidden
          className={
            'h-2.5 w-1 rounded-[1px] ' +
            (i < filled
              ? 'bg-emerald-500 dark:bg-emerald-400'
              : 'bg-zinc-200 dark:bg-zinc-800')
          }
        />
      ))}
    </span>
  )
}

// Health is a project-level assessment someone made, so it shows whenever set
// (including on-track) — unlike priority, where the implicit default is silent.
function HealthChip({ health }: { health: StatusSummary['health'] }) {
  if (!health) return null
  const tone =
    health === 'off-track'
      ? 'bg-red-100 dark:bg-red-950/50 text-red-700 dark:text-red-300'
      : health === 'at-risk'
        ? 'bg-amber-100 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300'
        : 'bg-emerald-100 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-300'
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 ${tone}`}>
      {health !== 'on-track' && <TriangleAlert className="h-3 w-3" aria-hidden />}
      {health}
    </span>
  )
}

/**
 * The counts that would otherwise need a report. Each one applies the matching
 * filter to the tree below — that's how the strip answers "which ones?"
 * without becoming a second view.
 */
function AttentionButtons({ summary }: { summary: StatusSummary }) {
  const setBlocked = useNavigator((s) => s.setBlocked)
  const setOverdue = useNavigator((s) => s.setOverdue)
  const setUnassigned = useNavigator((s) => s.setUnassigned)
  const blocked = useNavigator((s) => s.blocked)
  const overdue = useNavigator((s) => s.overdue)
  const unassigned = useNavigator((s) => s.unassigned)

  return (
    <div className="flex items-center gap-1">
      <Count
        icon={Ban}
        n={summary.blocked}
        label="blocked"
        active={blocked === 'only'}
        onClick={() => setBlocked(blocked === 'only' ? null : 'only')}
      />
      <Count
        icon={Clock}
        n={summary.overdue}
        label="overdue"
        active={overdue}
        onClick={() => setOverdue(!overdue)}
      />
      <Count
        icon={UserRound}
        n={summary.unassigned}
        label="unassigned"
        active={unassigned}
        onClick={() => setUnassigned(!unassigned)}
      />
    </div>
  )
}

function Count({
  icon: Icon,
  n,
  label,
  active,
  onClick,
}: {
  icon: typeof Ban
  n: number
  label: string
  active: boolean
  onClick: () => void
}) {
  const title = active ? `Showing only ${label} — click to clear` : `Show only ${label} (${n})`
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={n === 0 && !active}
      title={title}
      aria-label={title}
      aria-pressed={active}
      className={
        'inline-flex items-center gap-1 px-1.5 h-6 rounded tabular-nums ' +
        (active
          ? ACTIVE_TOGGLE
          : n === 0
            ? 'text-zinc-400 dark:text-zinc-600'
            : 'text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-900')
      }
    >
      <Icon className="h-3.5 w-3.5" aria-hidden />
      {countLabel(n)}
    </button>
  )
}
